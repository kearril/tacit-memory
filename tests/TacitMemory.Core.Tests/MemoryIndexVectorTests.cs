using TacitMemory.Core.Index;
using TacitMemory.Core.Pages;
using TacitMemory.Core.Persona;

namespace TacitMemory.Core.Tests;

public sealed class MemoryIndexVectorTests : IDisposable
{
    private readonly string _memoryPath = Path.Combine(Path.GetTempPath(), "tacit-idx-vec-" + Guid.NewGuid().ToString("N")[..8]);

    public MemoryIndexVectorTests() => Directory.CreateDirectory(_memoryPath);

    public void Dispose()
    {
        if (Directory.Exists(_memoryPath))
            Directory.Delete(_memoryPath, recursive: true);
    }

    private MemoryIndex OpenIndex() => new(_memoryPath, "test-embed", () => new DateOnly(2026, 10, 20), () => new Dictionary<string, DateOnly>());

    private void WritePage(string workstream, string slug, string title, string body, string first = "2026-10-01", string last = "2026-10-10")
    {
        var path = Path.Combine(_memoryPath, "pages", workstream, $"{slug}.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var text = $"---\ntitle: {title}\naliases: [{title}别名一, {title}别名二]\ndates: {{ first: {first}, last: {last} }}\ntype: work\n---\n\n## 内容\n{body}\n";
        File.WriteAllText(path, text);
    }

    private void WriteRecent(string month, string content)
    {
        var path = Path.Combine(_memoryPath, "recent", $"{month}.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static float[] AxisVector(int dim, int axis, float scale = 1f)
    {
        var vector = new float[dim];
        vector[axis] = scale;
        return vector;
    }

    private static float[] PatternVector(int dim, int seed, float scale = 1f)
    {
        var vector = new float[dim];
        for (var i = 0; i < dim; i++)
            vector[i] = MathF.Sin((seed + 1) * 1.7f + i * 0.13f) * scale;
        return vector;
    }

    private static double ReferenceCosineDistance(float[] a, float[] b)
    {
        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
            normA += (double)a[i] * a[i];
            normB += (double)b[i] * b[i];
        }

        return normA == 0 || normB == 0 ? 1 : 1 - dot / Math.Sqrt(normA * normB);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(17)]
    [InlineData(1024)]
    public void SearchVector_ranks_by_cosine_distance_for_unnormalized_vectors_and_SIMD_tail_dimensions(int dim)
    {
        WritePage("my-app", "p1", "页面一", "正文一");
        WritePage("my-app", "p2", "页面二", "正文二");
        WritePage("my-app", "p3", "页面三", "正文三");
        using var index = OpenIndex();
        index.SyncAll();

        var pending = index.PendingUnits(100);
        Assert.Equal(6, pending.Count);
        var query = PatternVector(dim, seed: 42, scale: 3.5f);
        var assigned = new List<(PendingUnit Unit, float[] Vector)>();
        for (var i = 0; i < pending.Count; i++)
        {
            // Include an extreme scale vector to verify overflow safety.
            var scale = i == 2 ? 1e20f : 0.25f * (i + 1);
            var vec = i == 0 ? PatternVector(dim, seed: 42, scale: 7f) : PatternVector(dim, seed: i + 100, scale: scale);
            assigned.Add((pending[i], vec));
        }

        index.StoreVectors(assigned);

        var hits = index.SearchVector(query, new IndexFilter(null, null, null), limit: 4);

        var expected = assigned
            .Select(pair => (pair.Unit.Id, Distance: ReferenceCosineDistance(pair.Vector, query)))
            .OrderBy(item => item.Distance)
            .ThenBy(item => item.Id)
            .Take(4)
            .ToList();

        Assert.Equal(4, hits.Count);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(expected[i].Id, hits[i].Hit.Id);
            Assert.NotNull(hits[i].Score);
            Assert.InRange(Math.Abs(hits[i].Score!.Value - expected[i].Distance), 0, 1e-5);
        }
    }

    [Fact]
    public void SearchVector_breaks_distance_ties_by_ascending_unit_id_and_handles_zero_vectors()
    {
        WritePage("my-app", "a", "甲", "内容甲");
        WritePage("my-app", "b", "乙", "内容乙");
        WritePage("my-app", "c", "丙", "内容丙");
        using var index = OpenIndex();
        index.SyncAll();

        var pending = index.PendingUnits(100);
        Assert.Equal(6, pending.Count);
        var same = new float[] { 0.6f, 0.8f, 0f, 0f, 0f };
        var zero = new float[5];
        var opposite = new float[] { -0.6f, -0.8f, 0f, 0f, 0f };
        index.StoreVectors(
        [
            (pending[0], zero),
            (pending[1], same),
            (pending[2], opposite),
            (pending[3], same),
            (pending[4], same),
            (pending[5], zero),
        ]);

        // Tied top-2 among the three identical vectors must pick lowest IDs (pending[1], pending[3]).
        var tied = index.SearchVector(same, new IndexFilter(null, null, null), limit: 2);
        Assert.Equal([pending[1].Id, pending[3].Id], tied.Select(h => h.Hit.Id).ToArray());

        // Zero stored vectors sit at distance 1.0, strictly ahead of opposite vector at distance 2.0.
        var all = index.SearchVector(same, new IndexFilter(null, null, null), limit: 6);
        Assert.Equal([pending[1].Id, pending[3].Id, pending[4].Id, pending[0].Id, pending[5].Id, pending[2].Id], all.Select(h => h.Hit.Id).ToArray());
        Assert.Equal(1.0, all[3].Score);
        Assert.Equal(1.0, all[4].Score);

        // Zero query vector places every unit at distance 1.0 in ascending ID order.
        var fromZero = index.SearchVector(zero, new IndexFilter(null, null, null), limit: 3);
        Assert.Equal([pending[0].Id, pending[1].Id, pending[2].Id], fromZero.Select(h => h.Hit.Id).ToArray());
        Assert.All(fromZero, hit => Assert.Equal(1.0, hit.Score));
    }

    [Fact]
    public void SearchVector_applies_filters_and_limit_boundaries()
    {
        WritePage("my-app", "early", "早期页", "内容一", first: "2026-08-01", last: "2026-08-10");
        WritePage("ops-infra", "late", "晚期页", "内容二", first: "2026-10-05", last: "2026-10-15");
        WriteRecent("2026-10", "## 2026-10-12\n- [ops-infra] 调整容器日志轮转上限\n  <!-- on build-node -->\n");
        File.WriteAllText(Path.Combine(_memoryPath, "persona.md"), "## Preferences\n- 保持日志精简\n");
        using var index = OpenIndex();
        index.SyncAll();

        var pending = index.PendingUnits(100);
        index.StoreVectors(pending.Select(u => (u, AxisVector(4, 0))).ToList());

        var query = AxisVector(4, 0);
        Assert.Empty(index.SearchVector(query, new IndexFilter(null, null, null), limit: 0));
        Assert.Empty(index.SearchVector(AxisVector(8, 0), new IndexFilter(null, null, null), limit: 5));

        var activitiesOnly = index.SearchVector(query, new IndexFilter("ops-infra", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 14), Activities: true), limit: 10);
        var hit = Assert.Single(activitiesOnly);
        Assert.Equal(UnitKinds.Activity, hit.Hit.Kind);
        Assert.Equal("ops-infra", hit.Hit.Workstream);

        var pagesOnly = index.SearchVector(query, new IndexFilter(null, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), Activities: false), limit: 10);
        Assert.Equal(2, pagesOnly.Count);
        Assert.All(pagesOnly, item => Assert.Equal("ops-infra/late", item.Hit.Source));
    }

    [Fact]
    public void SearchJudgments_ranks_with_scopes_unit_kinds_and_empty_collections()
    {
        File.WriteAllText(
            Path.Combine(_memoryPath, "persona.md"),
            "## Preferences\n- 全局偏好一\n- 全局偏好二\n\n## Domain: dotnet\nTrigger: 编写代码时\n### Preferences\n- 领域规则一\n");
        File.WriteAllText(
            Path.Combine(_memoryPath, "candidates.md"),
            "## Candidates\n\n### 候选偏好\nBelongs to: Preferences\n- 2026-10-10 quote「候选」\n\n## Rejection list\n\n### 拒绝偏好\nBelongs to: Preferences\n- 2026-10-11 quote「拒绝」\n");
        using var index = OpenIndex();
        index.SyncAll();

        var pending = index.PendingUnits(100);
        index.StoreVectors(pending.Select(u => (u, u.Passage.Contains("领域规则", StringComparison.Ordinal) ? AxisVector(4, 0) : AxisVector(4, 1))).ToList());

        var query = AxisVector(4, 0);
        Assert.Empty(index.SearchJudgments(query, [], limit: 5));
        Assert.Empty(index.SearchJudgments(query, [SourceKinds.Persona], limit: 0));
        Assert.Empty(index.SearchJudgments(query, [SourceKinds.Persona], limit: 5, unitKinds: []));
        Assert.Empty(index.SearchJudgments(query, [SourceKinds.Persona], limit: 5, scopes: []));

        var scoped = index.SearchJudgments(
            query,
            [SourceKinds.Persona],
            limit: 5,
            unitKinds: [UnitKinds.PersonaEntry],
            scopes: [PersonaScope.Stored("domain:dotnet")]);
        var hit = Assert.Single(scoped);
        Assert.Contains("领域规则一", hit.Text);
        Assert.Equal(PersonaScope.Stored("domain:dotnet"), hit.Scope);

        var all = index.SearchJudgments(query, [SourceKinds.Persona, SourceKinds.Candidates], limit: 2);
        Assert.Equal(2, all.Count);
        Assert.Contains("领域规则一", all[0].Text);
        // Remaining entries tie at distance 1.0, so the lowest ascending unit ID ("全局偏好一") takes rank 2.
        Assert.Contains("全局偏好一", all[1].Text);
    }

    [Fact]
    public void SearchVector_handles_tiny_underflow_vectors_and_non_finite_values_safely()
    {
        WritePage("my-app", "u1", "页一", "正文一");
        WritePage("my-app", "u2", "页二", "正文二");
        using var index = OpenIndex();
        index.SyncAll();

        var pending = index.PendingUnits(100);
        Assert.Equal(4, pending.Count);
        // 5-dim vector triggers both SIMD (4 floats) and scalar tail (1 float) with float32 squaring underflow.
        var query = new float[] { 1f, 0f, 0f, 0f, 0f };
        var tinyExact = new float[] { 1e-25f, 0f, 0f, 0f, 0f };
        var tinyWithTail = new float[] { 1e-25f, 0f, 0f, 0f, 1e-30f };
        var nanVector = new float[] { float.NaN, 1f, 0f, 0f, 0f };
        var infVector = new float[] { float.PositiveInfinity, 0f, 0f, 0f, 0f };
        index.StoreVectors(
        [
            (pending[0], nanVector),
            (pending[1], tinyWithTail),
            (pending[2], tinyExact),
            (pending[3], infVector),
        ]);

        var hits = index.SearchVector(query, new IndexFilter(null, null, null), limit: 4);

        Assert.Equal(4, hits.Count);
        // tinyExact aligns exactly with query (distance 0.0), strictly ahead of tinyWithTail (~5e-11) and NaN/Infinity fallback (1.0).
        Assert.Equal(pending[2].Id, hits[0].Hit.Id);
        Assert.Equal(pending[1].Id, hits[1].Hit.Id);
        Assert.InRange(hits[0].Score!.Value, -1e-6, 1e-6);
        Assert.InRange(hits[1].Score!.Value, -1e-6, 1e-6);
        Assert.Equal(1.0, hits[2].Score);
        Assert.Equal(1.0, hits[3].Score);

        // Non-finite query vector falls back cleanly to equidistant distance 1.0 in ascending ID order.
        var nanQueryHits = index.SearchVector([float.NaN, 0f, 0f, 0f, 0f], new IndexFilter(null, null, null), limit: 2);
        Assert.Equal([pending[0].Id, pending[1].Id], nanQueryHits.Select(h => h.Hit.Id).ToArray());
        Assert.All(nanQueryHits, hit => Assert.Equal(1.0, hit.Score));
    }

    [Fact]
    public void SearchVector_hydrates_more_than_one_id_chunk_with_unbounded_limit()
    {
        // 300 pages -> 600 units, exceeding the 500-ID hydration chunk size.
        for (var i = 0; i < 300; i++)
            WritePage("my-app", $"page-{i:D3}", $"标题{i:D3}", $"内容{i:D3}");
        using var index = OpenIndex();
        index.SyncAll();

        var pending = index.PendingUnits(1000);
        Assert.Equal(600, pending.Count);
        var query = AxisVector(4, 0);
        index.StoreVectors(pending.Select((unit, idx) =>
        {
            var angle = idx * (MathF.PI / 600f);
            return (unit, new float[] { MathF.Cos(angle), MathF.Sin(angle), 0f, 0f });
        }).ToList());

        var hits = index.SearchVector(query, new IndexFilter(null, null, null), limit: int.MaxValue);

        Assert.Equal(600, hits.Count);
        Assert.Equal(pending[0].Id, hits[0].Hit.Id);
        Assert.Equal(pending[^1].Id, hits[^1].Hit.Id);
        for (var i = 1; i < hits.Count; i++)
            Assert.True(hits[i - 1].Score <= hits[i].Score);
    }

    [Fact]
    public void HasPendingVectors_tracks_unit_insertions_fills_rebuilds_and_dimension_changes()
    {
        WritePage("my-app", "p1", "页面一", "正文一");
        using var index = OpenIndex();
        index.SyncAll();

        Assert.True(index.HasPendingVectors());

        var pending = index.PendingUnits(100);
        index.StoreVectors(pending.Select(u => (u, AxisVector(4, 0))).ToList());
        Assert.False(index.HasPendingVectors());
        Assert.False(index.HasPendingVectors()); // cached fast-path

        // Single-path SyncPage invalidates the cached flag.
        WritePage("my-app", "p2", "页面二", "正文二");
        Assert.True(PageId.TryParse("my-app/p2", out var page2, out _));
        index.SyncPage(page2!);
        Assert.True(index.HasPendingVectors());
        var nextPending = index.PendingUnits(100);
        index.StoreVectors(nextPending.Select(u => (u, AxisVector(4, 1))).ToList());
        Assert.False(index.HasPendingVectors());

        // Single-path SyncActivities invalidates the cached flag.
        WriteRecent("2026-10", "## 2026-10-12\n- [my-app] 补齐一条活动\n  <!-- on dev-box -->\n");
        index.SyncActivities();
        Assert.True(index.HasPendingVectors());
        index.StoreVectors(index.PendingUnits(100).Select(u => (u, AxisVector(4, 2))).ToList());
        Assert.False(index.HasPendingVectors());

        // Single-path SyncJudgments invalidates the cached flag.
        File.WriteAllText(Path.Combine(_memoryPath, "persona.md"), "## Preferences\n- 保持简洁\n");
        index.SyncJudgments();
        Assert.True(index.HasPendingVectors());
        var allUnits = index.PendingUnits(100);
        // Dimension switch inside StoreVectors drops prior vectors so earlier units become pending again.
        index.StoreVectors(allUnits.Select(u => (u, AxisVector(8, 0))).ToList());
        Assert.True(index.HasPendingVectors());
        index.StoreVectors(index.PendingUnits(100).Select(u => (u, AxisVector(8, 0))).ToList());
        Assert.False(index.HasPendingVectors());

        index.ResetVectors();
        Assert.True(index.HasPendingVectors());

        index.Rebuild();
        index.SyncAll();
        Assert.True(index.HasPendingVectors());
    }

    [Fact]
    public void SyncAll_rolls_back_batch_and_leaves_skipped_untouched_when_a_file_is_held()
    {
        WritePage("my-app", "p1", "页面一", "正文一");
        WritePage("my-app", "p2", "页面二", "正文二");
        using var index = OpenIndex();

        var heldPath = Path.Combine(_memoryPath, "pages", "my-app", "p2.md");
        using (new FileStream(heldPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => index.SyncAll());
        }

        // Neither file should be half-committed when SyncAll aborts mid-batch.
        Assert.Equal(0, index.Counts().Files);
        Assert.Equal(0, index.Counts().Units);
        Assert.Empty(index.SkippedFiles);

        // Once released, SyncAll indexes both cleanly.
        index.SyncAll();
        Assert.Equal(2, index.Counts().Files);
        Assert.Equal(4, index.Counts().Units);
    }

    [Fact]
    public void SyncAll_handles_batch_create_incremental_noop_delete_and_non_utf8_skip()
    {
        WritePage("my-app", "p1", "页面一", "正文一");
        WritePage("my-app", "p2", "页面二", "正文二");
        var badPath = Path.Combine(_memoryPath, "pages", "my-app", "bad.md");
        File.WriteAllBytes(badPath, [0x2D, 0x0A, 0xFF, 0xFE, 0x0A]);
        using var index = OpenIndex();

        index.SyncAll();
        Assert.Equal(["pages/my-app/bad.md"], index.SkippedFiles);
        Assert.Equal(2, index.Counts().Files);
        Assert.Equal(4, index.Counts().Units);

        // Incremental no-op sync keeps identical state.
        index.SyncAll();
        Assert.Equal(["pages/my-app/bad.md"], index.SkippedFiles);
        Assert.Equal(4, index.Counts().Units);

        // Deleting one valid page and fixing the non-UTF-8 file syncs cleanly in one batch.
        File.Delete(Path.Combine(_memoryPath, "pages", "my-app", "p2.md"));
        WritePage("my-app", "bad", "已修复页", "有效内容");
        index.SyncAll();
        Assert.Empty(index.SkippedFiles);
        Assert.Equal(2, index.Counts().Files);
        Assert.Equal(4, index.Counts().Units);
    }
}
