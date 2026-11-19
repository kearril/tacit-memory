namespace TacitMemory.Core.Localization;

/// <summary>Human-facing text of the sync of the memory repository: the results, the status lines and the reasons.</summary>
internal static class SyncMessages
{
    public static readonly Localized Off = new(
        "未开启同步：config.yaml 中没有 sync.remote。",
        "Sync is off: config.yaml has no sync.remote.");

    public static readonly Localized Synced = new(
        "已与 {0} 同步：拉取 {1} 个提交，推送 {2} 个提交。",
        "Synced with {0}: {1} commit(s) pulled, {2} pushed.");

    public static readonly Localized Failed = new(
        "同步失败：{0}。写入照常在本机提交，下次同步时再试。",
        "The sync failed: {0}. Writes are still committed on this machine; the next sync tries again.");

    public static readonly Localized HandEdits = new(
        "同步暂缓：这些文件有未提交的修改，同步没有改动它们：{0}。提交或撤销这些修改后，下次同步时再试。",
        "The sync waits: these files have changes nobody has committed, and the sync left them alone: {0}. Once they are committed or undone, the next sync tries again.");

    public static readonly Localized Conflict = new(
        "同步冲突：本机与远端都改了 {0}。本机的提交保留，自动同步已暂停，写入照常在本机提交。在记忆仓库里解决冲突后运行 tacit sync 恢复；整理报告列出了解决步骤。",
        "Sync conflict: this machine and the remote both changed {0}. The local commits are kept, the automatic syncs pause, and writes are still committed on this machine. Resolve the conflict in the memory repository, then run tacit sync to resume; the consolidation report lists the steps.");

    public static readonly Localized Unrelated = new(
        "远端 {0} 的历史与本机记忆仓库没有共同的提交，同步既没有拉取也没有推送，自动同步已暂停。确认以哪边为准后运行 tacit sync --use-local（以本机为准）或 tacit sync --use-remote（以远端为准）。",
        "The history at {0} shares no commit with this machine's memory repository, so nothing was pulled or pushed, and the automatic syncs pause. Decide which side holds the truth, then run tacit sync --use-local (this machine) or tacit sync --use-remote (the remote).");

    public static readonly Localized RestorePaused = new(
        "恢复备份后同步已暂停，以免恢复的内容冲掉另一台机器写下的记忆，或被它冲掉。确认以哪边为准后运行 tacit sync --use-local（以本机为准）或 tacit sync --use-remote（以远端为准）。",
        "The sync is paused after a restore, so the restored memory neither overwrites what another machine wrote nor is overwritten by it. Decide which side holds the truth, then run tacit sync --use-local (this machine) or tacit sync --use-remote (the remote).");

    public static readonly Localized RebaseInProgress = new(
        "记忆仓库里有一次没有完成的 rebase，先完成（git rebase --continue）或放弃（git rebase --abort）它",
        "a rebase in the memory repository is not finished; finish it (git rebase --continue) or drop it (git rebase --abort) first");

    public static readonly Localized NoBranch = new(
        "记忆仓库当前不在任何分支上，先切回分支",
        "the memory repository is on no branch; check a branch out first");

    public static readonly Localized Title = new("同步", "Sync");

    public static readonly Localized StatusOff = new("未开启（config.yaml 中没有 sync.remote）", "Off (config.yaml has no sync.remote)");

    public static readonly Localized StatusRemote = new("远端：{0}", "Remote: {0}");

    public static readonly Localized OffRefused = new(
        "未开启同步，无从选边：config.yaml 中没有 sync.remote。",
        "Sync is off, so there is no side to choose: config.yaml has no sync.remote.");

    public static readonly Localized RemoteEmpty = new(
        "远端 {0} 上还没有分支 {1}，无法以远端为准；用 tacit sync 或 --use-local 把本机的历史推上去。",
        "The remote {0} has no branch {1} yet, so it cannot be taken as the truth; push this machine's history with tacit sync or --use-local.");

