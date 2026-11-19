using System.Text.Json.Serialization;

namespace TacitMemory.Core;

/// <summary>How a sync of the memory repository with its remote ended.</summary>
public enum SyncOutcome
{
    /// <summary>The repository and the remote hold the same history.</summary>
    Synced,

    /// <summary>The remote could not be reached or refused (network, authentication, a timeout, a rejected push); the next sync tries again.</summary>
    Failed,

    /// <summary>Files with changes nobody has committed stood in the way, so nothing was changed; the next sync tries again.</summary>
    HandEdits,

    /// <summary>Both sides changed the same lines; the local commits are kept and the automatic syncs pause.</summary>
    Conflict,

    /// <summary>The remote's history shares no commit with the repository's; nothing was pushed and the automatic syncs pause.</summary>
    Unrelated,

    /// <summary>A backup was restored; the syncs pause until a side is chosen.</summary>
    RestorePaused,
}

/// <summary>Which side a sync takes as the truth when the two cannot simply be joined.</summary>
public enum SyncSide
{
    /// <summary>This machine's repository: the remote's branch is replaced with it.</summary>
    Local,

    /// <summary>The remote's branch: this machine's repository is reset to it, after a safety backup.</summary>
    Remote,
}

/// <summary>The last sync, as <c>sync.json</c> in <c>TACIT_HOME</c> keeps it, so a pause outlives a restart.</summary>
/// <param name="At">When the sync ended.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="Detail">The reason or the files involved, in the memory language at the time; <c>null</c> after a success.</param>
public sealed record SyncRecord(DateTimeOffset At, SyncOutcome Outcome, string? Detail)
{
    /// <summary>Whether the automatic syncs wait: after a conflict, unrelated histories or a restore.</summary>
    [JsonIgnore]
    public bool Paused => Outcome is SyncOutcome.Conflict or SyncOutcome.Unrelated or SyncOutcome.RestorePaused;
}

/// <summary>The result of a sync made on request.</summary>
/// <param name="Enabled">Whether <c>sync.remote</c> is configured; when it is not, nothing was done.</param>
/// <param name="Record">How the sync ended; <c>null</c> when the sync is off.</param>
/// <param name="Report">What happened and what to do next, in the memory language.</param>
public sealed record SyncResult(bool Enabled, SyncRecord? Record, string Report);

/// <summary>What choosing <paramref name="Side"/> would do, as a fresh fetch sees the two sides; nothing was changed.</summary>
/// <param name="RemoteHead">
/// The commit of the remote's branch the preview saw, <c>null</c> when it has none; choosing the side with it refuses when
/// the branch has moved since.
/// </param>
/// <param name="LocalOnly">Commits only this machine has: choosing the remote drops them from this machine.</param>
/// <param name="RemoteOnly">Commits only the remote has: choosing this machine drops them from the remote's branch.</param>
/// <param name="Report">What the choice replaces, in the memory language.</param>
/// <param name="Prompt">The question to ask before choosing when the user has not passed <c>--yes</c>.</param>
/// <param name="Cancelled">The line to show when the user does not answer <c>y</c>.</param>
public sealed record SyncSidePreview(SyncSide Side, string? RemoteHead, int LocalOnly, int RemoteOnly, string Report, string Prompt, string Cancelled);

/// <summary>A side chosen after its preview.</summary>
/// <param name="RemoteHead">The <see cref="SyncSidePreview.RemoteHead"/> of the preview the choice was made on.</param>
public sealed record SyncSideChoice(SyncSide Side, string? RemoteHead);
