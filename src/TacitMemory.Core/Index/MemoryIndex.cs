using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Pages;
using TacitMemory.Core.Persona;
using TacitMemory.Core.Recent;

namespace TacitMemory.Core.Index;

/// <summary>A unit of the index that matched a query.</summary>
/// <param name="Id">The unit's row ID; the usage statistics count returns by it.</param>
/// <param name="Source">The source the unit hangs off: a detail page's ID, a month file's path, or the path of a persona file.</param>
/// <param name="Kind">The unit's kind, one of the values in <see cref="UnitKinds"/>.</param>
/// <param name="Section">The section name, or for a persona unit the name of the persona section it belongs to; <c>week</c> or <c>month</c> for a summary; for an activity the machine its source comment names; <c>null</c> for the title and aliases and for an activity without a machine.</param>
/// <param name="Text">The indexed text of the unit: a section's name and body, the title and aliases, or an activity's body.</param>
/// <param name="Workstream">The unit's workstream; empty when an activity has none.</param>
/// <param name="Date">An activity's day as yyyy-MM-dd; <c>null</c> for pages.</param>
/// <param name="Code">An activity's short code; <c>null</c> for pages.</param>
/// <param name="Passage">
/// The text the embedding model and the reranker read: a section's page title, name and body; the indexed text for the
/// other kinds of unit.
/// </param>
internal sealed record IndexHit(long Id, string Source, string Kind, string? Section, string Text, string Workstream, string? Date, string? Code, string Passage);

/// <summary>A judgment unit a judgment compares a new text with: its kind, the persona section it belongs to, and its text.</summary>
/// <param name="Kind">The unit's kind, one of the values in <see cref="UnitKinds"/>.</param>
/// <param name="Section">The persona section the unit belongs to; <c>null</c> when the stored name is none of the four.</param>
/// <param name="Text">The unit's text, as the judgment compares it.</param>
/// <param name="Scope">The scope the unit's entry is in: global, or the workstream or domain of its block.</param>
internal sealed record JudgmentHit(string Kind, PersonaSection? Section, string Text, PersonaScope Scope);

/// <summary>
/// Filters applied before matching; dates are inclusive and compared with each unit's own first and last dates.
/// <paramref name="Activities"/> keeps only activities (<c>true</c>) or only pages (<c>false</c>); <c>null</c> keeps both.
/// </summary>
internal sealed record IndexFilter(string? Workstream, DateOnly? From, DateOnly? To, bool? Activities = null);

/// <summary>A unit a search returned, with the score the search ordered it by: BM25 (lower is better) or cosine distance; <c>null</c> when the search had none.</summary>
internal sealed record ScoredHit(IndexHit Hit, double? Score);

/// <summary>How a full-text search matched the query.</summary>
public enum FullTextMethod
{
    /// <summary>The query's words, character pairs and single characters matched through the full-text index, ranked by BM25.</summary>
    Phrase,

    /// <summary>The query held nothing to search for.</summary>
    None,
}

/// <summary>The units a full-text search returned, best first, and how it matched.</summary>
internal sealed record FullTextSearch(FullTextMethod Method, IReadOnlyList<ScoredHit> Hits);

/// <summary>A unit that has no vector yet, with the passage to embed.</summary>
internal sealed record PendingUnit(long Id, string Passage);

/// <summary>What the index holds: its files, its units and the units that have a vector.</summary>
internal sealed record IndexCounts(int Files, int Units, int Vectors);

/// <summary>A page with the total of its units' returns and the last day one was returned, as yyyy-MM-dd or <c>null</c>.</summary>
internal sealed record PageUsageRow(string Page, long Count, string? Last);

/// <summary>The kinds of source the index holds.</summary>
internal static class SourceKinds
{
    /// <summary>A detail page under <c>pages/</c>.</summary>
    public const string Page = "page";

    /// <summary>A month file of the recent activity log, under <c>recent/</c>.</summary>
    public const string ActivityRecord = "activity";

    /// <summary><c>persona.md</c>: its entries are indexed for judgments only.</summary>
    public const string Persona = "persona";

    /// <summary><c>candidates.md</c>: its candidates are indexed for judgments only.</summary>
    public const string Candidates = "candidates";
}

/// <summary>The kinds of unit a source holds.</summary>
internal static class UnitKinds
{
    /// <summary>A section of a detail page.</summary>
    public const string Section = "section";

    /// <summary>The title and aliases of a detail page: the page-level clue.</summary>
    public const string Clue = "clue";

    /// <summary>A dated line of a month file of the recent activity log.</summary>
    public const string Activity = "activity";

    /// <summary>A summary line of the <c>## Summary</c> section of a month file: a retrieval unit of the activity kind.</summary>
    public const string Summary = "summary";

    /// <summary>An entry of <c>persona.md</c>; indexed for judgments only, never returned by search.</summary>
    public const string PersonaEntry = "persona";

    /// <summary>A candidate of <c>candidates.md</c>, named by its text; indexed for judgments only.</summary>
    public const string Candidate = "candidate";

    /// <summary>A candidate in the rejection list of <c>candidates.md</c>; indexed for judgments only.</summary>
    public const string RejectedCandidate = "rejected";
}

