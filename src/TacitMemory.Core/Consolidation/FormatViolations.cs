using System.Text.RegularExpressions;
using TacitMemory.Core.Formats;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
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
    /// <summary>The section titles of the memory files, in both languages.</summary>
    private static readonly Localized[] SectionWords =
    [
        StructureWords.RedLines,
        StructureWords.WorkingRules,
        StructureWords.Preferences,
        StructureWords.Profile,
        StructureWords.Candidates,
        StructureWords.RejectionList,
        StructureWords.Leftovers,
        StructureWords.Overturned,
        StructureWords.Summary,
        StructureWords.Domain,
        StructureWords.Workstream,
        StructureWords.Trigger,
        StructureWords.Scope,
        StructureWords.Lesson,
    ];

    /// <summary>The quote mark of a source comment or an occurrence line, with the rejected mark before it on an occurrence.</summary>
    private static readonly Regex QuoteMark = new(
        @"(?:(?<rejected>拒绝|rejected) )?(?<quote>原话|quote)「", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex InsteadWord = new(@"\binstead\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Every problem of every memory file, each with its file's path. The report splits them into the two parts.</summary>
    public static IReadOnlyList<FormatIssue> Issues(ConsolidationContext context)
    {
        var issues = new List<FormatIssue>();
        foreach (var relative in MemoryFiles(context.Config.MemoryPath))
            issues.AddRange(FileIssues(context, relative).Select(issue => issue with { File = relative }));
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
        AddFiles(files, memoryPath, PageId.Folder, SearchOption.AllDirectories);
        AddFiles(files, memoryPath, RecentFile.Folder, SearchOption.TopDirectoryOnly);
        foreach (var file in new[] { PersonaFiles.Persona, PersonaFiles.Candidates, WorkstreamFile.RelativePath })
            if (File.Exists(Path.Combine(memoryPath, file)))
                files.Add(file);
        return files.Order(StringComparer.Ordinal);
    }

    private static void AddFiles(List<string> files, string memoryPath, string folder, SearchOption search)
    {
        var directory = Path.Combine(memoryPath, folder);
        if (!Directory.Exists(directory))
            return;
        files.AddRange(Directory.EnumerateFiles(directory, "*.md", search)
            .Select(file => Path.GetRelativePath(memoryPath, file).Replace('\\', '/')));
    }

    /// <summary>The issues of one file, with an empty <see cref="FormatIssue.File"/>.</summary>
    private static List<FormatIssue> FileIssues(ConsolidationContext context, string relative)
    {
        var read = TextFile.Read(Path.Combine(context.Config.MemoryPath, relative));
        if (read is not { } text)
            return [new FormatIssue("", "file", "encoding", "The file is not valid UTF-8, so the service skips it. Re-save it as UTF-8 without changing its text.", FormatHandling.FixDirectly)];

        var issues = new List<FormatIssue>(MixedStructureWords(text.Text));
        if (relative.StartsWith($"{PageId.Folder}/", StringComparison.Ordinal))
            issues.AddRange(PageFormatCheck.Check(context.Config.MemoryPath, relative, text.Text, context.Config.Limits.Page));
        if (relative == PersonaFiles.Persona)
            issues.AddRange(PersonaFormatCheck.Check(text.Text, context.Config));
        if (relative == PersonaFiles.Candidates)
            issues.AddRange(CandidatesFormatCheck.Check(text.Text));
        if (relative == WorkstreamFile.RelativePath)
            issues.AddRange(WorkstreamFile.Parse(text.Text).Problems);
        if (relative.StartsWith(RecentFile.Folder + "/", StringComparison.Ordinal))
            issues.AddRange(RecentFormatCheck.Check(text.Text, relative));
        return issues;
    }

    /// <summary>
    /// The structure words of a file, read in both memory languages: when the file uses the words of both, one hint says
    /// so. Hint only, because the service reads either form.
    /// </summary>
    private static IEnumerable<FormatIssue> MixedStructureWords(string text)
    {
        (string Word, int Line)? zh = null;
        (string Word, int Line)? en = null;
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            foreach (var (word, language) in StructureWordsOn(lines[i]))
            {
                if (language == MemoryLanguage.Zh)
                    zh ??= (word, i + 1);
                else
                    en ??= (word, i + 1);
            }
        }

        if (zh is not { } chinese || en is not { } english)
            yield break;
        var first = chinese.Line < english.Line ? chinese : english;
        var second = chinese.Line < english.Line ? english : chinese;
        yield return new FormatIssue("", $"line {second.Line}", "structure words",
            $"Structure words in both languages: \"{first.Word}\" on line {first.Line} and \"{second.Word}\" on line {second.Line}.",
            FormatHandling.HintOnly);
    }

    /// <summary>
    /// The structure words a line uses, each with its language: a section heading, a "belongs to" line, the quote mark of
    /// a source or an occurrence (with the rejected mark before it), and "instead" in a list item (a red line's alternative).
    /// </summary>
    private static IEnumerable<(string Word, MemoryLanguage Language)> StructureWordsOn(string line)
    {
        if (SectionHeadingText(line) is { } heading && SectionWordOf(heading) is { } section)
            yield return section;

        if (line.StartsWith(StructureWords.BelongsTo.Zh.Trim(), StringComparison.Ordinal))
            yield return (StructureWords.BelongsTo.Zh.Trim(), MemoryLanguage.Zh);
        if (line.StartsWith(StructureWords.BelongsTo.En.Trim(), StringComparison.Ordinal))
            yield return (StructureWords.BelongsTo.En.Trim(), MemoryLanguage.En);

        foreach (Match mark in QuoteMark.Matches(line))
        {
            if (mark.Groups["rejected"] is { Success: true } rejected)
                yield return WordAs(StructureWords.Rejected, rejected.Value);
            yield return WordAs(StructureWords.Quote, mark.Groups["quote"].Value);
        }

        if (line.TrimStart().StartsWith("- ", StringComparison.Ordinal))
        {
            if (line.Contains(StructureWords.Instead.Zh, StringComparison.Ordinal))
                yield return (StructureWords.Instead.Zh, MemoryLanguage.Zh);
            if (InsteadWord.IsMatch(line))
                yield return (StructureWords.Instead.En, MemoryLanguage.En);
        }
    }

    /// <summary>The word as it is written in <paramref name="matched"/>, which is one of its two forms.</summary>
    private static (string Word, MemoryLanguage Language) WordAs(Localized word, string matched) =>
        matched == word.Zh ? (word.Zh, MemoryLanguage.Zh) : (word.En, MemoryLanguage.En);

    /// <summary>
    /// The section word a heading names, in either language. The words are compared trimmed: an English word written
    /// with a colon and a space (<c>Scope: </c>) names the heading <c>## Scope:</c>.
    /// </summary>
    private static (string Word, MemoryLanguage Language)? SectionWordOf(string heading)
    {
        foreach (var word in SectionWords)
        {
            if (heading == word.Zh.Trim())
                return (word.Zh.Trim(), MemoryLanguage.Zh);
            if (string.Equals(heading, word.En.Trim(), StringComparison.OrdinalIgnoreCase))
                return (word.En.Trim(), MemoryLanguage.En);
        }

        return null;
    }

    private static string? SectionHeadingText(string line) =>
        line.StartsWith("## ", StringComparison.Ordinal) ? line[3..].Trim() : null;
}
