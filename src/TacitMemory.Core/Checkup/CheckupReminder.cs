namespace TacitMemory.Core.Checkup;

/// <summary>
/// The checkup reminder the server instructions end with. When a checkup is due it says how long ago the last one was,
/// how many candidates can be confirmed, how many format problems there are and whether the mainline is stale. Every
/// count comes from <see cref="CheckupSummary"/>, which asks no model, so building the reminder never calls one.
/// </summary>
internal static class CheckupReminder
{
    /// <summary>
    /// The reminder text, or null when none is due. It is due when the last checkup is at least
    /// <c>checkup.remindAfterDays</c> days old, or when a candidate can be confirmed; format problems alone never bring it.
    /// A repository with neither a checkup nor an init commit has nothing to count from, so it counts as due.
    /// </summary>
    /// <remarks>
    /// The reminder is advice, so a repository that cannot be read gives no reminder rather than stopping the service.
    /// </remarks>
    public static async Task<string?> RenderAsync(TacitConfig config, DateOnly today, CancellationToken cancellationToken)
    {
        try
        {
            var summary = await CheckupSummary.ReadAsync(config, today, cancellationToken);
            var due = summary.DaysSince is null || summary.DaysSince >= config.Checkup.RemindAfterDays;
            if (!due && summary.Confirmable == 0)
                return null;

            return Render(summary);
        }
        catch (TacitException e) when (e.Error == TacitError.GitFailed)
        {
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A file another program holds open, such as an editor mid-save, must not stop the session from starting.
            return null;
        }
    }

    private static string Render(CheckupSummary summary) =>
        string.Join("\n",
        [
            "## Checkup reminder",
            BaselineLine(summary.Baseline, summary.DaysSince),
            $"- Candidates that can be confirmed: {summary.Confirmable}.",
            $"- Format and rule problems: {summary.Problems}.",
            summary.MainlineStale ? "- Mainline: stale." : "- Mainline: not stale.",
            "The user can run the tacit-checkup skill to start a checkup; suggest it when the moment fits, and do not start one unasked.",
        ]);

    private static string BaselineLine(CheckupBaseline baseline, int? days) => baseline.Kind switch
    {
        BaselineKind.Checkup => $"Last checkup: {Days(days!.Value)} ago, on {baseline.Commit!.Day:yyyy-MM-dd}.",
        BaselineKind.Init => $"No checkup has been run yet; the repository was set up {Days(days!.Value)} ago, on {baseline.Commit!.Day:yyyy-MM-dd}.",
        _ => "No checkup has been run, and the repository has no init commit.",
    };

    private static string Days(int count) => count == 1 ? "1 day" : $"{count} days";
}
