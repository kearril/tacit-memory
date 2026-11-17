using TacitMemory.Core.Formats;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Persona;

/// <summary>
/// The format problems of <c>candidates.md</c>: candidate blocks with no section, scope lines that name no scope, and lines
/// inside a block that are neither a section line, a scope line nor an occurrence record. Both the candidates and the
/// rejection list are checked.
/// </summary>
internal static class CandidatesFormatCheck
{
    /// <summary>Finds the problems in <paramref name="text"/>; the file must already be valid UTF-8.</summary>
    public static IReadOnlyList<FormatIssue> Check(string text)
    {
        var issues = new List<FormatIssue>();
        var lines = text.Split('\n');
        var inList = false;
        string? title = null;
        var blockLine = 0;
        var hasSection = false;

        void EndBlock()
        {
            if (title is not null && !hasSection)
                issues.Add(new FormatIssue("", $"line {blockLine + 1}", "Candidate without section",
                    $"\"{title}\" has no \"{StructureWords.BelongsTo}:\" line naming its section, so it is not read.", FormatHandling.FixDirectly));
            title = null;
            hasSection = false;
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                EndBlock();
                var heading = line[3..].Trim();
                inList = CandidatesDocument.IsCandidatesHeading(heading) || CandidatesDocument.IsRejectionListHeading(heading);
            }
            else if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                EndBlock();
                var name = line[4..].Trim();
                if (inList && name.Length > 0)
                {
                    title = name;
                    blockLine = index;
                }
            }
            else if (title is not null && line.Trim().Length > 0)
            {
                if (CandidatesDocument.BelongsToOf(line) is not null)
                    hasSection = true;
                else if (CandidatesDocument.ScopeTextOf(line) is { } scopeText)
                {
                    if (!PersonaScope.TryParse(scopeText, out _))
                        issues.Add(new FormatIssue("", $"line {index + 1}", "Invalid scope",
                            $"\"{scopeText}\" is not a scope: it must be global, workstream:<name> or domain:<label>, and the name uses only "
                            + "lowercase letters, digits, '.', '_' and '-'. Without a valid scope the candidate is read as global.", FormatHandling.FixDirectly));
                }
                else if (!CandidatesDocument.IsBelongsToLine(line) && CandidatesDocument.OccurrenceOf(line) is null)
                    issues.Add(UnrecognizedLine(index, line));
                else if (CandidatesDocument.IsBelongsToLine(line) && CandidatesDocument.BelongsToOf(line) is null)
                    issues.Add(UnrecognizedLine(index, line));
            }
        }

        EndBlock();
        return issues;
    }

    private static FormatIssue UnrecognizedLine(int index, string line) =>
        new("", $"line {index + 1}", "Unrecognized occurrence line",
            $"\"{line.Trim()}\" is neither a \"{StructureWords.BelongsTo}:\" line nor a dated occurrence, so it is not read.", FormatHandling.FixDirectly);
}
