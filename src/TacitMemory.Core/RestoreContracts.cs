namespace TacitMemory.Core;

/// <summary>
/// What a restore would do with a backup, read from its manifest and from the memory repository as it is now. Nothing is
/// changed. <paramref name="Replaces"/> and <paramref name="ReportLines"/> are in the memory language.
/// </summary>
/// <param name="Path">The backup file, as an absolute path.</param>
/// <param name="GeneratedAt">When the backup was made, in UTC; <c>null</c> when an older backup does not record it.</param>
/// <param name="TacitVersion">The tacit version that made the backup; <c>null</c> when an older backup does not record it.</param>
/// <param name="Kind">The backup's kind: <c>auto</c>, <c>manual</c>, <c>pre-restore</c> or <c>remote</c>; <c>null</c> for an older backup.</param>
/// <param name="MemoryHead">The memory repository's commit the backup holds.</param>
/// <param name="MemoryHeadSubject">The subject of that commit; <c>null</c> for an older backup.</param>
/// <param name="FileCount">The number of files the backup holds.</param>
/// <param name="Config">Whether the restore also replaces <c>config.yaml</c> and <c>prompts/</c>.</param>
/// <param name="Replaces">What the restore replaces, one item per line.</param>
/// <param name="ReportLines">The preview for the user, one line each. The lines that name a replaced item are flagged.</param>
/// <param name="Prompt">The question to ask before restoring when the user has not passed <c>--yes</c>.</param>
/// <param name="Cancelled">The line to show when the user does not answer <c>y</c>.</param>
public sealed record RestorePreview(
    string Path,
    DateTimeOffset? GeneratedAt,
    string? TacitVersion,
    string? Kind,
    string MemoryHead,
    string? MemoryHeadSubject,
    int FileCount,
    bool Config,
    IReadOnlyList<string> Replaces,
    IReadOnlyList<PreviewLine> ReportLines,
    string Prompt,
    string Cancelled)
{
    /// <summary>The preview for the user as text: its lines in order, separated by line feeds.</summary>
    public string Report => string.Join('\n', ReportLines.Select(line => line.Text));
}

/// <summary>One line of the restore preview.</summary>
/// <param name="Text">The line, in the memory language.</param>
/// <param name="Replaces">Whether the line names something the restore replaces.</param>
public sealed record PreviewLine(string Text, bool Replaces);

/// <summary>
/// What a restore did. The memory repository holds the backup's content at <paramref name="MemoryHead"/>, and the index was
/// rebuilt from it. <paramref name="SafetyBackup"/> holds the state from before the restore.
/// </summary>
/// <param name="SafetyBackup">The <c>pre-restore-*.zip</c> written before anything was replaced.</param>
/// <param name="MemoryHead">The memory repository's HEAD after the restore.</param>
/// <param name="Reindex">The full index rebuild that followed the restore.</param>
/// <param name="Notices">What the user must act on or know: the restart for <c>config.yaml</c>, a different <c>memory.path</c>, the plaintext secrets notice.</param>
/// <param name="Report">The summary for the user, notices included, in the memory language.</param>
public sealed record RestoreResult(
    string SafetyBackup,
    string MemoryHead,
    ReindexResult Reindex,
    IReadOnlyList<string> Notices,
    string Report);
