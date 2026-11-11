using Microsoft.Extensions.Logging;
using TacitMemory.Core.Logging;
using TacitMemory.Core.Decisions;
using TacitMemory.Core.Git;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Models;
using TacitMemory.Core.Pages;
using TacitMemory.Core.Tools;

namespace TacitMemory.Core.Recent;

/// <summary>
/// The recent activity side of the memory tools: the <c>write_recent</c> tool, which only adds or rewrites activities.
/// The workstream list is maintained by the consolidation, which edits <c>workstreams.md</c> directly. Callers serialize writes; this
/// class assumes it runs one write at a time.
/// </summary>
internal sealed class RecentTools(TacitOptions options, TacitConfig config, ServiceLog log, IDecisionModel decision)
{
    /// <summary>Every parameter <c>write_recent</c> takes, listed when an unknown one is refused.</summary>
    private const string Parameters = "text, workstream, source, replaces, confirm_no_secret";

    private readonly GitCli _git = new(config.MemoryPath, config.Language);
    private readonly SameTopicCheck _sameTopic = new(config, decision);

    public async Task<ToolResult> WriteAsync(WriteRecentRequest request, CancellationToken cancellationToken)
    {
        var problems = ToolText.UnknownParameters(request.UnknownParameters);
        if (problems.Count > 0)
            return ToolText.Refuse(
                problems,
                $"Call write_recent again with only these parameters: {Parameters}. The workstream list is maintained by the tacit-consolidate skill.");

        return await AddActivityAsync(request, cancellationToken);
    }

    private async Task<ToolResult> AddActivityAsync(WriteRecentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return ToolText.Refuse("Missing parameter text.", "Call write_recent again with text set to the activity in one line.");
        var text = request.Text.Trim();
        if (text.StartsWith('['))
            return ToolText.Refuse(
                "Text must not start with '['; the workstream label comes from the workstream parameter.",
                "Call write_recent again with the workstream parameter instead of a [workstream] prefix in text.");

        var problems = new List<string>();
        if (text.Contains('\n') || text.Contains('\r'))
            problems.Add("Text must be one line.");

        string? workstream = null;
        if (request.Workstream is not null)
        {
            workstream = request.Workstream.Trim().ToLowerInvariant();
            if (!PageId.IsWorkstreamName(workstream))
                problems.Add($"Workstream {ToolText.Quote(request.Workstream)} may only use a-z, 0-9, '.', '_' and '-', must not start with '.' or '_', for example {PageId.Suggest(workstream)}.");
        }

        var source = string.IsNullOrWhiteSpace(request.Source) ? null : request.Source.Trim();
        if (source is not null)
        {
            if (source.Contains('\n') || source.Contains('\r'))
                problems.Add("Source must be one line.");
            if (source.Contains("-->", StringComparison.Ordinal))
                problems.Add("Source must not contain \"-->\", which would end its comment.");
        }

        if (problems.Count > 0)
            return ToolText.Refuse(problems, "Fix these and call write_recent again.");

        if (request.ConfirmNoSecret != true)
        {
            var written = new List<WrittenText> { new("text", text) };
            if (source is not null)
                written.Add(new WrittenText("source", source));
            if (CredentialCheck.Run(written, "write_recent") is { } secret)
                return secret;
        }

        var day = options.Today;
        var relative = RecentFile.RelativePath(day);
        var path = Path.Combine(config.MemoryPath, relative);
        var read = TextFile.Read(path);
        if (read is null)
            return ToolText.Refuse(
                $"{relative} is not valid UTF-8, so writing to it would change its text.",
                $"Save {relative} as UTF-8, then call write_recent again.");
        var (existing, hasBom) = read.Value;

        var label = RecentFile.Label(workstream);
        var entryLine = $"- {label}{text}";
        var sourceLine = source is null ? null : $"  <!-- {StructureWords.Quote.In(config.Language)}「{source}」 -->";

        RecentFile.Activity? replaced = null;
        if (request.Replaces is not null)
        {
            var today = RecentFile.ActivitiesOn(existing, day);
            replaced = today.FirstOrDefault(entry => string.Equals(entry.Code, request.Replaces.Trim(), StringComparison.OrdinalIgnoreCase));
            if (replaced is null)
                return ToolText.Refuse(
                    $"Parameter replaces does not match an activity from {day:yyyy-MM-dd}.",
                    "Call write_recent again with replaces set to the short code of one of today's activities listed below, or without replaces to add a new one.",
                    today.Count == 0
                        ? $"No activities are recorded for {day:yyyy-MM-dd}."
                        : string.Join('\n', today.Select(entry => $"[{entry.Code}] {entry.Labeled}")));
        }
        string? sameTopicNote = null;
        if (replaced is null)
        {
            var verdict = await _sameTopic.RunAsync(label + text, RecentFile.ActivitiesOn(existing, day), cancellationToken);
            if (verdict.Same is { } same)
                return ToolText.Ok(
                    $"Not added: today's activity [{same.Code}] is about the same thing: {same.Labeled}\n"
                    + $"Next step: write one merged sentence that covers both, then call write_recent again with that sentence as text and replaces set to {same.Code}. Nothing was written.");
            sameTopicNote = verdict.Note;
        }

        // Checked before the write: a file that already had user edits gets that noted in its commit subject.
        var dirty = File.Exists(path) && await _git.IsDirtyAsync(relative, cancellationToken);

        string after;
        int line;
        if (replaced is null)
        {
            var entry = new List<string> { entryLine };
            if (sourceLine is not null)
                entry.Add(sourceLine);
            (after, line) = RecentFile.Insert(existing, day, entry);
        }
        else
        {
            after = RecentFile.Replace(existing, replaced.Line, entryLine, sourceLine);
            line = replaced.Line;
        }

        var code = RecentFile.CodeAt(after, line);
        var verb = replaced is null ? "Added" : "Replaced";
        var status = $"{verb} activity [{code}].";
        // The decision model being down is the one case the reply runs to a second line: the same-topic check did not happen.
        if (sameTopicNote is not null)
            status = $"{status}\n{sameTopicNote}";
        if (after == existing)
            return ToolText.Ok(status);

        TextFile.Write(path, after, hasBom);
        var subject = replaced is null ? "recent: add" : "recent: replace";
        var commitProblem = await CommitAsync(dirty ? $"{subject} {code} (with user edits)" : $"{subject} {code}", relative, cancellationToken);
        return commitProblem is null ? ToolText.Ok(status) with { Written = true } : new ToolResult($"{status}\n\n{commitProblem}", IsError: true, Written: true);
    }

    /// <summary>Commits the month file; on failure, tells the user and returns the note for the agent. The file stays written.</summary>
    private async Task<string?> CommitAsync(string message, string relativePath, CancellationToken cancellationToken)
    {
        try
        {
            await _git.CommitAsync(message, [relativePath], cancellationToken);
            return null;
        }
        catch (TacitException e) when (e.Error == TacitError.GitFailed)
        {
            log.Notify(LogLevel.Warning, e.Message);
            return $"Not committed: the file is saved, but git failed ({e.Message}). Nothing to redo: it is committed with the next write to this file.";
        }
    }
}