/// <summary>
/// The derived index of memory sources in <c>.cache/index.db</c>. A source is an indexed file: a detail page, a month
/// file of the activity log, or one of the two persona files. One row per source with its file hash; one unit per
/// section and one for the title and aliases of a page, one per dated line and one per summary line of a month file, and one per entry of
/// <c>persona.md</c> or candidate of <c>candidates.md</c>, each carrying its kind, workstream and dates. Persona units
/// serve judgments only. An FTS5 table over the units and a table of their vectors, compared by cosine distance in code.
/// It can be deleted at any time and is rebuilt from the Markdown files. One connection, used by one caller at a time.
/// </summary>
internal sealed class MemoryIndex : IDisposable
{
    /// <summary>Bump when the tables change; an index with another version is deleted and rebuilt.</summary>
    private const string SchemaVersion = "12";

    // Persona units serve judgments only: no search returns them.
    private const string Filters = $"""
        (@workstream IS NULL OR u.workstream = @workstream)
        AND (@from IS NULL OR (u.last IS NOT NULL AND u.last >= @from))
        AND (@to IS NULL OR (u.first IS NOT NULL AND u.first <= @to))
        AND (@activities IS NULL OR (u.kind IN ('{UnitKinds.Activity}', '{UnitKinds.Summary}')) = @activities)
        AND u.kind NOT IN ('{UnitKinds.PersonaEntry}', '{UnitKinds.Candidate}', '{UnitKinds.RejectedCandidate}')
        """;

    /// <summary>Every kind of judgment unit: the persona entries, the candidates and the rejected candidates.</summary>
    private static readonly string[] JudgmentKinds = [UnitKinds.PersonaEntry, UnitKinds.Candidate, UnitKinds.RejectedCandidate];

    /// <summary>The columns every search reads into an <see cref="IndexHit"/>.</summary>
    private const string HitColumns = "u.id AS Id, u.source_id AS Source, u.kind AS Kind, u.section AS Section, u.raw AS Text, u.workstream AS Workstream, u.first AS Date, u.code AS Code, u.passage AS Passage";

    private readonly string _memoryPath;
    private readonly string _databasePath;
    private readonly string? _embeddingModel;
    private readonly Lock _gate = new();

    /// <summary>Set when a background sync gave up; the next search syncs every file first.</summary>
    private volatile bool _possiblyStale;
    private readonly HashSet<string> _skipped = new(StringComparer.Ordinal);
    private readonly Func<DateOnly> _today;
    private readonly Func<IReadOnlyDictionary<string, DateOnly>> _commitDays;
    private IReadOnlyDictionary<string, DateOnly>? _commitDayCache;

    /// <summary>
    /// Set when the index was created empty, so the next <see cref="SyncAll"/> is a full build: a section it meets for the
    /// first time then takes the date of its file's last commit instead of today.
    /// </summary>
    private bool _fullBuild;
    private SqliteConnection _connection;

    /// <param name="embeddingModel">
    /// The configured embedding model, or <c>null</c> when none is configured. Vectors made by another model are dropped. With
    /// none configured, the stored vectors and the model name they were made by are kept as they are.
    /// </param>
    /// <param name="today">The local date today, which a section changed now carries.</param>
    /// <param name="commitDays">The last commit date of each page file, keyed by relative path; asked for once per full build.</param>
    public MemoryIndex(
        string memoryPath, string? embeddingModel,
        Func<DateOnly> today, Func<IReadOnlyDictionary<string, DateOnly>> commitDays)
    {
        _memoryPath = memoryPath;
        _databasePath = MemoryLayout.Full(memoryPath, MemoryLayout.IndexDatabase);
        _embeddingModel = embeddingModel;
        _today = today;
        _commitDays = commitDays;
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        _connection = OpenCurrent();
        if (embeddingModel is not null && Meta("embedding_model") != embeddingModel)
        {
            DropVectors();
            SetMeta("embedding_model", embeddingModel);
        }
    }

    /// <summary>Files that are not UTF-8 and so are not indexed, relative to the memory repository.</summary>
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

    /// <summary>
    /// Brings every page, activity month file and persona file in line with its file: new and changed files are indexed,
    /// deleted ones removed.
    /// </summary>
    public void SyncAll()
    {
        lock (_gate)
        {
            var pages = PageFiles().ToDictionary(file => file.Id.RelativePath);
            var months = RecentFile.MonthFiles(_memoryPath).ToDictionary(file => file.RelativePath, file => file.FullPath);
            var known = _connection.Query<(string Path, string Hash)>("SELECT path, hash FROM sources").ToDictionary(row => row.Path, row => row.Hash);
            foreach (var path in known.Keys.Where(path => !pages.ContainsKey(path) && !months.ContainsKey(path) && !IsJudgmentFile(path)))
                Remove(path);
            foreach (var (path, file) in pages)
                Sync(file.Id, file.FullPath, known.GetValueOrDefault(path));
            foreach (var (path, fullPath) in months)
                SyncActivity(path, fullPath, known.GetValueOrDefault(path));
            SyncJudgments();
            _skipped.RemoveWhere(path => !pages.ContainsKey(path) && !months.ContainsKey(path) && !IsJudgmentFile(path));
            _fullBuild = false;
            _commitDayCache = null;
            // Cleared only once every file is in step: a sync that meets a held file leaves the mark for the next search.
            _possiblyStale = false;
        }
    }

