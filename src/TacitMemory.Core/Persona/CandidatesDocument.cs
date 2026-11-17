using System.Globalization;
using System.Text.RegularExpressions;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Persona;

/// <summary>One dated line under a candidate: a day the user said it, and whether that was the day it was rejected.</summary>
internal sealed record CandidateOccurrence(DateOnly Day, string Quote, bool Rejected);

/// <summary>
/// A candidate, or a rejected one: its text (the heading, the entry as it would be written into the persona), the
/// section it belongs to, its occurrence records in file order, and the scope it goes into.
/// </summary>
internal sealed record PersonaCandidate(string Text, PersonaSection Section, IReadOnlyList<CandidateOccurrence> Occurrences, PersonaScope Scope);

/// <summary>
/// <c>candidates.md</c> parsed leniently. Under its two <c>## </c> sections, in any language, each <c>### </c> heading
/// starts a candidate, followed by a <c>Belongs to: </c> line naming its section, an optional <c>Scope: </c> line naming its
/// scope (global when absent), and dated occurrence lines. A block without a recognized section, and every line that is
/// not a recognized occurrence, is left out; nothing is reported as an error.
/// </summary>
internal sealed record CandidatesDocument(IReadOnlyList<PersonaCandidate> Candidates, IReadOnlyList<PersonaCandidate> RejectionList)
{
    private static readonly Regex Occurrence = new(
        $@"^- (?<day>\d{{4}}-\d{{2}}-\d{{2}}) (?:(?<rejected>{StructureWords.Rejected}) )?{StructureWords.Quote}「(?<quote>.*)」$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static CandidatesDocument Parse(string text)
    {
        var candidates = new List<PersonaCandidate>();
        var rejectionList = new List<PersonaCandidate>();
        List<PersonaCandidate>? list = null;
        string? title = null;
        PersonaSection? belongsTo = null;
        string? scopeText = null;
        var occurrences = new List<CandidateOccurrence>();

        void EndCandidate()
        {
            if (list is not null && title is { Length: > 0 } && belongsTo is { } section)
                list.Add(new PersonaCandidate(title, section, occurrences, ScopeOfText(scopeText)));
            title = null;
            belongsTo = null;
            scopeText = null;
            occurrences = [];
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                EndCandidate();
                var heading = line[3..].Trim();
                list = IsCandidatesHeading(heading) ? candidates
                    : IsRejectionListHeading(heading) ? rejectionList
                    : null;
            }
            else if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                EndCandidate();
                title = line[4..].Trim();
            }
            else if (title is not null)
            {
                belongsTo ??= BelongsToOf(line);
                scopeText ??= ScopeTextOf(line);
                if (OccurrenceOf(line) is { } occurrence)
                    occurrences.Add(occurrence);
            }
        }

        EndCandidate();
        return new CandidatesDocument(candidates, rejectionList);
    }

    private static bool Matches(string heading, string word) => heading.Equals(word, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a <c>## </c> heading, without its marker, is the candidates section.</summary>
    internal static bool IsCandidatesHeading(string heading) => Matches(heading, StructureWords.Candidates);

    /// <summary>Whether a <c>## </c> heading, without its marker, is the rejection list.</summary>
    internal static bool IsRejectionListHeading(string heading) => Matches(heading, StructureWords.RejectionList);

    /// <summary>Whether a line starts with the <c>Belongs to: </c> prefix, whatever follows it.</summary>
    internal static bool IsBelongsToLine(string line) => line.StartsWith(StructureWords.BelongsTo, StringComparison.OrdinalIgnoreCase);

    /// <summary>The section named by a <c>Belongs to: </c> line, or <c>null</c> when the line is not one or names no section.</summary>
    internal static PersonaSection? BelongsToOf(string line)
    {
        return IsBelongsToLine(line) ? PersonaDocument.KindOf(line[StructureWords.BelongsTo.Length..].Trim()) : null;
    }

    /// <summary>The text after a <c>Scope: </c> line's prefix; <c>null</c> when the line is not one.</summary>
    internal static string? ScopeTextOf(string line)
    {
        return line.StartsWith(StructureWords.Scope, StringComparison.OrdinalIgnoreCase) ? line[StructureWords.Scope.Length..].Trim() : null;
    }

    /// <summary>
    /// The scope a candidate's scope line names. A missing line is the global scope, and so is a line that does not name a
    /// scope; the format check reports the latter.
    /// </summary>
    internal static PersonaScope ScopeOfText(string? text) =>
        text is not null && PersonaScope.TryParse(text, out var scope) ? scope : PersonaScope.Global;

    internal static CandidateOccurrence? OccurrenceOf(string line)
    {
        var match = Occurrence.Match(line);
        return match.Success && DateOnly.TryParseExact(match.Groups["day"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? new CandidateOccurrence(day, match.Groups["quote"].Value, match.Groups["rejected"].Success)
            : null;
    }
}
