using System.Globalization;
using System.Text.RegularExpressions;
using TacitMemory.Core.Checkup;

namespace TacitMemory.Core.Recent;

/// <summary>
/// The format problems of a month file of the activity log: entries outside a date heading, headings that are not
/// dates, and dates that look like dates but are not in <c>yyyy-MM-dd</c>.
/// </summary>
internal static partial class RecentFormatCheck
{
    /// <summary>Finds the problems in <paramref name="text"/>; the file must already be valid UTF-8.</summary>
    public static IReadOnlyList<FormatIssue> Check(string text)
    {
        var issues = new List<FormatIssue>();
        var lines = text.Split('\n');
        var underDate = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var heading = line[3..];
                underDate = DateOnly.TryParseExact(heading, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
                if (underDate)
                    continue;

                issues.Add(LooksLikeDate().IsMatch(heading.Trim())
                    ? new FormatIssue("", $"line {index + 1}", "Date format error",
                        $"\"{heading.Trim()}\" is not a date in yyyy-MM-dd, so its entries are not read.", FormatHandling.FixDirectly)
                    : new FormatIssue("", $"line {index + 1}", "Unrecognized date heading",
                        $"\"{heading.Trim()}\" is not a date heading, so its entries are not read.", FormatHandling.FixDirectly));
            }
            else if (!underDate && line.StartsWith("- ", StringComparison.Ordinal) && line[2..].Trim().Length > 0)
            {
                issues.Add(new FormatIssue("", $"line {index + 1}", "Entry outside a date heading",
                    "This activity is not under a yyyy-MM-dd heading, so it is not read.", FormatHandling.FixDirectly));
            }
        }

        return issues;
    }

    [GeneratedRegex(@"^\d{4}[-/.]\d{1,2}[-/.]\d{1,2}$")]
    private static partial Regex LooksLikeDate();
}