    /// <summary>
    /// Marks the index as possibly behind its files: a background sync gave up, and no change may come to start another. It
    /// is kept in memory only, as a restart syncs every file anyway.
    /// </summary>
    public void MarkPossiblyStale() => _possiblyStale = true;

    /// <summary>
    /// Syncs every file when a background sync gave up since the last full sync, so a search does not read what the files no
    /// longer say. A file still held leaves the mark for the next search.
    /// </summary>
    public void SyncAllIfStale()
    {
        if (!_possiblyStale)
            return;
        try
        {
            SyncAll();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The give-up was reported already; the search reads what the index holds, and the next one tries again.
        }
    }

    /// <summary>
    /// Brings <c>persona.md</c> and <c>candidates.md</c> in line with their files: their units are rebuilt when a hash
    /// changed. A judgment that offers these entries calls it before its question is prepared, and a consolidation once before
    /// its judgments, so they see the latest text; the write path calls it after each write, so the next read does.
    /// </summary>
    public void SyncJudgments()
    {
        lock (_gate)
        {
            SyncJudgment(MemoryLayout.Persona, SourceKinds.Persona, KnownHash(MemoryLayout.Persona), PersonaUnits);
            SyncJudgment(MemoryLayout.Candidates, SourceKinds.Candidates, KnownHash(MemoryLayout.Candidates), CandidateUnits);
        }
    }

    /// <summary>Brings the activity month files in line with their files. The write path calls it so a new activity is searchable at once.</summary>
    public void SyncActivities()
    {
        lock (_gate)
        {
            var months = RecentFile.MonthFiles(_memoryPath).ToDictionary(file => file.RelativePath, file => file.FullPath);
            var known = _connection.Query<(string Path, string Hash)>("SELECT path, hash FROM sources WHERE kind = @kind", new { kind = SourceKinds.ActivityRecord })
                .ToDictionary(row => row.Path, row => row.Hash);
            foreach (var path in known.Keys.Where(path => !months.ContainsKey(path)))
                Remove(path);
            foreach (var (path, fullPath) in months)
                SyncActivity(path, fullPath, known.GetValueOrDefault(path));
            _skipped.RemoveWhere(path => MemoryLayout.IsIn(path, MemoryLayout.Recent) && !months.ContainsKey(path));
        }
    }

