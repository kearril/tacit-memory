using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.Sqlite;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Pages;

namespace TacitMemory.Core.Index;

/// <summary>A unit of the index that matched a query.</summary>
/// <param name="Source">The source the unit hangs off: a detail page's ID.</param>
/// <param name="Kind">The unit's kind, one of the values in <see cref="UnitKinds"/>.</param>
/// <param name="Section">The section name; <c>null</c> for the title and aliases.</param>
/// <param name="Text">The indexed text of the unit: the section name and body, or the title and aliases.</param>
internal sealed record IndexHit(string Source, string Kind, string? Section, string Text);

/// <summary>Filters applied before matching; dates are inclusive and compared with each unit's own first and last dates.</summary>
internal sealed record IndexFilter(string? Project, DateOnly? From, DateOnly? To);

/// <summary>A unit that has no vector yet, with the text to embed.</summary>
internal sealed record PendingUnit(long Id, string Text);

/// <summary>The kinds of source the index holds.</summary>
internal static class SourceKinds
{
    /// <summary>A detail page under <c>pages/</c>.</summary>
    public const string Page = "page";
}

/// <summary>The kinds of unit a source holds.</summary>
internal static class UnitKinds
{
    /// <summary>A section of a detail page.</summary>
    public const string Section = "section";

    /// <summary>The title and aliases of a detail page: the page-level clue.</summary>
    public const string Clue = "clue";
}

/// <summary>
/// The derived index of memory sources in <c>.cache/index.db</c>. A source is an indexed file, now always a detail page:
/// one row per source with its file hash, and one unit per section and one for the title and aliases, each carrying its
/// kind, project and dates. An FTS5 table over the units and a <c>vec0</c> table of their vectors.
/// It can be deleted at any time and is rebuilt from the Markdown files. One connection, used by one caller at a time.
/// </summary>
internal sealed class PageIndex : IDisposable
{
    /// <summary>Bump when the tables change; an index with another version is deleted and rebuilt.</summary>
    private const string SchemaVersion = "3";

    private const string Filters = """
        (@project IS NULL OR u.project = @project)
        AND (@from IS NULL OR (u.last IS NOT NULL AND u.last >= @from))
        AND (@to IS NULL OR (u.first IS NOT NULL AND u.first <= @to))
        """;

    private readonly string _memoryPath;
    private readonly string _databasePath;
    private readonly Lock _gate = new();
    private readonly HashSet<string> _skipped = new(StringComparer.Ordinal);
    private readonly SqliteConnection _connection;

    /// <param name="embeddingModel">The configured embedding model; vectors made by another model are dropped.</param>
    public PageIndex(string memoryPath, string embeddingModel, MemoryLanguage language)
    {
        _memoryPath = memoryPath;
        _databasePath = Path.Combine(memoryPath, ".cache", "index.db");
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        VectorExtension.Verify(language);
        _connection = OpenCurrent();
        if (Meta("embedding_model") != embeddingModel)
        {
            DropVectors();
            SetMeta("embedding_model", embeddingModel);
        }
    }

