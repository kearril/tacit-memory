using System.Globalization;
using System.Text.RegularExpressions;
using TacitMemory.Core.Consolidation;

namespace TacitMemory.Core.Formats;

/// <summary>A memory file as the memory page shows it: its path and the problems consolidation reports for it.</summary>
internal static partial class MemoryFileReport
{
    /// <summary>The file <paramref name="relative"/> to the repository; a missing file has no problem.</summary>
    public static MemoryFileInfo Of(TacitConfig config, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(config.MemoryPath, relative));
        var problems = File.Exists(path) ? FormatViolations.FileIssues(config, relative).Select(Problem).ToList() : [];
        return new MemoryFileInfo(path, problems);
    }

    private static FileProblem Problem(FormatIssue issue) =>
        new(LineLocation().Match(issue.Location) is { Success: true } match ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null,
            issue.Location, issue.Kind, issue.Description);

    [GeneratedRegex(@"^line (\d+)$")]
    private static partial Regex LineLocation();
}