    /// <summary>Brings one page in line with its file, which may have been deleted.</summary>
    public void SyncPage(PageId id)
    {
        lock (_gate)
        {
            var fullPath = MemoryLayout.Full(_memoryPath, id.RelativePath);
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

    /// <summary>Whether the index has any page in the same workstream as <paramref name="except"/> other than <paramref name="except"/> itself.</summary>
    public bool HasOtherPagesInWorkstream(PageId except)
    {
        lock (_gate)
            return _connection.ExecuteScalar<long>(
                "SELECT count(*) FROM sources s WHERE s.kind = @kind AND s.id <> @id AND EXISTS (SELECT 1 FROM units u WHERE u.source_id = s.id AND u.workstream = @workstream)",
                new { kind = SourceKinds.Page, workstream = except.Workstream, id = except.ToString() }) > 0;
    }

    /// <summary>
    /// The best <paramref name="limit"/> units for <paramref name="query"/> by full text, best first, each with its BM25 value.
    /// </summary>
    public FullTextSearch Search(string query, IndexFilter filter, int limit)
    {
        if (IndexText.ForQuery(query) is not { } match)
            return new FullTextSearch(FullTextMethod.None, []);
        lock (_gate)
        {
            var rows = _connection.Query<ScoredRow>($"""
                SELECT {HitColumns}, bm25(units_fts) AS Score
                FROM units_fts JOIN units u ON u.id = units_fts.rowid
                WHERE units_fts MATCH @match AND {Filters}
                ORDER BY bm25(units_fts) LIMIT @limit
                """, Parameters(filter, limit, new { match }));
            return new FullTextSearch(FullTextMethod.Phrase, rows.Select(row => row.ToHit()).ToList());
        }
    }

    /// <summary>
    /// The <paramref name="limit"/> units nearest to <paramref name="vector"/> by cosine distance, best first, each with its
    /// distance; empty when no vectors of that length are stored.
    /// </summary>
    public IReadOnlyList<ScoredHit> SearchVector(float[] vector, IndexFilter filter, int limit)
    {
        lock (_gate)
        {
            if (Meta("dimension") != vector.Length.ToString(CultureInfo.InvariantCulture))
                return [];
            var rows = _connection.Query<ScoredRow>($"""
                SELECT {HitColumns}, v.embedding AS Embedding
                FROM units_vectors v JOIN units u ON u.id = v.id
                WHERE {Filters}
                ORDER BY u.id
                """, Parameters(filter, limit, new { }));
            return Nearest(rows, row => row.Embedding, vector, limit).Select(nearest => nearest.Row.ToHit() with { Score = nearest.Distance }).ToList();
        }
    }

    /// <summary>
    /// The <paramref name="limit"/> judgment units (persona entries, candidates and rejected candidates) of the sources of
    /// the given kinds nearest to <paramref name="vector"/>, best first; empty when no vectors of that length are stored.
    /// <paramref name="scopes"/> keeps only the units in those scopes; <c>null</c> keeps every scope.
    /// Judgment units never reach a search; this is the only way to read them back, for judgments.
    /// </summary>
    public IReadOnlyList<JudgmentHit> SearchJudgments(
        float[] vector,
        IReadOnlyCollection<string> sourceKinds,
        int limit,
        IReadOnlyCollection<string>? unitKinds = null,
        IReadOnlyCollection<PersonaScope>? scopes = null)
    {
        lock (_gate)
        {
            if (Meta("dimension") != vector.Length.ToString(CultureInfo.InvariantCulture))
                return [];
            var scopeFilter = scopes is null ? "" : "AND u.scope IN @scopes";
            var rows = _connection.Query<JudgmentRow>($"""
                SELECT u.kind AS Kind, u.section AS Section, u.raw AS Text, u.scope AS Scope, v.embedding AS Embedding
                FROM units_vectors v JOIN units u ON u.id = v.id JOIN sources s ON s.id = u.source_id
                WHERE s.kind IN @sourceKinds AND u.kind IN @unitKinds {scopeFilter}
                ORDER BY u.id
                """,
                new
                {
                    sourceKinds = sourceKinds.ToList(),
                    unitKinds = (unitKinds ?? JudgmentKinds).ToList(),
                    scopes = scopes?.Select(scope => scope.ToString()).ToList(),
                });
            return Nearest(rows, row => row.Embedding, vector, limit)
                .Select(nearest => nearest.Row)
                .Select(row => new JudgmentHit(row.Kind, PersonaSectionNamed(row.Section), row.Text, PersonaScope.Stored(row.Scope ?? "global")))
                .ToList();
        }
    }

    /// <summary>A judgment unit as a search reads it: its kind, its stored section and scope names, its text and its vector.</summary>
    private sealed record JudgmentRow(string Kind, string? Section, string Text, string? Scope, byte[] Embedding);

    /// <summary>
    /// The <paramref name="limit"/> rows whose stored vector is nearest to <paramref name="vector"/> by cosine distance, nearest
    /// first; rows at the same distance keep their order. Every vector search goes through here.
    /// </summary>
    private static IEnumerable<(T Row, double Distance)> Nearest<T>(IEnumerable<T> rows, Func<T, byte[]> embedding, float[] vector, int limit) =>
        rows.Select(row => (Row: row, Distance: CosineDistance(MemoryMarshal.Cast<byte, float>(embedding(row)), vector)))
            .OrderBy(nearest => nearest.Distance)
            .Take(limit);

    /// <summary>One minus the cosine similarity; vectors need not be normalized. A zero vector is at distance 1 from any other.</summary>
    private static double CosineDistance(ReadOnlySpan<float> stored, ReadOnlySpan<float> query)
    {
        double dot = 0, storedNorm = 0, queryNorm = 0;
        for (var i = 0; i < stored.Length; i++)
        {
            dot += (double)stored[i] * query[i];
            storedNorm += (double)stored[i] * stored[i];
            queryNorm += (double)query[i] * query[i];
        }

        return storedNorm == 0 || queryNorm == 0 ? 1 : 1 - dot / Math.Sqrt(storedNorm * queryNorm);
    }

    /// <summary>The persona section a stored section name stands for; <c>null</c> when it is none of the four.</summary>
    private static PersonaSection? PersonaSectionNamed(string? name) => Enum.TryParse<PersonaSection>(name, out var section) ? section : null;

    /// <summary>Whether any judgment unit of the sources of the given kinds is indexed, with or without a vector yet.</summary>
    public bool HasJudgments(IReadOnlyCollection<string> sourceKinds)
    {
        lock (_gate)
            return _connection.ExecuteScalar<long>(
                "SELECT EXISTS (SELECT 1 FROM units u JOIN sources s ON s.id = u.source_id WHERE s.kind IN @sourceKinds)",
                new { sourceKinds = sourceKinds.ToList() }) > 0;
    }

    /// <summary>The probabilities cached for a question, by option key; <c>null</c> when the question has not been answered.</summary>
    public IReadOnlyDictionary<string, double>? CachedJudgment(string cacheKey)
    {
        lock (_gate)
        {
            var stored = _connection.QuerySingleOrDefault<string>("SELECT probabilities FROM judgment_cache WHERE cache_key = @cacheKey", new { cacheKey });
            return stored is null ? null : JsonSerializer.Deserialize(stored, CoreJson.Default.DictionaryStringDouble);
        }
    }

    /// <summary>Caches the probabilities the decision model answered for a question, as answered: thresholds apply when they are read.</summary>
    public void StoreJudgment(string cacheKey, IReadOnlyDictionary<string, double> probabilities, DateTimeOffset writtenAt)
    {
        lock (_gate)
            _connection.Execute(
                "INSERT INTO judgment_cache (cache_key, probabilities, written) VALUES (@cacheKey, @probabilities, @written) "
                + "ON CONFLICT (cache_key) DO UPDATE SET probabilities = excluded.probabilities, written = excluded.written",
                new { cacheKey, probabilities = JsonSerializer.Serialize(probabilities.ToDictionary(), CoreJson.Default.DictionaryStringDouble), written = writtenAt.ToString("O", CultureInfo.InvariantCulture) });
    }

    /// <summary>Up to <paramref name="limit"/> units that still need a vector.</summary>
    public IReadOnlyList<PendingUnit> PendingUnits(int limit)
    {
        lock (_gate)
        {
            return _connection.Query<PendingUnit>(
                "SELECT id AS Id, passage AS Passage FROM units WHERE id NOT IN (SELECT id FROM units_vectors) ORDER BY id LIMIT @limit",
                new { limit }).ToList();
        }
    }

    /// <summary>
    /// Stores vectors for units that still exist with the same passage. Vectors of another length than the stored ones
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
                SetMeta("dimension", dimension.ToString(CultureInfo.InvariantCulture));
            }

            using var transaction = _connection.BeginTransaction();
            foreach (var (unit, vector) in vectors)
            {
                var current = _connection.QuerySingleOrDefault<string>("SELECT passage FROM units WHERE id = @Id", unit, transaction);
                if (current != unit.Passage)
                    continue;
                _connection.Execute(
                    "INSERT INTO units_vectors (id, embedding) VALUES (@id, @vector) ON CONFLICT (id) DO UPDATE SET embedding = excluded.embedding",
                    new { id = unit.Id, vector = Blob(vector) }, transaction);
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

    /// <summary>
    /// Deletes the index and creates an empty one: the full-text table, the units, the vectors and the judgment cache all
    /// go. The next <see cref="SyncAll"/> indexes every file again.
    /// </summary>
    public void Rebuild()
    {
        lock (_gate)
        {
            _connection.Dispose();
            _skipped.Clear();
            _connection = Create();
            if (_embeddingModel is not null)
                SetMeta("embedding_model", _embeddingModel);
        }
    }

    /// <summary>The last changed date of each section of a page, by section name; empty when the page is not indexed.</summary>
    public IReadOnlyDictionary<string, DateOnly> SectionChanges(PageId id)
    {
        lock (_gate)
        {
            var dates = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
            foreach (var row in _connection.Query<(string Section, string Changed)>(
                "SELECT section AS Section, changed AS Changed FROM units WHERE source_id = @source AND kind = @kind AND changed IS NOT NULL",
                new { source = id.ToString(), kind = UnitKinds.Section }))
                dates[row.Section] = DateOnly.ParseExact(row.Changed, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            return dates;
        }
    }

    /// <summary>How often <c>recall</c> has returned each section of a page, by section name; empty when the page is not indexed.</summary>
    public IReadOnlyDictionary<string, long> SectionReturns(PageId id)
    {
        lock (_gate)
            return _connection.Query<(string Section, long Returns)>(
                "SELECT section AS Section, returns AS Returns FROM units WHERE source_id = @source AND kind = @kind",
                new { source = id.ToString(), kind = UnitKinds.Section })
                .GroupBy(row => row.Section, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Sum(row => row.Returns), StringComparer.Ordinal);
    }

    /// <summary>Counts one return of each of the units, and records today as their last return.</summary>
    public void RecordReturns(IReadOnlyCollection<long> unitIds)
    {
        if (unitIds.Count == 0)
            return;
        lock (_gate)
            _connection.Execute(
                "UPDATE units SET returns = returns + 1, returned = MAX(COALESCE(returned, ''), @day) WHERE id IN @ids",
                new { day = DateText(_today()), ids = unitIds.ToList() });
    }

    /// <summary>Counts one return of each section of a page, as reading the whole page does.</summary>
    public void RecordPageRead(PageId id)
    {
        lock (_gate)
            _connection.Execute(
                "UPDATE units SET returns = returns + 1, returned = MAX(COALESCE(returned, ''), @day) WHERE source_id = @source AND kind = @kind",
                new { day = DateText(_today()), source = id.ToString(), kind = UnitKinds.Section });
    }

    /// <summary>
    /// Counts one return for each listed page, as a workstream brief does: the count lands on the page's title and aliases unit,
    /// so each page adds one whatever its size.
    /// </summary>
    public void RecordPagesListed(IReadOnlyCollection<PageId> pages)
    {
        if (pages.Count == 0)
            return;
        lock (_gate)
            _connection.Execute(
                "UPDATE units SET returns = returns + 1, returned = MAX(COALESCE(returned, ''), @day) WHERE source_id IN @sources AND kind = @kind",
                new { day = DateText(_today()), sources = pages.Select(page => page.ToString()).ToList(), kind = UnitKinds.Clue });
    }

    /// <summary>
    /// Every page with the total of its units' returns and the last day any of them was returned; a page never returned
    /// has zero and no day. These statistics never reach the ranking.
    /// </summary>
    public IReadOnlyList<PageUsageRow> PageUsages()
    {
        lock (_gate)
            return _connection.Query<(string Page, long Count, string? Last)>(
                "SELECT s.id AS Page, SUM(u.returns) AS Count, MAX(u.returned) AS Last FROM sources s JOIN units u ON u.source_id = s.id "
                + "WHERE s.kind = @kind GROUP BY s.id ORDER BY s.id",
                new { kind = SourceKinds.Page })
                .Select(row => new PageUsageRow(row.Page, row.Count, row.Last)).ToList();
    }

    /// <summary>The returns of every activity unit, by its short code. Derived state, like <see cref="PageUsages"/>.</summary>
    public IReadOnlyDictionary<string, long> ActivityUsages()
    {
        lock (_gate)
            return _connection.Query<(string Code, long Count)>(
                "SELECT code AS Code, SUM(returns) AS Count FROM units WHERE kind = @kind AND code IS NOT NULL GROUP BY code",
                new { kind = UnitKinds.Activity })
                .ToDictionary(row => row.Code, row => row.Count, StringComparer.Ordinal);
    }

    /// <summary>How many files, units and units with a vector the index holds now.</summary>
    public IndexCounts Counts()
    {
        lock (_gate)
        {
            return new IndexCounts(
                (int)_connection.ExecuteScalar<long>("SELECT count(*) FROM sources"),
                (int)_connection.ExecuteScalar<long>("SELECT count(*) FROM units"),
                (int)_connection.ExecuteScalar<long>("SELECT count(*) FROM units_vectors"));
        }
    }

    public void Dispose()
    {
        lock (_gate)
            _connection.Dispose();
    }

    private static object Parameters(IndexFilter filter, int limit, object extra)
    {
        var parameters = new DynamicParameters(extra);
        parameters.Add("workstream", filter.Workstream);
        parameters.Add("from", filter.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        parameters.Add("to", filter.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        parameters.Add("activities", filter.Activities);
        parameters.Add("limit", limit);
        return parameters;
    }

    /// <summary>A vector as stored: its float32s in the machine's byte order, read back by <see cref="Nearest"/>.</summary>
    private static byte[] Blob(float[] vector) => MemoryMarshal.AsBytes(vector.AsSpan()).ToArray();

    private void Sync(PageId id, string fullPath, string? knownHash)
    {
        var bytes = TextFile.ReadSharedBytes(fullPath);
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
        var source = id.ToString();
        var previous = SectionStates(source);
        var carried = UsageStates(source);
        // A section a full build meets first is dated by its file's last commit; any other new or changed section is today's.
        var firstSeen = _today();
        if (_fullBuild && knownHash is null)
        {
            _commitDayCache ??= _commitDays();
            firstSeen = _commitDayCache.GetValueOrDefault(id.RelativePath, firstSeen);
        }

        using var transaction = _connection.BeginTransaction();
        RemoveRows(id.RelativePath, transaction);
        var header = document.Header;
        _connection.Execute(
            "INSERT INTO sources (id, kind, path, hash) VALUES (@id, @kind, @path, @hash)",
            new { id = source, kind = SourceKinds.Page, path = id.RelativePath, hash },
            transaction);
        var scope = new UnitScope(id.Workstream, DateText(header.First), DateText(header.Last));
        AddUnit(source, UnitKinds.Clue, null, 0, string.Join('\n', [header.Title, .. header.Aliases]), scope, transaction, carried: carried);
        for (var i = 0; i < document.Sections.Count; i++)
        {
            var section = document.Sections[i];
            var raw = section.Name + "\n" + section.Body;
            // Trailing whitespace is not content, so a newline at the end of a file does not make its last section changed.
            var contentHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw.TrimEnd())));
            var unchanged = previous.TryGetValue(section.Name, out var before) && before.Hash == contentHash && before.Changed is not null;
            var changed = unchanged ? before!.Changed! : DateText(firstSeen)!;
            AddUnit(source, UnitKinds.Section, section.Name, i + 1, raw, scope, transaction, hash: contentHash, changed: changed, carried: carried,
                passage: header.Title + "\n" + raw);
        }

        transaction.Commit();
    }

    /// <summary>The content hash and last changed date each section of a page had when last synced, by section name.</summary>
    private Dictionary<string, (string Hash, string? Changed)> SectionStates(string source)
    {
        var states = new Dictionary<string, (string Hash, string? Changed)>(StringComparer.Ordinal);
        foreach (var row in _connection.Query<(string Section, string Hash, string? Changed)>(
            "SELECT section AS Section, hash AS Hash, changed AS Changed FROM units WHERE source_id = @source AND kind = @kind",
            new { source, kind = UnitKinds.Section }))
            states[row.Section] = (row.Hash, row.Changed);
        return states;
    }

    /// <summary>
    /// The usage counts each unit of a source holds, keyed by <see cref="UsageKey"/>, so a re-sync can carry them over.
    /// Usage is derived state: only a reindex or a restore clears it, never an edit of the file.
    /// </summary>
    private Dictionary<string, UsageState> UsageStates(string source)
    {
        var states = new Dictionary<string, UsageState>(StringComparer.Ordinal);
        foreach (var row in _connection.Query<UsageRow>(
            "SELECT kind AS Kind, section AS Section, code AS Code, first AS First, last AS Last, returns AS Returns, returned AS Returned "
            + "FROM units WHERE source_id = @source AND (returns > 0 OR returned IS NOT NULL)",
            new { source }))
            states[UsageKey(row.Kind, row.Section, row.Code, row.First, row.Last)] = new UsageState(row.Returns, row.Returned);
        return states;
    }

    /// <summary>
    /// What identifies a unit within its source across a re-sync: a page's section by its name, its title and aliases
    /// by kind alone, an activity by its short code, and a summary by its section and period.
    /// </summary>
    private static string UsageKey(string kind, string? section, string? code, string? first, string? last) => kind switch
    {
        UnitKinds.Activity => $"{kind}|{code}",
        UnitKinds.Summary => $"{kind}|{section}|{first}|{last}",
        UnitKinds.Section => $"{kind}|{section}",
        _ => kind,
    };

    /// <summary>A unit's usage counts as <see cref="UsageStates"/> reads them.</summary>
    /// <summary>
    /// A search row: the hit's columns and the score the search ordered by. Mapped by property, because SQLite reports no type
    /// for a computed column of an empty result, so no constructor would match.
    /// </summary>
    private sealed class ScoredRow
    {
        public long Id { get; init; }
        public string Source { get; init; } = "";
        public string Kind { get; init; } = "";
        public string? Section { get; init; }
        public string Text { get; init; } = "";
        public string Workstream { get; init; } = "";
        public string? Date { get; init; }
        public string? Code { get; init; }
        public string Passage { get; init; } = "";
        public double Score { get; init; }
        public byte[] Embedding { get; init; } = [];

        public ScoredHit ToHit() => new(new IndexHit(Id, Source, Kind, Section, Text, Workstream, Date, Code, Passage), Score);
    }

    private sealed record UsageState(long Returns, string? Returned);

    /// <summary>A unit's identity and usage counts, as <see cref="UsageStates"/> reads them.</summary>
    private sealed record UsageRow(string Kind, string? Section, string? Code, string? First, string? Last, long Returns, string? Returned);

    private void SyncActivity(string relativePath, string fullPath, string? knownHash)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(TextFile.ReadSharedBytes(fullPath)));
        if (hash == knownHash)
            return;

        if (TextFile.Read(fullPath) is not { } read)
        {
            Remove(relativePath);
            _skipped.Add(relativePath);
            return;
        }

        _skipped.Remove(relativePath);
        var carried = UsageStates(relativePath);
        using var transaction = _connection.BeginTransaction();
        RemoveRows(relativePath, transaction);
        _connection.Execute(
            "INSERT INTO sources (id, kind, path, hash) VALUES (@id, @kind, @path, @hash)",
            new { id = relativePath, kind = SourceKinds.ActivityRecord, path = relativePath, hash },
            transaction);
        foreach (var activity in RecentFile.Activities(read.Text))
        {
            // An activity has no section; the column holds the machine it was written on, so recall can show it.
            var day = DateText(activity.Day);
            AddUnit(relativePath, UnitKinds.Activity, activity.Machine, activity.Line, activity.Labeled, new UnitScope(activity.Workstream ?? "", day, day), transaction, activity.Code, carried: carried);
        }

        foreach (var summary in SummaryLines.All(read.Text))
        {
            var kind = summary.Kind == SummaryKind.Week ? "week" : "month";
            AddUnit(relativePath, UnitKinds.Summary, kind, summary.Line, summary.Text, new UnitScope("", DateText(summary.First), DateText(summary.Last)), transaction, carried: carried);
        }

        transaction.Commit();
    }

