using System.Globalization;
using TacitMemory.Core.Decisions;
using TacitMemory.Core.Git;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Pages;
using TacitMemory.Core.Tools;

namespace TacitMemory.Core.Recent;

/// <summary>
/// The recent activity side of the memory tools: the <c>write_recent</c> operations. Callers serialize writes; this
/// class assumes it runs one write at a time.
/// </summary>
internal sealed class RecentTools(TacitOptions options, TacitConfig config, Action<string> notice)
{
    private const string Ops = "add_activity, update_mainline";

    /// <summary>The parameters each op takes; any other parameter given with an op is refused rather than ignored.</summary>
    private static readonly Dictionary<string, string[]> OpParameters = new()
    {
        ["add_activity"] = ["text", "project", "source", "replaces", "confirm_no_secret"],
        ["update_mainline"] = ["items", "hash", "confirm_no_secret"],
    };

    private readonly GitCli _git = new(config.MemoryPath, config.Language);

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
            "update_mainline" => ToolText.Refuse(
                "Op update_mainline is not available yet.",
                "Call write_recent again with op=add_activity to record an activity."),
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

        if (request.Replaces is not null)
            return ToolText.Refuse("Parameter replaces is not available yet.", "Call write_recent again without replaces.");

        var day = options.Today;
        var relative = RecentFile.RelativePath(day);
        var path = Path.Combine(config.MemoryPath, relative);
        var read = TextFile.Read(path);
        if (read is null)
            return ToolText.Refuse(
                $"{relative} is not valid UTF-8, so writing to it would change its text.",
                $"Save {relative} as UTF-8, then call write_recent op=add_activity again.");
        var (existing, hasBom) = read.Value;

        // Checked before the write: a file that already had user edits gets that noted in its commit subject.
        var dirty = File.Exists(path) && await _git.IsDirtyAsync(relative, cancellationToken);
        var label = project is null ? "" : $"[{project}] ";
        var entry = new List<string> { $"- {label}{text}" };
        if (source is not null)
            entry.Add($"  <!-- {StructureWords.Quote.In(config.Language)}「{source}」 -->");

        var (after, line) = RecentFile.Insert(existing, day, entry);
        TextFile.Write(path, after, hasBom);
        var code = RecentFile.CodeAt(after, line);
        var commitProblem = await CommitAsync(dirty ? $"recent: add {code} (with user edits)" : $"recent: add {code}", relative, cancellationToken);
        var reply = $"Added activity [{code}].";
        return commitProblem is null ? ToolText.Ok(reply) : new ToolResult($"{reply}\n\n{commitProblem}", IsError: true);
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
            return $"Not committed: the activity is saved, but git failed ({e.Message}). Nothing to redo: it is committed with the next write to this file.";
        }
    }

    private static int Length(string text) => new StringInfo(text).LengthInTextElements;
}
