using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TacitMemory.Core.Git;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Logging;

namespace TacitMemory.Core.Sync;

/// <summary>What started a sync; the automatic ones wait while the sync is paused.</summary>
internal enum SyncTrigger
{
    /// <summary><c>tacit sync</c> or the management API: tries again even after a conflict.</summary>
    Manual,

    BeforeWrite,
    AfterWrite,
    Timer,

    /// <summary>The consolidation report, which pulls before it reads the repository.</summary>
    Report,
}

/// <summary>
/// The sync of the memory repository with the git remote in <c>sync.remote</c>; every network operation of the sync goes
/// through here. One sync fetches, then fast-forwards when this machine has no commits of its own, or rebases its commits
/// onto the remote's when it has, then pushes what the remote lacks. It never touches a file with changes nobody has
/// committed, never forces a push, and after a failed rebase puts the repository back as it was. The caller holds the
/// write slot, so a sync never runs beside a write.
/// <para>
/// The last sync is kept in <c>sync.json</c> in <c>TACIT_HOME</c>, which no backup takes. A conflict, unrelated histories
/// or a restore pause the automatic syncs until a sync on request succeeds, or a side is chosen.
/// </para>
/// </summary>
/// <param name="pulled">Called once a sync has brought commits from the remote into the files, still in the write slot.</param>
internal sealed class MemorySync(TacitOptions options, TacitConfig config, ServiceLog log, Action pulled)
{
    /// <summary>The name of the remote tacit keeps in the memory repository.</summary>
    public const string RemoteName = "tacit";

    /// <summary>The state file's name under <c>TACIT_HOME</c>.</summary>
    public const string StateFileName = "sync.json";

    /// <summary>
    /// The attributes the sync gives the repository in <c>.git/info/attributes</c>, which applies to every git command run
    /// there and is never committed: LF in every working tree whatever <c>core.autocrlf</c> says, and a union merge of the
    /// files that only grow, so two machines adding activities on one day, or occurrences under one candidate, keep both.
    /// </summary>
    public const string Attributes = "* text=auto eol=lf\n/recent/*.md merge=union\n/candidates.md merge=union\n";