    /// <summary>
    /// Brings a persona file in line with its file: its units are rebuilt when the hash changed. A missing file leaves
    /// no units; a file that is not UTF-8 is skipped, as the other indexed files are.
    /// </summary>
    private void SyncJudgment(string relativePath, string sourceKind, string? knownHash, Func<string, IReadOnlyList<JudgmentUnit>> unitsOf)
    {
        var fullPath = MemoryLayout.Full(_memoryPath, relativePath);
        if (!File.Exists(fullPath))
        {
            Remove(relativePath);
            _skipped.Remove(relativePath);
            return;
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(TextFile.ReadSharedBytes(fullPath)));
        if (hash == knownHash)
            return;

        if (TextFile.Read(fullPath) is not { } read)
        {
            Remove(relativePath);
            _skipped.Add(relativePath);
            return;
        }

        _skipped.Remove(relativePath);
        var units = unitsOf(read.Text);
        using var transaction = _connection.BeginTransaction();
        RemoveRows(relativePath, transaction);
        _connection.Execute(
            "INSERT INTO sources (id, kind, path, hash) VALUES (@id, @kind, @path, @hash)",
            new { id = relativePath, kind = sourceKind, path = relativePath, hash },
            transaction);
        var scope = new UnitScope("", null, null);
        for (var i = 0; i < units.Count; i++)
            AddUnit(relativePath, units[i].Kind, units[i].Section.ToString(), i + 1, units[i].Text, scope, transaction, judgmentScope: units[i].Scope.ToString());
        transaction.Commit();
    }

