using TacitMemory.Core.Consolidation;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Pages;

namespace TacitMemory.Core.Workstreams;

/// <summary>An established workstream: its name, its background line, and its recent line when the file has one.</summary>
/// <param name="Name">The workstream name, as the file writes it.</param>
/// <param name="Background">What the workstream is; always present.</param>
/// <param name="Recent">The summary of its recent phase, or <c>null</c> when the file gives none.</param>
internal sealed record WorkstreamEntry(string Name, string Background, string? Recent);

/// <summary>
/// The result of reading <c>workstreams.md</c>: the established workstreams, the format problems of the file, and every name
/// the file writes under a workstream heading, established or not, in file order.
/// </summary>
internal sealed record WorkstreamParse(IReadOnlyList<WorkstreamEntry> Entries, IReadOnlyList<FormatIssue> Problems, IReadOnlyList<string> Names);

/// <summary>
/// <c>workstreams.md</c>, the file that decides which workstreams are established. Under one <c>## 主线</c> heading (or
/// <c>## Workstream</c>), each workstream has a <c>### name</c> heading, a <c>背景：</c> (<c>Background:</c>) line, and an
/// optional <c>近况：</c> (<c>Recent:</c>) line. A name with a complete entry is established; a name that only appears in a
/// page folder or an activity label is not, and the service leaves it alone. Only the user establishes a workstream, by
/// having the consolidation write its entry.
/// </summary>
internal static class WorkstreamFile
{
    /// <summary>The file, relative to the memory repository, that lists the established workstreams.</summary>
    public const string RelativePath = "workstreams.md";

    /// <summary>The file as the repository holds it now; an empty parse when the file is missing.</summary>
    public static WorkstreamParse Read(string memoryPath) =>
        Parse(TextFile.Read(Path.Combine(memoryPath, RelativePath))?.Text ?? "");

    /// <summary>The established workstreams in file order; empty when the file is missing.</summary>
    public static IReadOnlyList<WorkstreamEntry> Established(string memoryPath) => Read(memoryPath).Entries;

    /// <summary>
    /// Reads the file text. An entry is established when its name is valid, appears once in the file, and has a
    /// background line. Every other entry, and every line the reader does not recognize, is a format problem.
    /// </summary>
    public static WorkstreamParse Parse(string text)
    {
        var entries = new List<Candidate>();
        var problems = new List<FormatIssue>();
        Candidate? current = null;
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            var at = $"line {index + 1}";
            if (line.Length == 0)
                continue;

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                current = null;
                if (!IsSectionHeading(line[3..].Trim()))
                    problems.Add(Unrecognized(at, line));
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                current = new Candidate(line[4..].Trim(), index + 1);
                entries.Add(current);
                continue;
            }

            if (current is not null && TryField(line, StructureWords.Background, out var background) && current.Background is null)
            {
                current.Background = background;
                continue;
            }

            if (current is not null && TryField(line, StructureWords.Recent, out var recent) && current.Recent is null)
            {
                current.Recent = recent;
                continue;
            }

            problems.Add(Unrecognized(at, line));
        }

        var counts = entries.GroupBy(entry => entry.Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var established = new List<WorkstreamEntry>();
        foreach (var entry in entries)
        {
            var at = $"line {entry.Line}";
            var nameValid = PageId.IsWorkstreamName(entry.Name);
            if (!nameValid)
                problems.Add(new FormatIssue("", at, "Workstream name",
                    $"The workstream name \"{entry.Name}\" is not valid: use a-z, 0-9, '.', '_' and '-', and do not start with '.' or '_'.", FormatHandling.FixDirectly));
            if (counts[entry.Name] > 1)
                problems.Add(new FormatIssue("", at, "Duplicate workstream",
                    $"The workstream name \"{entry.Name}\" has {counts[entry.Name]} entries; keep one.", FormatHandling.FixDirectly));
            if (string.IsNullOrWhiteSpace(entry.Background))
                problems.Add(new FormatIssue("", at, "Missing background",
                    $"The workstream \"{entry.Name}\" has no background line (\"{StructureWords.Background.Zh}：\" or \"{StructureWords.Background.En}:\").", FormatHandling.FixDirectly));

            if (nameValid && counts[entry.Name] == 1 && !string.IsNullOrWhiteSpace(entry.Background))
                established.Add(new WorkstreamEntry(entry.Name, entry.Background!, string.IsNullOrWhiteSpace(entry.Recent) ? null : entry.Recent));
        }

        return new WorkstreamParse(established, problems, entries.Select(entry => entry.Name).ToList());
    }

    private static bool IsSectionHeading(string heading) =>
        heading == StructureWords.Workstream.Zh || string.Equals(heading, StructureWords.Workstream.En, StringComparison.OrdinalIgnoreCase);

    /// <summary>A field line: the label, then a colon in either form, then the value. Labels match in either language.</summary>
    private static bool TryField(string line, Localized label, out string value)
    {
        foreach (var form in new[] { label.Zh, label.En })
        {
            if (!line.StartsWith(form, StringComparison.OrdinalIgnoreCase) || line.Length == form.Length)
                continue;
            var separator = line[form.Length];
            if (separator is '：' or ':')
            {
                value = line[(form.Length + 1)..].Trim();
                return true;
            }
        }

        value = "";
        return false;
    }

    private static FormatIssue Unrecognized(string at, string line) =>
        new("", at, "Unrecognized line", $"The line \"{line}\" is neither a workstream heading, a workstream name, nor a background or recent line.", FormatHandling.FixDirectly);

    /// <summary>An entry as the file gives it, before the checks decide whether it is established.</summary>
    private sealed class Candidate(string name, int line)
    {
        public string Name { get; } = name;

        public int Line { get; } = line;

        public string? Background { get; set; }

        public string? Recent { get; set; }
    }
}
