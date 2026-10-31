using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.Sqlite;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Pages;

namespace TacitMemory.Core.Index;

/// <summary>A unit of the index that matched a query: a page section, or the page's title and aliases.</summary>
/// <param name="Section">The section name; <c>null</c> for the title and aliases.</param>
internal sealed record IndexHit(string PageId, string? Section);

/// <summary>Filters applied before matching; dates are inclusive and compared with the page's first and last dates.</summary>
internal sealed record IndexFilter(string? Project, DateOnly? From, DateOnly? To);

/// <summary>
/// The derived index of detail pages in <c>.cache/index.db</c>: one row per page with its file hash, one unit per
/// section and one for the title and aliases, and an FTS5 table over the units. It can be deleted at any time and is
/// rebuilt from the Markdown files. One connection, used by one caller at a time.
/// </summary>
internal sealed class PageIndex : IDisposable
{
    /// <summary>Bump when the tables change; an index with another version is deleted and rebuilt.</summary>
    private const string SchemaVersion = "1";

    private readonly string _memoryPath;
    private readonly string _databasePath;
    private readonly Lock _gate = new();
    private readonly HashSet<string> _skipped = new(StringComparer.Ordinal);
    private SqliteConnection _connection;

    public PageIndex(string memoryPath)
    {
        _memoryPath = memoryPath;
        _databasePath = Path.Combine(memoryPath, ".cache", "index.db");
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        _connection = OpenCurrent();
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

    /// <summary>Brings every page in line with its file: new and changed files are indexed, deleted ones removed.</summary>
    public void SyncAll()
    {
        lock (_gate)
        {
            var onDisk = PageFiles().ToDictionary(file => file.Id.RelativePath);
            var known = _connection.Query<(string Path, string Hash)>("SELECT path, hash FROM pages").ToDictionary(row => row.Path, row => row.Hash);
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
            var known = _connection.QuerySingleOrDefault<string>("SELECT hash FROM pages WHERE path = @path", new { path = id.RelativePath });
            if (File.Exists(fullPath))
                Sync(id, fullPath, known);
            else
            {
                Remove(id.RelativePath);
                _skipped.Remove(id.RelativePath);
            }
        }
    }

    /// <summary>The best <paramref name="limit"/> units for <paramref name="query"/>, best first.</summary>
    public IReadOnlyList<IndexHit> Search(string query, IndexFilter filter, int limit)
    {
        var parameters = new
        {
            project = filter.Project,
            from = filter.From?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            to = filter.To?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            limit,
            match = IndexText.ForQuery(query),
            patterns = IndexText.SingleCharacters(query).Select(c => $"%{c}%").ToList(),
        };
        const string Filters = """
            (@project IS NULL OR p.project = @project)
            AND (@from IS NULL OR (p.last IS NOT NULL AND p.last >= @from))
            AND (@to IS NULL OR (p.first IS NOT NULL AND p.first <= @to))
            """;

        lock (_gate)
        {
            if (parameters.match is not null)
            {
                return _connection.Query<IndexHit>($"""
                    SELECT u.page_id AS PageId, u.section AS Section
                    FROM units_fts JOIN units u ON u.id = units_fts.rowid JOIN pages p ON p.id = u.page_id
                    WHERE units_fts MATCH @match AND {Filters}
                    ORDER BY bm25(units_fts) LIMIT @limit
                    """, parameters).ToList();
            }

            if (parameters.patterns.Count == 0)
                return [];
            var hits = new List<IndexHit>();
            foreach (var pattern in parameters.patterns)
            {
                hits.AddRange(_connection.Query<IndexHit>($"""
                    SELECT u.page_id AS PageId, u.section AS Section
                    FROM units u JOIN pages p ON p.id = u.page_id
                    WHERE instr(u.raw, @character) > 0 AND {Filters}
                    ORDER BY p.last DESC, u.page_id, u.ordinal LIMIT @limit
                    """, new { parameters.project, parameters.from, parameters.to, parameters.limit, character = pattern.Trim('%') }));
            }

            return hits.Distinct().Take(limit).ToList();
        }
    }

    public void Dispose()
    {
        lock (_gate)
            _connection.Dispose();
    }

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
        _connection.Execute(
            "INSERT INTO pages (id, project, path, hash, first, last) VALUES (@id, @project, @path, @hash, @first, @last)",
            new
            {
                id = id.ToString(),
                project = id.Project,
                path = id.RelativePath,
                hash,
                first = header.First?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                last = header.Last?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            },
            transaction);
        AddUnit(id, null, 0, string.Join('\n', [header.Title, .. header.Aliases]), transaction);
        for (var i = 0; i < document.Sections.Count; i++)
            AddUnit(id, document.Sections[i].Name, i + 1, document.Sections[i].Name + "\n" + document.Sections[i].Body, transaction);
        transaction.Commit();
    }

    private void AddUnit(PageId id, string? section, int ordinal, string raw, SqliteTransaction transaction)
    {
        var unit = _connection.ExecuteScalar<long>(
            "INSERT INTO units (page_id, section, ordinal, raw) VALUES (@page, @section, @ordinal, @raw) RETURNING id",
            new { page = id.ToString(), section, ordinal, raw }, transaction);
        _connection.Execute("INSERT INTO units_fts (rowid, text) VALUES (@unit, @text)", new { unit, text = IndexText.ForIndex(raw) }, transaction);
    }

    private void Remove(string relativePath)
    {
        using var transaction = _connection.BeginTransaction();
        RemoveRows(relativePath, transaction);
        transaction.Commit();
    }

    private void RemoveRows(string relativePath, SqliteTransaction transaction)
    {
        var page = _connection.QuerySingleOrDefault<string>("SELECT id FROM pages WHERE path = @relativePath", new { relativePath }, transaction);
        if (page is null)
            return;
        _connection.Execute("DELETE FROM units_fts WHERE rowid IN (SELECT id FROM units WHERE page_id = @page)", new { page }, transaction);
        _connection.Execute("DELETE FROM units WHERE page_id = @page", new { page }, transaction);
        _connection.Execute("DELETE FROM pages WHERE id = @page", new { page }, transaction);
    }

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
            CREATE TABLE pages (id TEXT PRIMARY KEY, project TEXT NOT NULL, path TEXT NOT NULL UNIQUE, hash TEXT NOT NULL, first TEXT, last TEXT);
            CREATE TABLE units (id INTEGER PRIMARY KEY, page_id TEXT NOT NULL, section TEXT, ordinal INTEGER NOT NULL, raw TEXT NOT NULL);
            CREATE INDEX units_page ON units (page_id);
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