    /// <summary>
    /// The units of <c>persona.md</c>: one per list entry under a known section, in any scope, its text with its continuation
    /// lines but without its source comment.
    /// </summary>
    private static IReadOnlyList<JudgmentUnit> PersonaUnits(string text) =>
        PersonaDocument.Parse(text).KnownEntries()
            .Where(entry => entry.Entry.IsListItem && entry.Entry.Text.Length > 0)
            .Select(entry => new JudgmentUnit(UnitKinds.PersonaEntry, entry.Kind, entry.Entry.JudgmentText, entry.Scope))
            .ToList();

    /// <summary>The units of <c>candidates.md</c>: one per candidate and one per rejected candidate, each named by its text.</summary>
    private static IReadOnlyList<JudgmentUnit> CandidateUnits(string text)
    {
        var document = CandidatesDocument.Parse(text);
        return
        [
            .. document.Candidates.Select(candidate => new JudgmentUnit(UnitKinds.Candidate, candidate.Section, candidate.Text, candidate.Scope)),
            .. document.RejectionList.Select(candidate => new JudgmentUnit(UnitKinds.RejectedCandidate, candidate.Section, candidate.Text, candidate.Scope)),
        ];
    }

    /// <summary>Whether a source path is one of the two persona files.</summary>
    private static bool IsJudgmentFile(string path) => path is MemoryLayout.Persona or MemoryLayout.Candidates;

