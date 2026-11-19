using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TacitMemory.Core.Consolidation;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Logging;
using TacitMemory.Core.Maintenance;
using TacitMemory.Core.Pages;
using TacitMemory.Core.Persona;
using TacitMemory.Core.Recent;
using TacitMemory.Core.Retrieval;
using TacitMemory.Core.Sync;
using TacitMemory.Core.Upkeep;

namespace TacitMemory.Core;

/// <summary>
/// The memory tools as MCP serves them: the four tools, the server instructions and the consolidation report. Reads share
/// the gate; writes run one at a time, in the order they arrive. Each call uses the memory the service runs now, so one
/// taken before a reload reaches the reopened memory.
/// </summary>
public sealed class ServiceTools
{
    private readonly TacitOptions _options;
    private readonly MaintenanceGate _gate;
    private readonly DerivedUpkeep _upkeep;
    private readonly ServiceLog _log;
    private readonly Func<ToolRuntime> _runtime;

    internal ServiceTools(TacitOptions options, MaintenanceGate gate, DerivedUpkeep upkeep, ServiceLog log, Func<ToolRuntime> runtime)
    {
        _options = options;
        _gate = gate;
        _upkeep = upkeep;
        _log = log;
        _runtime = runtime;
    }

    /// <summary>
    /// The server instructions, generated on first use and kept until something they are built from may have changed: a
    /// prompt document, the persona or its candidates, a page or activity file, <c>workstreams.md</c> (which the consolidation
    /// reminder reads), the memory repository's commits, or the status (unavailable models, skipped files, the date).
    /// Builds run one at a time.
    /// </summary>
    public async Task<string> ServerInstructionsAsync(CancellationToken cancellationToken = default)
    {
        using var hold = await _gate.ReadAsync(cancellationToken);
        return await _upkeep.InstructionsAsync(cancellationToken);
    }

    /// <summary>
    /// The text of the consolidation report resource, built from the repository as it is now: the repository's path, today's
    /// date and the consolidation report. A configured sync pulls first, in the write queue, so the report covers what the
    /// other machine wrote; the report itself only reads. Cancelling it abandons the report.
    /// </summary>
    public async Task<string> ConsolidationReportAsync(CancellationToken cancellationToken = default)
    {
        using (await _gate.WriteAsync(cancellationToken))
            await _runtime().Sync.RunAsync(SyncTrigger.Report, cancellationToken);
        using var hold = await _gate.ReadAsync(cancellationToken);
        var runtime = _runtime();
        return await ConsolidationResource.RenderAsync(_options, runtime.Config, runtime.Consolidation, cancellationToken);
    }

    /// <summary>The <c>recall</c> tool: searches pages or reads one. Never changes a file.</summary>
    public Task<ToolResult> RecallAsync(RecallRequest request, CancellationToken cancellationToken = default) =>
        RecordToolCall("recall", null, Targets(request.Page), () => RecallCoreAsync(request, cancellationToken));

    private async Task<(ToolResult, string)> RecallCoreAsync(RecallRequest request, CancellationToken cancellationToken)
    {
        using var hold = await _gate.ReadAsync(cancellationToken);
        var runtime = _runtime();
        var (result, results) = await runtime.Recall.RunAsync(request, cancellationToken);
        return (result, RecallTool.LogDetails(request, results, runtime.Config.Language));
    }

    /// <summary>The <c>write_page</c> tool. Writes run one at a time, in the order they arrive.</summary>
    public Task<ToolResult> WritePageAsync(WritePageRequest request, CancellationToken cancellationToken = default) =>
        RecordToolCall("write_page", request.Op, Targets(request.Page, request.Hash), () => WritePageCoreAsync(request, cancellationToken));

    private async Task<ToolResult> WritePageCoreAsync(WritePageRequest request, CancellationToken cancellationToken)
    {
        using var hold = await WriteHoldAsync(cancellationToken);
        return await _runtime().Pages.WriteAsync(request, cancellationToken);
    }

    /// <summary>The <c>write_recent</c> tool. Shares the write queue with <c>write_page</c>, so writes land in arrival order.</summary>
    public Task<ToolResult> WriteRecentAsync(WriteRecentRequest request, CancellationToken cancellationToken = default) =>
        RecordToolCall("write_recent", null, Targets(request.Replaces), () => WriteRecentCoreAsync(request, cancellationToken));

