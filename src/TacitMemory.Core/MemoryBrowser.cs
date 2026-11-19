using TacitMemory.Core.Index;
using TacitMemory.Core.Maintenance;
using TacitMemory.Core.Pages;
using TacitMemory.Core.Persona;
using TacitMemory.Core.Recent;
using TacitMemory.Core.Retrieval;
using TacitMemory.Core.Workstreams;

namespace TacitMemory.Core;

/// <summary>
/// The memory as the memory page browses it: the persona, the workstreams and a brief, the activity log and the detail
/// pages, and the recall trace. Each view waits as a read does, then reads the memory the service runs now, so one taken
/// before a reload reads the reopened memory. Browsing counts no use and changes no file.
/// </summary>
public sealed class MemoryBrowser
{
    private readonly MaintenanceGate _gate;
    private readonly Func<BrowseRuntime> _runtime;
    private readonly Func<DateOnly> _today;

    internal MemoryBrowser(MaintenanceGate gate, Func<BrowseRuntime> runtime, Func<DateOnly> today)
    {
        _gate = gate;
        _runtime = runtime;
        _today = today;
    }

    /// <summary>The persona and its candidates, with the two files' paths and problems.</summary>
    public Task<PersonaReport> PersonaAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(memory => PersonaBrowse.Read(memory.Config), cancellationToken);

    /// <summary>The established workstreams and the names in use that are not.</summary>
    public Task<WorkstreamList> WorkstreamsAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(memory => WorkstreamBrowse.List(memory.Config), cancellationToken);

    /// <summary>A workstream's brief, whole and structured, gathered as <c>recall</c> gathers it.</summary>
    /// <exception cref="TacitException"><see cref="TacitError.MemoryNotFound"/> when there is no such workstream.</exception>
    public Task<WorkstreamBriefView> WorkstreamBriefAsync(string workstream, CancellationToken cancellationToken = default) =>
        ReadAsync(memory => WorkstreamBrowse.Brief(memory.Config, memory.Reader, workstream), cancellationToken);

    /// <summary>The months of the activity log that have a file, newest first, and the current month.</summary>
    public Task<ActivityMonths> ActivityMonthsAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(memory => ActivityBrowse.Months(memory.Config.MemoryPath, _today()), cancellationToken);

    /// <summary>One month of the activity log by day, with each activity's returns, its summaries and the file's problems.</summary>
    /// <exception cref="TacitException">
    /// <see cref="TacitError.RequestInvalid"/> when the month is not written yyyy-MM; <see cref="TacitError.MemoryNotFound"/>
    /// when it has no file and is not the current month.
    /// </exception>
    public Task<ActivityMonthView> ActivityMonthAsync(string month, CancellationToken cancellationToken = default) =>
        ReadAsync(memory => ActivityBrowse.Month(memory.Config, memory.Index, _today(), month), cancellationToken);

    /// <summary>Every detail page, for the memory page's list.</summary>
    public Task<PageList> PagesAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(memory => PageBrowse.List(memory.Config, memory.Index), cancellationToken);

    /// <summary>One detail page, with its sections' dates and counts.</summary>
    /// <exception cref="TacitException">
    /// <see cref="TacitError.RequestInvalid"/> for a page ID that is not valid; <see cref="TacitError.MemoryNotFound"/> when there is no such page.
    /// </exception>
    public Task<PageView> PageAsync(string workstream, string slug, CancellationToken cancellationToken = default) =>
        ReadAsync(memory => PageBrowse.Read(memory.Config, memory.Index, memory.Reader, workstream, slug), cancellationToken);

    /// <summary>
    /// A recall trace: the search <c>recall</c> runs for the same parameters, stage by stage, with up to three retrieval
    /// values replaced. Counts no return and changes no file.
    /// </summary>
    /// <exception cref="TacitException"><see cref="TacitError.RequestInvalid"/> with every problem of the request.</exception>
    public async Task<RecallTraceReport> TraceRecallAsync(RecallTraceRequest request, CancellationToken cancellationToken = default)
    {
        var search = RecallTraceRun.Check(request, _runtime().Config.Language);
        using var hold = await _gate.ReadAsync(cancellationToken);
        var memory = _runtime();
        var trace = await memory.Reader.SearchAsync(search, Usage.NotCounted, cancellationToken);
        return RecallTraceRun.Report(trace, memory.Config);
    }

    /// <summary>Runs <paramref name="read"/> on the memory the service runs once the read slot is held.</summary>
    private async Task<T> ReadAsync<T>(Func<BrowseRuntime, T> read, CancellationToken cancellationToken)
    {
        using var hold = await _gate.ReadAsync(cancellationToken);
        return read(_runtime());
    }
}

/// <summary>What browsing reads: the config the service runs, its index and its reader.</summary>
internal sealed record BrowseRuntime(TacitConfig Config, MemoryIndex Index, MemoryReader Reader);
