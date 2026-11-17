using TacitMemory.Core.Formats;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Pages;
using TacitMemory.Core.Persona;
using TacitMemory.Core.Recent;
using TacitMemory.Core.Workstreams;

namespace TacitMemory.Core.Consolidation;

/// <summary>
/// The "Format and rule violations" category. Every memory file is read again each time the report is built, so a
/// problem the user has just fixed is gone from the next report. Nothing is recorded or cached.
/// </summary>
internal static class FormatViolations
{
    /// <summary>Every problem of every memory file, each with its file's path. The report splits them into the two parts.</summary>
    public static IReadOnlyList<FormatIssue> Issues(ConsolidationContext context)
    {
        var issues = new List<FormatIssue>();
        foreach (var relative in MemoryFiles(context.Config.MemoryPath))
            issues.AddRange(FileIssues(context.Config, relative).Select(issue => issue with { File = relative }));
        return issues;
    }

    /// <summary>Part A: the problems the agent fixes directly, or only hints at, without asking the user.</summary>
    public static ConsolidationSection Automatic(IReadOnlyList<FormatIssue> issues) =>
        new(ConsolidationTitles.FormatViolations, Findings(issues, handling => handling != FormatHandling.AskUser));

    private static List<string> Findings(IReadOnlyList<FormatIssue> issues, Func<FormatHandling, bool> include) =>
        issues.Where(issue => include(issue.Handling)).Select(issue => issue.ToFinding()).ToList();

    /// <summary>
    /// The memory files the service reads, relative to the repository with forward slashes, in ordinal order: the detail
    /// pages, the persona, the candidates, the workstreams file and the month files of the activity log.
    /// </summary>
    private static IEnumerable<string> MemoryFiles(string memoryPath)
    {
        var files = new List<string>();
        AddFiles(files, memoryPath, MemoryLayout.Pages, SearchOption.AllDirectories);
        AddFiles(files, memoryPath, MemoryLayout.Recent, SearchOption.TopDirectoryOnly);
        foreach (var file in new[] { MemoryLayout.Persona, MemoryLayout.Candidates, MemoryLayout.Workstreams })
            if (File.Exists(MemoryLayout.Full(memoryPath, file)))
                files.Add(file);
        return files.Order(StringComparer.Ordinal);
    }

    private static void AddFiles(List<string> files, string memoryPath, string folder, SearchOption search)
    {
        var directory = MemoryLayout.Full(memoryPath, folder);
        if (!Directory.Exists(directory))
            return;
        files.AddRange(Directory.EnumerateFiles(directory, "*.md", search)
            .Select(file => Path.GetRelativePath(memoryPath, file).Replace('\\', '/')));
    }

    /// <summary>The issues of one file, <paramref name="relative"/> to the repository, with an empty <see cref="FormatIssue.File"/>.</summary>
    internal static List<FormatIssue> FileIssues(TacitConfig config, string relative)
    {
        var read = TextFile.Read(MemoryLayout.Full(config.MemoryPath, relative));
        if (read is not { } text)
            return [new FormatIssue("", "file", "encoding", "The file is not valid UTF-8, so the service skips it. Re-save it as UTF-8 without changing its text.", FormatHandling.FixDirectly)];

        var issues = new List<FormatIssue>();
        if (MemoryLayout.IsIn(relative, MemoryLayout.Pages))
            issues.AddRange(PageFormatCheck.Check(config.MemoryPath, relative, text.Text, config.Limits.Page));
        if (relative == MemoryLayout.Persona)
            issues.AddRange(PersonaFormatCheck.Check(text.Text, config));
        if (relative == MemoryLayout.Candidates)
            issues.AddRange(CandidatesFormatCheck.Check(text.Text));
        if (relative == MemoryLayout.Workstreams)
            issues.AddRange(WorkstreamFile.Parse(text.Text).Problems);
        if (relative.StartsWith(MemoryLayout.Recent + "/", StringComparison.Ordinal))
            issues.AddRange(RecentFormatCheck.Check(text.Text, relative));
        return issues;
    }
}