    public static readonly Localized RemoteMoved = new(
        "预览之后，远端 {0} 的分支 {1} 又有了新的提交，什么都没有改动。重新运行这条命令，看过新的预览再确认。",
        "The branch {1} at {0} has moved since the preview, so nothing was changed. Run the command again and confirm the new preview.");

    public static readonly Localized PreviewLocal = new(
        "以本机为准：远端 {0} 的分支 {1} 换成本机的历史。只在远端的 {2} 个提交会从这个分支上去掉；本机的文件不变。",
        "This machine as the truth: the branch {1} at {0} is replaced with this machine's history. The {2} commit(s) only the remote has leave that branch; the files on this machine stay as they are.");

    public static readonly Localized PreviewRemote = new(
        "以远端为准：先写一份手动备份，再把本机的记忆仓库重置到远端 {0} 的分支 {1}，然后重建索引。只在本机的 {2} 个提交与未提交的修改会被丢弃，只能从这份备份里找回。",
        "The remote as the truth: a manual backup is written first, then this machine's memory repository is reset to the branch {1} at {0}, and the index is rebuilt. The {2} commit(s) only this machine has, and any changes not committed, are dropped; only that backup keeps them.");

    public static readonly Localized SidePrompt = new("确认继续？输入 y 确认：", "Go ahead? Type y to confirm:");

    public static readonly Localized SideCancelled = new("已取消，什么都没有改动。", "Cancelled; nothing was changed.");

    public static readonly Localized UsedLocal = new(
        "已以本机为准：远端 {0} 的分支 {1} 现在与本机相同，同步已恢复。",
        "This machine was taken as the truth: the branch {1} at {0} now matches this machine, and the sync resumes.");

    public static readonly Localized UsedRemote = new(
        "已以远端为准：本机已重置到远端 {0} 的分支 {1}，同步已恢复。重置前的手动备份：{2}",
        "The remote was taken as the truth: this machine was reset to the branch {1} at {0}, and the sync resumes. The manual backup from before the reset: {2}");

    public static readonly Localized StatusNever = new("上次同步：还没有", "Last sync: none yet");

    public static readonly Localized StatusLast = new("上次同步：{0:yyyy-MM-dd HH:mm}，{1}", "Last sync: {0:yyyy-MM-dd HH:mm}, {1}");

    public static readonly Localized StatusUnreadable = new("上次同步：记录无法读取：{0}", "Last sync: the record cannot be read: {0}");

    public static readonly Localized StatusUnpushed = new("未推送的提交：{0}", "Commits not pushed: {0}");

    public static readonly Localized StatusUnpushedUnknown = new("未推送的提交：无法读取：{0}", "Commits not pushed: cannot be read: {0}");

    public static readonly Localized OutcomeSynced = new("成功", "succeeded");

    public static readonly Localized OutcomeFailed = new("失败：{0}", "failed: {0}");

    public static readonly Localized OutcomeHandEdits = new("被未提交的修改挡住：{0}", "held back by changes not committed: {0}");

    public static readonly Localized OutcomeConflict = new("冲突，自动同步已暂停：{0}", "conflict, automatic syncs paused: {0}");

    public static readonly Localized OutcomeUnrelated = new("远端历史不相关，自动同步已暂停", "the remote's history is unrelated, automatic syncs paused");

    public static readonly Localized OutcomeRestorePaused = new("恢复备份后已暂停，等待选边", "paused after a restore, waiting for a side to be chosen");

    public static readonly Localized StatusChooseSide = new(
        "选边：tacit sync --use-local（以本机为准）或 tacit sync --use-remote（以远端为准）",
        "Choose a side: tacit sync --use-local (this machine) or tacit sync --use-remote (the remote)");

    public static readonly Localized StatusResolve = new(
        "在记忆仓库里解决冲突后运行 tacit sync；也可以选边",
        "Resolve the conflict in the memory repository, then run tacit sync; or choose a side");
}