    /// <summary>One unit of a persona file: its kind, the persona section it belongs to, its text, and the scope it is in.</summary>
    private sealed record JudgmentUnit(string Kind, PersonaSection Section, string Text, PersonaScope Scope);

    /// <summary>The workstream and dates every unit of a source carries.</summary>
    private sealed record UnitScope(string Workstream, string? First, string? Last);

    /// <param name="hash">A section's content hash; the other kinds of unit have none.</param>
    /// <param name="changed">A section's last changed date as yyyy-MM-dd; the other kinds of unit have none.</param>
    /// <param name="judgmentScope">A judgment unit's persona scope as its text; the other kinds of unit have none.</param>
    /// <param name="carried">
    /// The usage counts of the source's units before this sync, by <see cref="UsageKey"/>. A unit that matches one takes its
    /// counts, and each old unit passes its counts on once.
    /// </param>
    /// <param name="passage">What the embedding model and the reranker read; <c>null</c> for the indexed text itself.</param>
    private void AddUnit(
        string source, string kind, string? section, int ordinal, string raw, UnitScope scope, SqliteTransaction transaction,
        string? code = null, string? hash = null, string? changed = null, string? judgmentScope = null,
        Dictionary<string, UsageState>? carried = null, string? passage = null)
    {
        var usage = carried is not null && carried.Remove(UsageKey(kind, section, code, scope.First, scope.Last), out var state) ? state : null;
        var unit = _connection.ExecuteScalar<long>(
            "INSERT INTO units (source_id, kind, section, ordinal, workstream, first, last, raw, passage, code, hash, changed, scope, returns, returned) "
            + "VALUES (@source, @kind, @section, @ordinal, @workstream, @first, @last, @raw, @passage, @code, @hash, @changed, @judgmentScope, @returns, @returned) RETURNING id",
            new
            {
                source, kind, section, ordinal, workstream = scope.Workstream, first = scope.First, last = scope.Last, raw, passage = passage ?? raw,
                code, hash, changed, judgmentScope,
                returns = usage?.Returns ?? 0,
                returned = usage?.Returned,
            },
            transaction);
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
        _connection.Execute("DELETE FROM units_vectors WHERE id IN (SELECT id FROM units WHERE source_id = @source)", new { source }, transaction);
        _connection.Execute("DELETE FROM units WHERE source_id = @source", new { source }, transaction);
        _connection.Execute("DELETE FROM sources WHERE id = @source", new { source }, transaction);
    }

