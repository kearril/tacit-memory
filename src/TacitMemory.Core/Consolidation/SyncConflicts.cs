using TacitMemory.Core.Sync;

namespace TacitMemory.Core.Consolidation;

/// <summary>
/// Part A's sync category: while the sync of the memory repository is paused, what paused it and how to resume. A conflict
/// comes with the steps that resolve it in the repository; unrelated histories and a restore come with the side to choose,
/// which only the user decides.
/// </summary>
internal static class SyncConflicts
{
    public static async Task<ConsolidationSection> ProduceAsync(ConsolidationContext context, SyncRecord? last, CancellationToken cancellationToken)
    {
        if (context.Config.Sync.Remote is null || last is not { Paused: true })
            return new ConsolidationSection(ConsolidationTitles.SyncConflicts, []);

        var repository = context.Config.MemoryPath;
        var git = $"git -C \"{repository}\"";
        string finding;
        if (last.Outcome == SyncOutcome.Conflict)
        {
            var target = await MemorySync.BranchAsync(context.Git, cancellationToken) ?? "<branch>";
            finding = $"- This machine and the remote both changed {last.Detail}, so the automatic syncs are paused. Resolve it first, before the other steps: "
                + $"run `{git} pull --rebase {MemorySync.RemoteName} {target}`; in each conflicted file keep the facts of both sides and remove the conflict markers; "
                + $"run `{git} add <file>` for each, then `{git} -c core.editor=true rebase --continue` until the rebase ends; then run `tacit sync`, which resumes the automatic syncs. "
                + $"If a conflict cannot be resolved without dropping a fact, run `{git} rebase --abort` and ask the user.";
        }
        else
        {
            var reason = last.Outcome == SyncOutcome.Unrelated
                ? "The remote's history shares no commit with this repository's"
                : "A backup was restored on this machine";
            finding = $"- {reason}, so the sync is paused until the user chooses a side. Do not choose it yourself: tell the user to run "
                + "`tacit sync --use-local` to keep this machine's memory, or `tacit sync --use-remote` to take the remote's.";
        }
        return new ConsolidationSection(ConsolidationTitles.SyncConflicts, [finding]);
    }
}