    private async Task<ToolResult> WriteRecentCoreAsync(WriteRecentRequest request, CancellationToken cancellationToken)
    {
        using var hold = await WriteHoldAsync(cancellationToken);
        return await _runtime().Recent.WriteAsync(request, cancellationToken);
    }

    /// <summary>
    /// The <c>write_persona</c> tool. Shares the write queue with the other writes. A written persona re-renders the bound
    /// resident blocks the same way a manual edit of <c>persona.md</c> does: debounced, and a failure only tells the user.
    /// </summary>
    public Task<ToolResult> WritePersonaAsync(WritePersonaRequest request, CancellationToken cancellationToken = default) =>
        RecordToolCall("write_persona", request.Op, Targets(request.Hash), () => WritePersonaCoreAsync(request, cancellationToken));

    private async Task<ToolResult> WritePersonaCoreAsync(WritePersonaRequest request, CancellationToken cancellationToken)
    {
        using var hold = await WriteHoldAsync(cancellationToken);
        return await _runtime().Persona.WriteAsync(request, cancellationToken);
    }

    /// <summary>
    /// Takes the write slot, then lets a configured sync pull, so the write changes the files as the other machine last left
    /// them. A pull that fails does not stop the write: it is recorded, and the write is committed here and pushed later.
    /// </summary>
    private async Task<Hold> WriteHoldAsync(CancellationToken cancellationToken)
    {
        var hold = await _gate.WriteAsync(cancellationToken);
        try
        {
            await _runtime().Sync.RunAsync(SyncTrigger.BeforeWrite, cancellationToken);
            return hold;
        }
        catch
        {
            hold.Dispose();
            throw;
        }
    }

    /// <summary>Runs a memory tool whose event adds nothing to the targets; see the overload that takes details.</summary>
    private Task<ToolResult> RecordToolCall(string tool, string? op, string targets, Func<Task<ToolResult>> call) =>
        RecordToolCall(tool, op, targets, async () => (await call(), ""));

    /// <summary>
    /// Runs one memory tool and writes its event: the tool, its op, the page IDs and short codes the call names, the details
    /// the call adds, the outcome and the time taken. It is the only place tool calls are logged. No event carries the text a
    /// call sends, except the query of a <c>recall</c> search, which the details hold.
    /// </summary>
    private async Task<ToolResult> RecordToolCall(string tool, string? op, string targets, Func<Task<(ToolResult Result, string Details)>> call)
    {
        var started = Stopwatch.GetTimestamp();
        (ToolResult Result, string Details)? done = null;
        try
        {
            done = await call();
            return done.Value.Result;
        }
        finally
        {
            var outcome = done is not { Result: var result } ? LogMessages.ToolAborted
                : result.IsError ? LogMessages.ToolRefused
                : result.Written ? LogMessages.ToolWritten
                : LogMessages.ToolDone;
            _log.Record(
                LogLevel.Information, LogMessages.ToolCall, tool, OpName(op), targets, done?.Details ?? "", outcome.In(_runtime().Config.Language),
                (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    /// <summary>The page IDs and short codes a call names, as far as they have their shape; <c>-</c> when none does. Other text is never logged.</summary>
    private static string Targets(params string?[] names)
    {
        var shown = new List<string>();
        foreach (var name in names)
        {
            if (name is null)
                continue;
            if (PageId.TryParse(name, out var id, out _))
                shown.Add(id.ToString());
            else if (ShortCode.LooksLike(name))
                shown.Add(name);
        }

        return shown.Count == 0 ? "-" : string.Join(' ', shown);
    }

    /// <summary>The op of a call when it is a plain word, such as <c>upsert_section</c>; otherwise <c>-</c>.</summary>
    private static readonly Regex OpShape = new("^[a-z_]{1,32}$", RegexOptions.CultureInvariant);

    private static string OpName(string? op) => op is not null && OpShape.IsMatch(op) ? op : "-";
}

/// <summary>The parts of the running memory the tools use; a reload replaces them.</summary>
internal sealed record ToolRuntime(
    TacitConfig Config, RecallTool Recall, PageTools Pages, RecentTools Recent, PersonaTools Persona, ConsolidationServices Consolidation,
    MemorySync Sync);
