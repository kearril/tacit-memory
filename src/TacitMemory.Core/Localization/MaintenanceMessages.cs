namespace TacitMemory.Core.Localization;

/// <summary>Human-facing text of a maintenance operation's result.</summary>
internal static class MaintenanceMessages
{
    public static readonly Localized Reindexed = new(
        "索引已重建：文件 {0}，单元 {1}，已有向量的单元 {2}，耗时 {3:0.0} 秒。",
        "Index rebuilt: {0} files, {1} units, {2} units with a vector, in {3:0.0} s.");

    public static readonly Localized SkippedFiles = new(
        "跳过的非 UTF-8 文件（不可检索）：",
        "Files skipped because they are not UTF-8 (not searchable):");

    public static readonly Localized VectorsPending = new(
        "嵌入模型不可用：向量将在模型恢复后补齐，在此之前只能按全文检索。",
        "The embedding model is unavailable: vectors will be filled in once it is back; until then search is by full text only.");
}
