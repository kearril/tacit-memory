using System.Globalization;
using System.Text.RegularExpressions;
using TacitMemory.Core.Consolidation;

namespace TacitMemory.Core.Recent;

/// <summary>
/// The format problems of a month file of the activity log: entries outside a date heading, headings that are not
/// dates, dates that look like dates but are not in <c>yyyy-MM-dd</c>, and lines of the summary section that are not
/// summaries of their file's month.
/// </summary>
internal static partial class RecentFormatCheck
{
    /// <summary>Finds the problems in <paramref name="text"/> of the file at <paramref name="relative"/>; the text must already be valid UTF-8.</summary>
    public static IReadOnlyList<FormatIssue> Check(string text, string relative)
    {
        var fileMonth = FileMonth(relative);
        var issues = new List<FormatIssue>();
        var lines = text.Split('\n');
        var underDate = false;
        var inSummary = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (SummaryLines.IsHeading(line))
                {
                    underDate = false;
                    inSummary = true;
                    continue;
                }

                inSummary = false;
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
            else if (inSummary)
            {
                if (line.StartsWith("- ", StringComparison.Ordinal) && line[2..].Trim().Length > 0 && SummaryIssue(line, index, fileMonth) is { } issue)
                    issues.Add(issue);
            }
            else if (!underDate && line.StartsWith("- ", StringComparison.Ordinal) && line[2..].Trim().Length > 0)
            {
                issues.Add(new FormatIssue("", $"line {index + 1}", "Entry outside a date heading",
                    "This activity is not under a yyyy-MM-dd heading, so it is not read.", FormatHandling.FixDirectly));
            }
        }

        return issues;
    }

    /// <summary>The problem with one line of the summary section, or null when it is a summary of a real period in this file's month.</summary>
    private static FormatIssue? SummaryIssue(string line, int index, DateOnly? fileMonth)
    {
        var location = $"line {index + 1}";
        if (SummaryLines.ShapeOf(line) is not { } shape)
        {
            return new FormatIssue("", location, "Unrecognized summary line",
                "This line is neither a week summary (- 2026-W45：...) nor a month summary (- 2026-11：...), so it is not read.",
                FormatHandling.FixDirectly);
        }

        if (SummaryLines.PeriodOf(shape) is not { } period)
        {
            return shape.Kind == SummaryKind.Week
                ? new FormatIssue("", location, "Week number error",
                    $"Week {shape.Year}-W{shape.Number:00} does not exist, so the summary is not read.", FormatHandling.FixDirectly)
                : new FormatIssue("", location, "Month number error",
                    $"Month {shape.Year}-{shape.Number:00} does not exist, so the summary is not read.", FormatHandling.FixDirectly);
        }

        var expected = shape.Kind == SummaryKind.Week
            ? SummaryLines.FileMonthOfWeek(period.First)
            : period.First;
        if (fileMonth is { } actual && actual != expected)
        {
            return new FormatIssue("", location, "Summary in the wrong file",
                $"{period.Label} belongs in the file of {expected:yyyy-MM}, not {actual:yyyy-MM}.", FormatHandling.FixDirectly);
        }

        return null;
    }

    /// <summary>The first day of the month a month file is for, from its name <c>recent/yyyy-MM.md</c>; null when the name is not one.</summary>
    private static DateOnly? FileMonth(string relative)
    {
        var stem = Path.GetFileNameWithoutExtension(relative);
        return DateOnly.TryParseExact(stem + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month)
            ? month
            : null;
    }

    [GeneratedRegex(@"^\d{4}[-/.]\d{1,2}[-/.]\d{1,2}$")]
    private static partial Regex LooksLikeDate();
}
