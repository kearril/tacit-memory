using System.Globalization;
using TacitMemory.Core.Git;
using TacitMemory.Core.Recent;

namespace TacitMemory.Core.Checkup;

/// <summary>
/// The mainline in <c>recent.md</c> when its last commit is more than <c>checkup.mainlineStaleDays</c> days ago. A
/// file that was never committed is not stale.
/// </summary>
internal static class StaleMainline
{
    public static async Task<CheckupSection> ProduceAsync(CheckupContext context, CancellationToken cancellationToken)
    {
        var committed = await LastCommitDayAsync(context.Git, cancellationToken);
        if (committed is not { } day)
            return new CheckupSection(CheckupTitles.StaleMainline, []);

        var staleAfter = context.Config.Checkup.MainlineStaleDays;
        var days = context.Today.DayNumber - day.DayNumber;
        if (days <= staleAfter)
            return new CheckupSection(CheckupTitles.StaleMainline, []);

        return new CheckupSection(CheckupTitles.StaleMainline,
        [
            $"- {MainlineSection.RelativePath} was last committed on {day:yyyy-MM-dd}, {days} days ago (stale after {staleAfter} days). Rewrite the mainline from the last month of activities.",
        ]);
    }

    /// <summary>The local date of the last commit that touched the mainline file; null when it was never committed.</summary>
    private static async Task<DateOnly?> LastCommitDayAsync(GitCli git, CancellationToken cancellationToken)
    {
        var line = (await git.RunAsync(cancellationToken, "log", "-1", "--format=%as", "--", MainlineSection.RelativePath)).Trim();
        return line.Length == 0 ? null : DateOnly.ParseExact(line, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