    private string? KnownHash(string relativePath) =>
        _connection.QuerySingleOrDefault<string>("SELECT hash FROM sources WHERE path = @relativePath", new { relativePath });

    private void DropVectors()
    {
        _connection.Execute("DELETE FROM units_vectors; DELETE FROM meta WHERE key = 'dimension';");
    }

    private string? Meta(string key) => _connection.QuerySingleOrDefault<string?>("SELECT value FROM meta WHERE key = @key", new { key });

    private void SetMeta(string key, string value) =>
        _connection.Execute("INSERT INTO meta (key, value) VALUES (@key, @value) ON CONFLICT (key) DO UPDATE SET value = excluded.value", new { key, value });

    /// <summary>Page files named as page IDs: <c>pages/&lt;workstream&gt;/&lt;slug&gt;.md</c>. Other files are not pages.</summary>
    private IEnumerable<(PageId Id, string FullPath)> PageFiles()
    {
        var pages = MemoryLayout.Full(_memoryPath, MemoryLayout.Pages);
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
        return Create();
    }

    /// <summary>Deletes the index files and creates an empty index with the current schema.</summary>
    private SqliteConnection Create()
    {
        _fullBuild = true;
        foreach (var suffix in (string[])["", "-wal", "-shm", "-journal"])
            File.Delete(_databasePath + suffix);
        var fresh = Connect();
        fresh.Execute($"""
            CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE sources (id TEXT PRIMARY KEY, kind TEXT NOT NULL, path TEXT NOT NULL UNIQUE, hash TEXT NOT NULL);
            CREATE TABLE units (id INTEGER PRIMARY KEY, source_id TEXT NOT NULL, kind TEXT NOT NULL, section TEXT, ordinal INTEGER NOT NULL, workstream TEXT NOT NULL, first TEXT, last TEXT, raw TEXT NOT NULL, passage TEXT NOT NULL, code TEXT, hash TEXT, changed TEXT, returns INTEGER NOT NULL DEFAULT 0, returned TEXT, scope TEXT);
            CREATE INDEX units_source ON units (source_id);
            CREATE VIRTUAL TABLE units_fts USING fts5 (text, tokenize = 'unicode61');
            CREATE TABLE units_vectors (id INTEGER PRIMARY KEY, embedding BLOB NOT NULL);
            CREATE TABLE judgment_cache (cache_key TEXT PRIMARY KEY, probabilities TEXT NOT NULL, written TEXT NOT NULL);
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
