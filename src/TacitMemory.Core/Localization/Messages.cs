namespace TacitMemory.Core.Localization;

/// <summary>Short human-facing messages: CLI output and refusals.</summary>
internal static class Messages
{
    public static readonly Localized InitRepositoryCreated = new("已创建记忆仓库：{0}", "Created the memory repository: {0}");
    public static readonly Localized InitRepositoryExists = new("记忆仓库已存在：{0}", "The memory repository already exists: {0}");
    public static readonly Localized InitCreated = new("已创建：{0}", "Created: {0}");
    public static readonly Localized InitExisting = new("已存在，未改动：{0}", "Already exists, left unchanged: {0}");
    public static readonly Localized InitSkeletonNotCommitted = new(
        "补上的骨架文件未提交，请在记忆仓库中自行提交。",
        "The added skeleton files are not committed; commit them in the memory repository yourself.");
    public static readonly Localized InitNextSteps = new(
        "下一步：在 {0} 中填写 models 一节。",
        "Next: fill in the models section of {0}.");

    public static readonly Localized MemoryDirectoryNotEmpty = new(
        "目录 {0} 不为空且不是 Git 仓库，不能用作记忆仓库。请指定空目录或已有的记忆仓库（--memory）。",
        "The directory {0} is not empty and is not a Git repository, so it cannot be the memory repository. Choose an empty directory or an existing memory repository (--memory).");
    public static readonly Localized ConflictsWithConfig = new(
        "{0} 已将 {1} 设为 {2}，与传入的 {3} 不同。请先修改配置，或不传该选项。",
        "{0} already sets {1} to {2}, which differs from the given {3}. Edit the config first, or leave the option out.");
    public static readonly Localized GitFailed = new("git {0} 执行失败：{1}", "git {0} failed: {1}");
}