    private static readonly JsonSerializerOptions StateJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        TypeInfoResolver = CoreJson.Default,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter<SyncOutcome>() },
    };

    private readonly GitCli _git = new(config.MemoryPath, config.Language);

    private MemoryLanguage Language => config.Language;

    /// <summary>Whether <c>sync.remote</c> is set.</summary>
    public bool Enabled => config.Sync.Remote is not null;

    /// <summary>The last sync, or <c>null</c> when there was none.</summary>
    /// <exception cref="IOException">The record cannot be read.</exception>
    /// <exception cref="JsonException">The record is not valid JSON of the expected shape.</exception>
    public static SyncRecord? ReadLast(TacitOptions options)
    {
        var path = StatePath(options);
        return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), StateJson.For<SyncRecord>()) : null;
    }

    /// <summary>
    /// Syncs once, unless the sync is off, or <paramref name="trigger"/> is automatic and the sync is paused. A sync on
    /// request after a restore does nothing either: it waits for a side to be chosen. Failures of git end up in the record
    /// and the result, never as an exception.
    /// </summary>
    public async Task<SyncResult> RunAsync(SyncTrigger trigger, CancellationToken cancellationToken)
    {
        if (config.Sync.Remote is not { } remote)
            return new SyncResult(false, null, SyncMessages.Off.In(Language));

        var last = TryReadLast(options);
        if (last is { Paused: true } && (trigger != SyncTrigger.Manual || last.Outcome == SyncOutcome.RestorePaused))
            return Result(last, remote, 0, 0);

        var (record, pulledCount, pushedCount) = await AttemptAsync(remote, cancellationToken);
        Save(record);
        var result = Result(record, remote, pulledCount, pushedCount);
        if (record.Outcome != SyncOutcome.Synced)
            log.Record(LogLevel.Warning, LogMessages.SyncNotDone, TriggerName(trigger), result.Report);
        else if (trigger == SyncTrigger.Manual)
            log.Record(LogLevel.Information, LogMessages.SyncDone, result.Report);
        return result;
    }

    /// <summary>Puts the sync on pause after a restore, until a side is chosen. Only called while the sync is on.</summary>
    public void PauseAfterRestore()
    {
        Save(new SyncRecord(options.Time.GetUtcNow(), SyncOutcome.RestorePaused, null));
        log.Record(LogLevel.Warning, LogMessages.SyncPausedAfterRestore);
    }

    /// <summary>
    /// Fetches and says where the two sides stand, for choosing one. Works in the middle of an unfinished rebase too: the
    /// branch is the one being rebased, and this machine's side is that branch as it was before the rebase. Refused while
    /// the sync is off; a remote that cannot be reached is a git failure.
    /// </summary>
    public async Task<SidePosition> FetchSidesAsync(CancellationToken cancellationToken)
    {
        var remote = config.Sync.Remote ?? throw new TacitException(TacitError.RequestInvalid, SyncMessages.OffRefused.In(Language));
        await PointRemoteAsync(remote, cancellationToken);
        await WriteAttributesAsync(_git, cancellationToken);
        var branch = await BranchAsync(_git, cancellationToken)
            ?? throw new TacitException(TacitError.GitFailed, SyncMessages.NoBranch.In(Language));
        await _git.RunRemoteAsync(cancellationToken, "fetch", "--quiet", "--prune", RemoteName);
        var local = $"refs/heads/{branch}";
        var tracking = TrackingRef(branch);
        if (!await RefExistsAsync(_git, tracking, cancellationToken))
            return new SidePosition(remote, branch, null, Count(await _git.RunAsync(cancellationToken, "rev-list", "--count", local)), 0);
        var (ahead, behind) = await DivergenceAsync(_git, tracking, local, cancellationToken);
        return new SidePosition(remote, branch, (await _git.RunAsync(cancellationToken, "rev-parse", tracking)).Trim(), ahead, behind);
    }

    /// <summary>What choosing <paramref name="side"/> replaces; refused for the remote's side when the remote has no branch to take.</summary>
    public SyncSidePreview Preview(SyncSide side, SidePosition position)
    {
        var report = side == SyncSide.Local
            ? SyncMessages.PreviewLocal.Format(Language, position.Remote, position.Branch, position.RemoteOnly)
            : SyncMessages.PreviewRemote.Format(Language, position.Remote, position.Branch, position.LocalOnly);
        if (side == SyncSide.Remote)
            RequireRemoteBranch(position);
        return new SyncSidePreview(
            side, position.RemoteHead, position.LocalOnly, position.RemoteOnly, report,
            SyncMessages.SidePrompt.In(Language), SyncMessages.SideCancelled.In(Language));
    }

    /// <summary>
    /// Refuses the choice when the remote's branch is no longer where the preview saw it, so nothing another machine pushed
    /// since is dropped without being shown.
    /// </summary>
    public void RequireUnchanged(SidePosition position, string? previewedRemoteHead)
    {
        if (position.RemoteHead != previewedRemoteHead)
            throw new TacitException(TacitError.RemoteMoved, SyncMessages.RemoteMoved.Format(Language, position.Remote, position.Branch));
    }

    /// <summary>
    /// Replaces the remote's branch with this machine's history, dropping an unfinished rebase first. The push is forced only
    /// over the commit the fetch saw, so a commit pushed in between is refused rather than lost.
    /// </summary>
    public async Task<SyncResult> UseLocalAsync(SidePosition position, CancellationToken cancellationToken)
    {
        await AbortRebaseAsync(cancellationToken);
        var reference = $"refs/heads/{position.Branch}";
        await _git.RunRemoteAsync(
            cancellationToken, "push", "--quiet", $"--force-with-lease={reference}:{position.RemoteHead}", RemoteName, $"{reference}:{reference}");
        return Chosen(SyncMessages.UsedLocal.Format(Language, position.Remote, position.Branch), "local");
    }

    /// <summary>
    /// Resets this machine's repository to the remote's branch, dropping an unfinished rebase first. The caller has written
    /// the backup and rebuilds the index after.
    /// </summary>
    public async Task<SyncResult> UseRemoteAsync(SidePosition position, string backup, CancellationToken cancellationToken)
    {
        RequireRemoteBranch(position);
        await AbortRebaseAsync(cancellationToken);
        await _git.RunAsync(cancellationToken, "reset", "--hard", "--quiet", position.RemoteHead!);
        return Chosen(SyncMessages.UsedRemote.Format(Language, position.Remote, position.Branch, backup), "remote");
    }

    /// <summary>Refuses to take the remote's side when the remote has no branch to take.</summary>
    public void RequireRemoteBranch(SidePosition position)
    {
        if (position.RemoteHead is null)
            throw new TacitException(TacitError.RemoteEmpty, SyncMessages.RemoteEmpty.Format(Language, position.Remote, position.Branch));
    }

    private async Task AbortRebaseAsync(CancellationToken cancellationToken)
    {
        if (await RebaseInProgressAsync(cancellationToken))
            await _git.RunAsync(cancellationToken, "rebase", "--abort");
    }

    private SyncResult Chosen(string report, string side)
    {
        var record = Record(SyncOutcome.Synced, null);
        Save(record);
        log.Record(LogLevel.Information, LogMessages.SyncSideChosen, side, report);
        return new SyncResult(true, record, report);
    }

    /// <summary>
    /// How many commits of this branch the remote does not have yet, as the last fetch saw it; every commit of the branch
    /// when it has never been pushed. Touches no network.
    /// </summary>
    public static async Task<int> UnpushedAsync(TacitConfig config, CancellationToken cancellationToken)
    {
        var git = new GitCli(config.MemoryPath, config.Language);
        if (await BranchAsync(git, cancellationToken) is not { } branch)
            return 0;
        var local = $"refs/heads/{branch}";
        var tracking = TrackingRef(branch);
        var range = await RefExistsAsync(git, tracking, cancellationToken) ? $"{tracking}..{local}" : local;
        return Count(await git.RunAsync(cancellationToken, "rev-list", "--count", range));
    }

    /// <summary>
    /// The branch the repository is on, or, in the middle of a rebase, the branch being rebased; <c>null</c> when it is on
    /// no branch.
    /// </summary>
    public static async Task<string?> BranchAsync(GitCli git, CancellationToken cancellationToken)
    {
        var (code, branch, _) = await git.TryRunAsync(cancellationToken, "symbolic-ref", "--quiet", "--short", "HEAD");
        if (code == 0)
            return branch.Trim();
        foreach (var folder in RebaseFolders)
        {
            var headName = Path.Combine(await GitPathAsync(git, folder, cancellationToken), "head-name");
            if (File.Exists(headName) && File.ReadAllText(headName).Trim() is var name && name.StartsWith("refs/heads/", StringComparison.Ordinal))
                return name["refs/heads/".Length..];
        }
        return null;
    }

    /// <summary>
    /// Writes <see cref="Attributes"/> to the repository's <c>.git/info/attributes</c>, unless it already says exactly that.
    /// </summary>
    public static async Task WriteAttributesAsync(GitCli git, CancellationToken cancellationToken)
    {
        var path = await GitPathAsync(git, "info/attributes", cancellationToken);
        if (File.Exists(path) && File.ReadAllText(path) == Attributes)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Attributes);
    }

    /// <summary>The last sync, or <c>null</c> when there was none or its record cannot be read, which pauses nothing.</summary>
    public static SyncRecord? TryReadLast(TacitOptions options)
    {
        try
        {
            return ReadLast(options);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // The next sync writes a fresh record.
            return null;
        }
    }

    private async Task<(SyncRecord Record, int Pulled, int Pushed)> AttemptAsync(string remote, CancellationToken cancellationToken)
    {
        try
        {
            return await CycleAsync(remote, cancellationToken);
        }
        catch (TacitException e) when (e.Error == TacitError.GitFailed)
        {
            return (Record(SyncOutcome.Failed, e.Message), 0, 0);
        }
    }

    private async Task<(SyncRecord Record, int Pulled, int Pushed)> CycleAsync(string remote, CancellationToken cancellationToken)
    {
        await PointRemoteAsync(remote, cancellationToken);
        await WriteAttributesAsync(_git, cancellationToken);
        if (await RebaseInProgressAsync(cancellationToken))
            return (Record(SyncOutcome.Conflict, SyncMessages.RebaseInProgress.In(Language)), 0, 0);
        if (await BranchAsync(_git, cancellationToken) is not { } branch)
            return (Record(SyncOutcome.Failed, SyncMessages.NoBranch.In(Language)), 0, 0);

        await _git.RunRemoteAsync(cancellationToken, "fetch", "--quiet", "--prune", RemoteName);
        var tracking = TrackingRef(branch);
        if (!await RefExistsAsync(_git, tracking, cancellationToken))
        {
            // The remote has no such branch yet: the first machine pushes its history.
            var all = Count(await _git.RunAsync(cancellationToken, "rev-list", "--count", "HEAD"));
            await PushAsync(branch, cancellationToken);
            return (Record(SyncOutcome.Synced, null), 0, all);
        }

        // merge-base exits 1 when there is no common commit; anything else is git failing.
        var (baseCode, _, baseError) = await _git.TryRunAsync(cancellationToken, "merge-base", "HEAD", tracking);
        if (baseCode == 1)
            return (Record(SyncOutcome.Unrelated, null), 0, 0);
        if (baseCode != 0)
            throw new TacitException(TacitError.GitFailed, Messages.GitFailed.Format(Language, "merge-base", baseError.Trim()));

        var (ahead, behind) = await DivergenceAsync(_git, tracking, "HEAD", cancellationToken);
        if (behind > 0)
        {
            var blocked = await BlockingEditsAsync(tracking, rebasing: ahead > 0, cancellationToken);
            if (blocked.Count > 0)
                return (Record(SyncOutcome.HandEdits, string.Join(", ", blocked)), 0, 0);

            if (ahead == 0)
                await _git.RunAsync(cancellationToken, "merge", "--ff-only", "--quiet", tracking);
            else if (await RebaseAsync(tracking, cancellationToken) is { } conflicted)
                return (Record(SyncOutcome.Conflict, conflicted), 0, 0);
            pulled();
        }

        if (ahead > 0)
            await PushAsync(branch, cancellationToken);
        return (Record(SyncOutcome.Synced, null), behind, ahead);
    }

    /// <summary>The commits only <paramref name="local"/> has, and those only <paramref name="tracking"/> has.</summary>
    private static async Task<(int Ahead, int Behind)> DivergenceAsync(GitCli git, string tracking, string local, CancellationToken cancellationToken)
    {
        var counts = (await git.RunAsync(cancellationToken, "rev-list", "--count", "--left-right", $"{local}...{tracking}"))
            .Split('\t', StringSplitOptions.TrimEntries);
        return (Count(counts[0]), Count(counts[1]));
    }

    /// <summary>Adds the remote under <see cref="RemoteName"/>, or points it at the configured address when it names another.</summary>
    private async Task PointRemoteAsync(string remote, CancellationToken cancellationToken)
    {
        var (code, current, _) = await _git.TryRunAsync(cancellationToken, "remote", "get-url", RemoteName);
        if (code != 0)
            await _git.RunAsync(cancellationToken, "remote", "add", RemoteName, remote);
        else if (current.Trim() != remote)
            await _git.RunAsync(cancellationToken, "remote", "set-url", RemoteName, remote);
    }

    private async Task<bool> RebaseInProgressAsync(CancellationToken cancellationToken)
    {
        foreach (var folder in RebaseFolders)
        {
            if (Directory.Exists(await GitPathAsync(_git, folder, cancellationToken)))
                return true;
        }
        return false;
    }

    private static readonly string[] RebaseFolders = ["rebase-merge", "rebase-apply"];

    /// <summary>The full path of a file or folder inside the repository's git directory.</summary>
    private static async Task<string> GitPathAsync(GitCli git, string name, CancellationToken cancellationToken) =>
        Path.GetFullPath((await git.RunAsync(cancellationToken, "rev-parse", "--git-path", name)).Trim(), git.WorkingDirectory);

    /// <summary>
    /// The files with changes nobody has committed that the sync would have to change: those the remote changed since the
    /// common commit, and, for a rebase, which git refuses over any tracked change, every tracked file with changes.
    /// </summary>
    private async Task<IReadOnlyList<string>> BlockingEditsAsync(string tracking, bool rebasing, CancellationToken cancellationToken)
    {
        var incoming = (await _git.RunAsync(cancellationToken, "diff", "--name-only", "-z", $"HEAD...{tracking}"))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
        var entries = (await _git.RunAsync(cancellationToken, "status", "--porcelain", "-z", "--untracked-files=all"))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var blocked = new List<string>();
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry.Length < 4)
                continue;
            var path = entry[3..];
            var untracked = entry.StartsWith("??", StringComparison.Ordinal);
            if (incoming.Contains(path) || (rebasing && !untracked))
                blocked.Add(path);
            // A rename or copy is followed by its old path.
            if (entry[0] is 'R' or 'C')
                i++;
        }
        return blocked;
    }

    /// <summary>
    /// Rebases this machine's commits onto <paramref name="tracking"/>. On a conflict the rebase is undone and the files in
    /// conflict are returned; <c>null</c> when it went through. A rebase that failed without stopping is a git failure.
    /// </summary>
    private async Task<string?> RebaseAsync(string tracking, CancellationToken cancellationToken)
    {
        var (code, _, error) = await _git.TryRunAsync(cancellationToken, "-c", "rebase.autoStash=false", "rebase", "--quiet", tracking);
        if (code == 0)
            return null;
        if (!await RebaseInProgressAsync(cancellationToken))
            throw new TacitException(TacitError.GitFailed, Messages.GitFailed.Format(Language, "rebase", error.Trim()));
        var conflicted = (await _git.RunAsync(cancellationToken, "diff", "--name-only", "--diff-filter=U"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        await _git.RunAsync(cancellationToken, "rebase", "--abort");
        return string.Join(", ", conflicted);
    }

    private Task PushAsync(string branch, CancellationToken cancellationToken) =>
        _git.RunRemoteAsync(cancellationToken, "push", "--quiet", RemoteName, $"HEAD:refs/heads/{branch}");

    private static string TrackingRef(string branch) => $"refs/remotes/{RemoteName}/{branch}";

    private static async Task<bool> RefExistsAsync(GitCli git, string reference, CancellationToken cancellationToken) =>
        (await git.TryRunAsync(cancellationToken, "rev-parse", "--verify", "--quiet", reference)).ExitCode == 0;

    private static int Count(string output) => int.Parse(output.Trim(), CultureInfo.InvariantCulture);

    private SyncRecord Record(SyncOutcome outcome, string? detail) => new(options.Time.GetUtcNow(), outcome, detail);

    private SyncResult Result(SyncRecord record, string remote, int pulledCount, int pushedCount) => new(
        true,
        record,
        record.Outcome switch
        {
            SyncOutcome.Synced => SyncMessages.Synced.Format(Language, remote, pulledCount, pushedCount),
            SyncOutcome.Failed => SyncMessages.Failed.Format(Language, record.Detail),
            SyncOutcome.HandEdits => SyncMessages.HandEdits.Format(Language, record.Detail),
            SyncOutcome.Conflict => SyncMessages.Conflict.Format(Language, record.Detail),
            SyncOutcome.Unrelated => SyncMessages.Unrelated.Format(Language, remote),
            _ => SyncMessages.RestorePaused.In(Language),
        });

    /// <summary>Writes the record. One that cannot be written is logged: the sync's own result stands.</summary>
    private void Save(SyncRecord record)
    {
        try
        {
            var path = StatePath(options);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(record, StateJson.For<SyncRecord>()));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Record(LogLevel.Warning, LogMessages.SyncRecordFailed, e.Message);
        }
    }

    internal static string TriggerName(SyncTrigger trigger) => trigger switch
    {
        SyncTrigger.Manual => "manual",
        SyncTrigger.BeforeWrite => "before-write",
        SyncTrigger.AfterWrite => "after-write",
        SyncTrigger.Timer => "timer",
        _ => "report",
    };

    private static string StatePath(TacitOptions options) => Path.Combine(options.TacitHome, StateFileName);
}

/// <summary>Where the two sides stand after a fetch, for choosing one.</summary>
/// <param name="RemoteHead">The commit of the remote's branch; <c>null</c> when the remote has no such branch.</param>
/// <param name="LocalOnly">Commits only this machine has.</param>
/// <param name="RemoteOnly">Commits only the remote has.</param>
internal sealed record SidePosition(string Remote, string Branch, string? RemoteHead, int LocalOnly, int RemoteOnly);