    /// <summary>Page files that are not UTF-8 and so are not indexed, relative to the memory repository.</summary>
    public IReadOnlyList<string> SkippedFiles
    {
        get
        {
            lock (_gate)
                return _skipped.Order(StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>The length of the stored vectors; <c>null</c> before the embedding model has answered once.</summary>
    public int? Dimension
    {
        get
        {
            lock (_gate)
                return int.TryParse(Meta("dimension"), CultureInfo.InvariantCulture, out var dimension) ? dimension : null;
        }
    }

    /// <summary>Brings every page in line with its file: new and changed files are indexed, deleted ones removed.</summary>
    public void SyncAll()
    {
        lock (_gate)
        {
            var onDisk = PageFiles().ToDictionary(file => file.Id.RelativePath);
            var known = _connection.Query<(string Path, string Hash)>("SELECT path, hash FROM sources").ToDictionary(row => row.Path, row => row.Hash);
            foreach (var path in known.Keys.Where(path => !onDisk.ContainsKey(path)))
                Remove(path);
            foreach (var (path, file) in onDisk)
                Sync(file.Id, file.FullPath, known.GetValueOrDefault(path));
            _skipped.RemoveWhere(path => !onDisk.ContainsKey(path));
        }
    }

    /// <summary>Brings one page in line with its file, which may have been deleted.</summary>
    public void SyncPage(PageId id)
    {
        lock (_gate)
        {
            var fullPath = Path.Combine(_memoryPath, id.RelativePath);
            var known = _connection.QuerySingleOrDefault<string>("SELECT hash FROM sources WHERE path = @path", new { path = id.RelativePath });
            if (File.Exists(fullPath))
                Sync(id, fullPath, known);
            else
            {
                Remove(id.RelativePath);
                _skipped.Remove(id.RelativePath);
            }
        }
    }

    /// <summary>Whether the index has any page in the same project as <paramref name="except"/> other than <paramref name="except"/> itself.</summary>
    public bool HasOtherPagesInProject(PageId except)
    {
        lock (_gate)
            return _connection.ExecuteScalar<long>(
                "SELECT count(*) FROM sources s WHERE s.kind = @kind AND s.id <> @id AND EXISTS (SELECT 1 FROM units u WHERE u.source_id = s.id AND u.project = @project)",
                new { kind = SourceKinds.Page, project = except.Project, id = except.ToString() }) > 0;
    }

    /// <summary>The best <paramref name="limit"/> units for <paramref name="query"/> by full text, best first.</summary>
    public IReadOnlyList<IndexHit> Search(string query, IndexFilter filter, int limit)
    {
        var match = IndexText.ForQuery(query);
        lock (_gate)
        {
            if (match is not null)
            {
                return _connection.Query<IndexHit>($"""
                    SELECT u.source_id AS Source, u.kind AS Kind, u.section AS Section, u.raw AS Text
                    FROM units_fts JOIN units u ON u.id = units_fts.rowid
                    WHERE units_fts MATCH @match AND {Filters}
                    ORDER BY bm25(units_fts) LIMIT @limit
                    """, Parameters(filter, limit, new { match })).ToList();
            }

            var hits = new List<IndexHit>();
            foreach (var character in IndexText.SingleCharacters(query))
            {
                hits.AddRange(_connection.Query<IndexHit>($"""
                    SELECT u.source_id AS Source, u.kind AS Kind, u.section AS Section, u.raw AS Text
                    FROM units u
                    WHERE instr(u.raw, @character) > 0 AND {Filters}
                    ORDER BY u.last DESC, u.source_id, u.ordinal LIMIT @limit
                    """, Parameters(filter, limit, new { character })));
            }

            return hits.Distinct().Take(limit).ToList();
        }
    }

    /// <summary>
    /// The <paramref name="limit"/> units nearest to <paramref name="vector"/> by cosine distance, best first; empty
    /// when no vectors of that length are stored.
    /// </summary>
    public IReadOnlyList<IndexHit> SearchVector(float[] vector, IndexFilter filter, int limit)
    {
        lock (_gate)
        {
            if (Meta("dimension") != vector.Length.ToString(CultureInfo.InvariantCulture))
                return [];
            return _connection.Query<IndexHit>($"""
                SELECT u.source_id AS Source, u.kind AS Kind, u.section AS Section, u.raw AS Text
                FROM units_vec v JOIN units u ON u.id = v.rowid
                WHERE {Filters}
                ORDER BY vec_distance_cosine(v.embedding, @vector) LIMIT @limit
                """, Parameters(filter, limit, new { vector = Blob(vector) })).ToList();
        }
    }

    /// <summary>Up to <paramref name="limit"/> units that still need a vector.</summary>
    public IReadOnlyList<PendingUnit> PendingUnits(int limit)
    {
        lock (_gate)
        {
            var missing = HasVectorTable()
                ? "SELECT id AS Id, raw AS Text FROM units WHERE id NOT IN (SELECT rowid FROM units_vec) ORDER BY id LIMIT @limit"
                : "SELECT id AS Id, raw AS Text FROM units ORDER BY id LIMIT @limit";
            return _connection.Query<PendingUnit>(missing, new { limit }).ToList();
        }
    }

    /// <summary>
    /// Stores vectors for units that still exist with the same text. Vectors of another length than the stored ones
    /// mean the model changed: every stored vector is dropped and the new length recorded.
    /// </summary>
    public void StoreVectors(IReadOnlyList<(PendingUnit Unit, float[] Vector)> vectors)
    {
        if (vectors.Count == 0)
            return;
        lock (_gate)
        {
            var dimension = vectors[0].Vector.Length;
            if (Meta("dimension") != dimension.ToString(CultureInfo.InvariantCulture))
            {
                DropVectors();
                _connection.Execute($"CREATE VIRTUAL TABLE units_vec USING vec0 (embedding float[{dimension}])");
                SetMeta("dimension", dimension.ToString(CultureInfo.InvariantCulture));
            }

            using var transaction = _connection.BeginTransaction();
            foreach (var (unit, vector) in vectors)
            {
                var current = _connection.QuerySingleOrDefault<string>("SELECT raw FROM units WHERE id = @Id", unit, transaction);
                if (current != unit.Text)
                    continue;
                _connection.Execute("DELETE FROM units_vec WHERE rowid = @Id", unit, transaction);
                _connection.Execute("INSERT INTO units_vec (rowid, embedding) VALUES (@id, @vector)", new { id = unit.Id, vector = Blob(vector) }, transaction);
            }

            transaction.Commit();
        }
    }

    /// <summary>Drops every stored vector, for example because the embedding model now answers with another length.</summary>
    public void ResetVectors()
    {
        lock (_gate)
            DropVectors();
    }

    public void Dispose()
    {
        lock (_gate)
            _connection.Dispose();
    }

    private static object Parameters(IndexFilter filter, int limit, object extra)
    {
        var parameters = new DynamicParameters(extra);
        parameters.Add("project", filter.Project);
        parameters.Add("from", filter.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        parameters.Add("to", filter.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        parameters.Add("limit", limit);
        return parameters;
    }

    /// <summary>A vector as <c>vec0</c> reads it: little-endian float32s.</summary>
    private static byte[] Blob(float[] vector) => MemoryMarshal.AsBytes(vector.AsSpan()).ToArray();

    private void Sync(PageId id, string fullPath, string? knownHash)
    {
        var bytes = File.ReadAllBytes(fullPath);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (hash == knownHash)
            return;

        if (TextFile.Read(fullPath) is not { } read)
        {
            Remove(id.RelativePath);
            _skipped.Add(id.RelativePath);
            return;
        }

        _skipped.Remove(id.RelativePath);
        var document = PageDocument.Parse(read.Text, read.HasBom, id.Slug);
        using var transaction = _connection.BeginTransaction();
        RemoveRows(id.RelativePath, transaction);
        var header = document.Header;
        var source = id.ToString();
        _connection.Execute(
            "INSERT INTO sources (id, kind, path, hash) VALUES (@id, @kind, @path, @hash)",
            new { id = source, kind = SourceKinds.Page, path = id.RelativePath, hash },
            transaction);
        var scope = new UnitScope(id.Project, DateText(header.First), DateText(header.Last));
        AddUnit(source, UnitKinds.Clue, null, 0, string.Join('\n', [header.Title, .. header.Aliases]), scope, transaction);
        for (var i = 0; i < document.Sections.Count; i++)
            AddUnit(source, UnitKinds.Section, document.Sections[i].Name, i + 1, document.Sections[i].Name + "\n" + document.Sections[i].Body, scope, transaction);
        transaction.Commit();
    }

    /// <summary>The project and dates every unit of a source carries.</summary>
    private sealed record UnitScope(string Project, string? First, string? Last);

    private void AddUnit(string source, string kind, string? section, int ordinal, string raw, UnitScope scope, SqliteTransaction transaction)
    {
        var unit = _connection.ExecuteScalar<long>(
            "INSERT INTO units (source_id, kind, section, ordinal, project, first, last, raw) VALUES (@source, @kind, @section, @ordinal, @project, @first, @last, @raw) RETURNING id",
            new { source, kind, section, ordinal, project = scope.Project, first = scope.First, last = scope.Last, raw }, transaction);
        _connection.Execute("INSERT INTO units_fts (rowid, text) VALUES (@unit, @text)", new { unit, text = IndexText.ForIndex(raw) }, transaction);
    }

    private static string? DateText(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private void Remove(string relativePath)
    {
        using var transaction = _connection.BeginTransaction();
        RemoveRows(relativePath, transaction);
        transaction.Commit();
    }

    private void RemoveRows(string relativePath, SqliteTransaction transaction)
    {
        var source = _connection.QuerySingleOrDefault<string>("SELECT id FROM sources WHERE path = @relativePath", new { relativePath }, transaction);
        if (source is null)
            return;
        _connection.Execute("DELETE FROM units_fts WHERE rowid IN (SELECT id FROM units WHERE source_id = @source)", new { source }, transaction);
        if (HasVectorTable(transaction))
            _connection.Execute("DELETE FROM units_vec WHERE rowid IN (SELECT id FROM units WHERE source_id = @source)", new { source }, transaction);
        _connection.Execute("DELETE FROM units WHERE source_id = @source", new { source }, transaction);
        _connection.Execute("DELETE FROM sources WHERE id = @source", new { source }, transaction);
    }

    private bool HasVectorTable(SqliteTransaction? transaction = null) =>
        _connection.ExecuteScalar<long>("SELECT count(*) FROM sqlite_master WHERE name = 'units_vec'", transaction: transaction) > 0;

    private void DropVectors()
    {
        _connection.Execute("DROP TABLE IF EXISTS units_vec; DELETE FROM meta WHERE key = 'dimension';");
    }

    private string? Meta(string key) => _connection.QuerySingleOrDefault<string?>("SELECT value FROM meta WHERE key = @key", new { key });

    private void SetMeta(string key, string value) =>
        _connection.Execute("INSERT INTO meta (key, value) VALUES (@key, @value) ON CONFLICT (key) DO UPDATE SET value = excluded.value", new { key, value });

    /// <summary>Page files named as page IDs: <c>pages/&lt;project&gt;/&lt;slug&gt;.md</c>. Other files are not pages.</summary>
    private IEnumerable<(PageId Id, string FullPath)> PageFiles()
    {
        var pages = Path.Combine(_memoryPath, "pages");
        if (!Directory.Exists(pages))
            yield break;
        foreach (var file in Directory.EnumerateFiles(pages, "*.md", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(_memoryPath, file).Replace('\\', '/');
            var parts = relative.Split('/');
            if (parts.Length == 3 && PageId.TryParse($"{parts[1]}/{Path.GetFileNameWithoutExtension(parts[2])}", out var id, out _)
                && id.RelativePath == relative)
                yield return (id, file);
        }
    }

    /// <summary>Opens the index, deleting and recreating it when it is unreadable or of another schema version.</summary>
    private SqliteConnection OpenCurrent()
    {
        SqliteConnection? existing = null;
        try
        {
            existing = Connect();
            var hasMeta = existing.ExecuteScalar<long>("SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'meta'") > 0;
            if (hasMeta && existing.ExecuteScalar<string?>("SELECT value FROM meta WHERE key = 'schema'") == SchemaVersion)
                return existing;
        }
        catch (SqliteException)
        {
            // Not a database: rebuilt below.
        }

        existing?.Dispose();
        foreach (var suffix in (string[])["", "-wal", "-shm", "-journal"])
            File.Delete(_databasePath + suffix);
        var fresh = Connect();
        fresh.Execute($"""
            CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE sources (id TEXT PRIMARY KEY, kind TEXT NOT NULL, path TEXT NOT NULL UNIQUE, hash TEXT NOT NULL);
            CREATE TABLE units (id INTEGER PRIMARY KEY, source_id TEXT NOT NULL, kind TEXT NOT NULL, section TEXT, ordinal INTEGER NOT NULL, project TEXT NOT NULL, first TEXT, last TEXT, raw TEXT NOT NULL);
            CREATE INDEX units_source ON units (source_id);
            CREATE VIRTUAL TABLE units_fts USING fts5 (text, tokenize = 'unicode61');
            INSERT INTO meta (key, value) VALUES ('schema', '{SchemaVersion}');
            """);
        return fresh;
    }

    private SqliteConnection Connect()
    {
        // No pooling: a pooled handle would keep the file open after Dispose and block deleting it.
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _databasePath, Pooling = false }.ToString());
        connection.Open();
        try
        {
            connection.EnableExtensions();
            connection.LoadExtension(VectorExtension.FilePath);
            connection.Execute("PRAGMA journal_mode = WAL;");
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}
