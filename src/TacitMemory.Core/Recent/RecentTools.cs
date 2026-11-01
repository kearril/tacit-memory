using System.Globalization;
using TacitMemory.Core.Decisions;
using TacitMemory.Core.Git;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Models;
using TacitMemory.Core.Pages;
using TacitMemory.Core.Tools;

namespace TacitMemory.Core.Recent;

/// <summary>
/// The recent activity side of the memory tools: the <c>write_recent</c> operations. Callers serialize writes; this
/// class assumes it runs one write at a time.
/// </summary>
internal sealed class RecentTools(TacitOptions options, TacitConfig config, Action<string> notice, IDecisionModel decision)
{
    private const string Ops = "add_activity, update_mainline";

    /// <summary>The parameters each op takes; any other parameter given with an op is refused rather than ignored.</summary>
    private static readonly Dictionary<string, string[]> OpParameters = new()
    {
        ["add_activity"] = ["text", "project", "source", "replaces", "confirm_no_secret"],
        ["update_mainline"] = ["items", "hash", "confirm_no_secret"],
    };

    private readonly GitCli _git = new(config.MemoryPath, config.Language);
    private readonly SameActivityCheck _sameActivity = new(config, decision);

    public async Task<ToolResult> WriteAsync(WriteRecentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Op))
            return ToolText.Refuse("Missing parameter op.", $"Call write_recent again with op set to one of: {Ops}.");
        if (OpParameters.TryGetValue(request.Op, out var taken) && MisplacedParameters(request, taken) is { Count: > 0 } misplaced)
            return ToolText.Refuse(
                misplaced.Select(name => $"Parameter {name} does not apply to op {request.Op}.").ToList(),
                $"Call write_recent again with only the parameters for op {request.Op}: {string.Join(", ", taken)}.");

        return request.Op switch
        {
            "add_activity" => await AddActivityAsync(request, cancellationToken),
            "update_mainline" => await UpdateMainlineAsync(request, cancellationToken),
            _ => ToolText.Refuse($"Unknown op {ToolText.Quote(request.Op)}.", $"Call write_recent again with op set to one of: {Ops}."),
        };
    }

    private async Task<ToolResult> AddActivityAsync(WriteRecentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return ToolText.Refuse("Missing parameter text.", "Call write_recent again with op=add_activity and text set to the activity in one line.");
        var text = request.Text.Trim();
        if (text.StartsWith('['))
            return ToolText.Refuse(
                "Text must not start with '['; the project label comes from the project parameter.",
                "Call write_recent again with the project parameter instead of a [project] prefix in text.");

        var problems = new List<string>();
        var limit = config.Limits.ItemLength;
        if (text.Contains('\n') || text.Contains('\r'))
            problems.Add("Text must be one line.");
        if (Length(text) > limit)
            problems.Add($"{Length(text)} characters; the limit is {limit}");

        string? project = null;
        if (request.Project is not null)
        {
            project = request.Project.Trim().ToLowerInvariant();
            if (!PageId.IsProjectName(project))
                problems.Add($"Project {ToolText.Quote(request.Project)} may only use a-z, 0-9, '.', '_' and '-', must not start with '.' or '_', for example {PageId.Suggest(project)}.");
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
            return ToolText.Refuse(problems, "Fix these and call write_recent op=add_activity again.");

        if (request.ConfirmNoSecret != true)
        {
            var written = new List<WrittenText> { new("text", text) };
            if (source is not null)
                written.Add(new WrittenText("source", source));
            if (CredentialCheck.Run(written, "write_recent", "add_activity") is { } secret)
                return secret;
        }

        var day = options.Today;
        var relative = RecentFile.RelativePath(day);
        var path = Path.Combine(config.MemoryPath, relative);
        var read = TextFile.Read(path);
        if (read is null)
            return ToolText.Refuse(
                $"{relative} is not valid UTF-8, so writing to it would change its text.",
                $"Save {relative} as UTF-8, then call write_recent op=add_activity again.");
        var (existing, hasBom) = read.Value;

        var label = project is null ? "" : $"[{project}] ";
        var entryLine = $"- {label}{text}";
        var sourceLine = source is null ? null : $"  <!-- {StructureWords.Quote.In(config.Language)}「{source}」 -->";

        RecentEntry? replaced = null;
        if (request.Replaces is not null)
        {
            var today = RecentFile.EntriesOn(existing, day);
            replaced = today.FirstOrDefault(entry => string.Equals(entry.Code, request.Replaces.Trim(), StringComparison.OrdinalIgnoreCase));
            if (replaced is null)
                return ToolText.Refuse(
                    $"Parameter replaces does not match an activity from {day:yyyy-MM-dd}.",
                    "Call write_recent again with replaces set to the short code of one of today's activities listed below, or without replaces to add a new one.",
                    today.Count == 0
                        ? $"No activities are recorded for {day:yyyy-MM-dd}."
                        : string.Join('\n', today.Select(entry => $"[{entry.Code}] {entry.Body}")));
        }
        string? sameActivityNote = null;
        if (replaced is null)
        {
            var verdict = await _sameActivity.RunAsync(label + text, RecentFile.EntriesOn(existing, day), cancellationToken);
            if (verdict.Repeat is { } repeat)
                return ToolText.Ok(
                    $"Not added: today's activity [{repeat.Code}] is about the same thing: {repeat.Body}\n"
                    + $"Next step: write one merged sentence that covers both, then call write_recent again with op=add_activity, that sentence as text, and replaces set to {repeat.Code}. Nothing was written.");
            sameActivityNote = verdict.Note;
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
        var status = sameActivityNote is null ? $"{verb} activity [{code}]." : $"{verb} activity [{code}].\n\n{sameActivityNote}";
        if (after == existing)
            return ToolText.Ok($"{status}\n\n{MainlineBlock(CurrentMainline())}\n\n{await ActivitiesBlockAsync(cancellationToken)}");

        TextFile.Write(path, after, hasBom);
        var subject = replaced is null ? "recent: add" : "recent: replace";
        var commitProblem = await CommitAsync(dirty ? $"{subject} {code} (with user edits)" : $"{subject} {code}", relative, cancellationToken);
        var reply = $"{status}\n\n{MainlineBlock(CurrentMainline())}\n\n{await ActivitiesBlockAsync(cancellationToken)}";
        return commitProblem is null ? ToolText.Ok(reply) with { Written = true } : new ToolResult($"{reply}\n\n{commitProblem}", IsError: true, Written: true);
    }

    private async Task<ToolResult> UpdateMainlineAsync(WriteRecentRequest request, CancellationToken cancellationToken)
    {
        var relative = MainlineSection.RelativePath;
        var path = Path.Combine(config.MemoryPath, relative);
        var read = TextFile.Read(path);
        if (read is null)
            return ToolText.Refuse(
                $"{relative} is not valid UTF-8, so writing to it would change its text.",
                $"Save {relative} as UTF-8, then call write_recent op=update_mainline again.");
        var (existing, hasBom) = read.Value;

        var current = MainlineSection.Items(existing);
        var currentVersion = MainlineSection.VersionOf(current);
        if (string.IsNullOrWhiteSpace(request.Hash) || !string.Equals(request.Hash.Trim(), currentVersion, StringComparison.OrdinalIgnoreCase))
        {
            var reason = string.IsNullOrWhiteSpace(request.Hash)
                ? "Missing parameter hash."
                : $"Hash {ToolText.Quote(request.Hash)} does not match the current mainline.";
            return ToolText.Refuse(
                reason,
                $"Call write_recent again with op=update_mainline, hash set to {currentVersion}, and the items you want.",
                MainlineBlock(current));
        }

        if (request.Items is null)
            return ToolText.Refuse(
                "Missing parameter items.",
                "Call write_recent again with op=update_mainline and items set to the whole mainline, in order; an empty list clears it.");

        var items = request.Items.Select(item => item.Trim()).ToList();
        var problems = new List<string>();
        var maxItems = config.Limits.Mainline;
        if (items.Count > maxItems)
            problems.Add($"Mainline has {items.Count} items; the limit is {maxItems}.");
        var maxLength = config.Limits.ItemLength;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.Length == 0)
                problems.Add($"Item {i + 1} is empty.");
            else if (item.Contains('\n') || item.Contains('\r'))
                problems.Add($"Item {i + 1} must be one line.");
            else if (Length(item) > maxLength)
                problems.Add($"Item {i + 1} has {Length(item)} characters; the limit is {maxLength}.");
        }

        if (problems.Count > 0)
            return ToolText.Refuse(problems, "Fix these and call write_recent op=update_mainline again with the same hash.", MainlineBlock(current));

        if (request.ConfirmNoSecret != true)
        {
            var written = items.Select((item, i) => new WrittenText($"item {i + 1}", item)).ToList();
            if (CredentialCheck.Run(written, "write_recent", "update_mainline") is { } secret)
                return secret;
        }

        if (items.SequenceEqual(current, StringComparer.Ordinal))
            return ToolText.Ok($"Mainline unchanged.\n\n{MainlineBlock(items)}");

        // Checked before the write, as in add_activity: user edits to recent.md are noted in the commit subject.
        var dirty = File.Exists(path) && await _git.IsDirtyAsync(relative, cancellationToken);
        var updated = MainlineSection.Replace(existing, items, MainlineSection.HeadingIn(config.Language), TextFile.NewlineOf(existing));
        TextFile.Write(path, updated, hasBom);
        var commitProblem = await CommitAsync(dirty ? "recent: mainline (with user edits)" : "recent: mainline", relative, cancellationToken);
        var reply = $"Mainline updated.\n\n{MainlineBlock(items)}";
        return commitProblem is null ? ToolText.Ok(reply) with { Written = true } : new ToolResult($"{reply}\n\n{commitProblem}", IsError: true, Written: true);
    }

    /// <summary>The mainline as write replies show it: its version code, then its items, or <c>(empty)</c>.</summary>
    private static string MainlineBlock(IReadOnlyList<string> items) =>
        $"Mainline, version [{MainlineSection.VersionOf(items)}]:\n"
        + (items.Count == 0 ? "(empty)" : string.Join("\n", items.Select(item => "- " + item)));

    private IReadOnlyList<string> CurrentMainline() =>
        MainlineSection.Items(TextFile.Read(Path.Combine(config.MemoryPath, MainlineSection.RelativePath))?.Text ?? "");

    /// <summary>
    /// The activities dated on or after the day of the last commit that touched <c>recent.md</c>, newest first, at most
    /// ten. With no such commit, or only the init commit, every activity is listed.
    /// </summary>
    private async Task<string> ActivitiesBlockAsync(CancellationToken cancellationToken)
    {
        const int listed = 10;
        var since = await LastMainlineCommitDayAsync(cancellationToken);
        var folder = Path.Combine(config.MemoryPath, "recent");
        var months = Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.md").OrderBy(file => file, StringComparer.Ordinal).ToList()
            : new List<string>();

        var activities = new List<RecentFile.Activity>();
        foreach (var file in months)
        {
            var month = Path.GetFileNameWithoutExtension(file);
            if (since is { } start && string.CompareOrdinal(month, start.ToString("yyyy-MM", CultureInfo.InvariantCulture)) < 0)
                continue;
            if (TextFile.Read(file) is not { } read)
                continue;
            activities.AddRange(RecentFile.Activities(read.Text).Where(activity => since is null || activity.Day >= since.Value));
        }

        // Months and lines are read oldest first; reversing puts the newest line first, and the stable sort keeps that order within a day.
        var newestFirst = activities.AsEnumerable().Reverse().OrderByDescending(activity => activity.Day).ToList();
        if (newestFirst.Count == 0)
            return "No activities since the last recent.md commit.";

        var lines = new List<string> { "Activities since the last recent.md commit, newest first:" };
        lines.AddRange(newestFirst.Take(listed).Select(activity => $"- {activity.Day:yyyy-MM-dd} [{activity.Code}] {(activity.Project is null ? "" : $"[{activity.Project}] ")}{activity.Body}"));
        if (newestFirst.Count > listed)
            lines.Add($"{newestFirst.Count - listed} more not listed.");
        return string.Join("\n", lines);
    }

    /// <summary>The day of the last commit that touched <c>recent.md</c>; null with no such commit or when it is the init commit.</summary>
    private async Task<DateOnly?> LastMainlineCommitDayAsync(CancellationToken cancellationToken)
    {
        try
        {
            var line = (await _git.RunAsync(cancellationToken, "log", "-1", "--format=%as%x09%s", "--", MainlineSection.RelativePath)).Trim();
            var parts = line.Split('\t', 2);
            if (parts.Length < 2 || parts[1].StartsWith("init:", StringComparison.Ordinal))
                return null;
            return DateOnly.ParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch (TacitException)
        {
            return null;
        }
    }

    /// <summary>The parameters that were given but the op does not take.</summary>
    private static List<string> MisplacedParameters(WriteRecentRequest request, string[] taken)
    {
        var given = new (string Name, bool IsGiven)[]
        {
            ("text", request.Text is not null),
            ("project", request.Project is not null),
            ("source", request.Source is not null),
            ("replaces", request.Replaces is not null),
            ("items", request.Items is not null),
            ("hash", request.Hash is not null),
            ("confirm_no_secret", request.ConfirmNoSecret is not null),
        };
        return given.Where(parameter => parameter.IsGiven && !taken.Contains(parameter.Name)).Select(parameter => parameter.Name).ToList();
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
            notice(e.Message);
            return $"Not committed: the file is saved, but git failed ({e.Message}). Nothing to redo: it is committed with the next write to this file.";
        }
    }

    private static int Length(string text) => new StringInfo(text).LengthInTextElements;
}
