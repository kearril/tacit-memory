using TacitMemory.Core.Index;
using TacitMemory.Core.Pages;

namespace TacitMemory.Core.Retrieval;

/// <summary>
/// Whether a read counts toward the usage statistics: <c>recall</c> counts what it returns; the memory page and the recall
/// trace count nothing.
/// </summary>
internal enum Usage
{
    Counted,
    NotCounted,
}

/// <summary>A whole page as read: the page, or the refusal that says why it cannot be read.</summary>
internal sealed record PageReading(PageDocument? Document, ToolResult? Refusal);

/// <summary>A workstream's brief as read: everything it lists, whole, and the text <c>recall</c> returns for it.</summary>
internal sealed record BriefReading(WorkstreamBrief.Parts Parts, string Text);

/// <summary>
/// The reads that can count toward the usage statistics: a search, a whole page and a workstream's brief. Each says whether
/// it counts, with no default, and a counted read records what it returned itself, so no caller has to remember to.
/// </summary>
internal sealed class MemoryReader(MemoryIndex index, PageTools pages, RecallPipeline pipeline, WorkstreamBrief brief)
{
    /// <summary>
    /// The search <c>recall</c> runs, stage by stage; counted, every unit it returned counts once. When a background sync
    /// gave up, every file is synced first.
    /// </summary>
    public async Task<RecallTrace> SearchAsync(
        string query, IndexFilter filter, RetrievalOverrides overrides, Usage usage, CancellationToken cancellationToken)
    {
        index.SyncAllIfStale();
        var trace = await pipeline.RunAsync(query, filter, overrides, cancellationToken);
        if (usage == Usage.Counted)
            index.RecordReturns([.. trace.Returned.Select(hit => hit.Id)]);
        return trace;
    }

    /// <summary>
    /// A whole page. The index follows the file first, so the section dates are current; counted, each section counts as
    /// returned.
    /// </summary>
    public PageReading Page(PageId id, Usage usage)
    {
        var (document, refusal) = pages.Load(id);
        if (document is null)
            return new PageReading(null, refusal);
        index.SyncPage(id);
        if (usage == Usage.Counted)
            index.RecordPageRead(id);
        return new PageReading(document, null);
    }

    /// <summary>
    /// The brief of <paramref name="workstream"/> within the dates; counted, every page its text lists counts once.
    /// <c>null</c> when the workstream is not established and has no pages and no activities.
    /// </summary>
    public BriefReading? Brief(string workstream, DateOnly? from, DateOnly? to, Usage usage)
    {
        if (brief.Gather(workstream, from, to) is not { } parts)
            return null;
        var (text, listed) = brief.Compose(parts);
        if (usage == Usage.Counted)
            index.RecordPagesListed(listed);
        return new BriefReading(parts, text);
    }
}
