using TacitMemory.Core.Git;

namespace TacitMemory.Core.Checkup;

/// <summary>
/// The checkup reminder the server instructions end with. When a checkup is due it says how long ago the last one was,
/// how many candidates can be confirmed, how many format problems there are and whether the mainline is stale. Every
/// count comes from the report's categories that need no model, so building the reminder never calls one.
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
            var git = new GitCli(config.MemoryPath, config.Language);
            var context = new CheckupContext(config, today, git, await CheckupBaseline.ReadAsync(git, cancellationToken));
            // Candidates are counted by the days they appeared on only, so the reminder asks no model.
            var confirmable = ConfirmableCandidates.Produce(config, CandidateReview.Unjudged(config)).Findings.Count;
            var problems = FormatViolations.Produce(context).Findings.Count;
            var mainlineStale = (await StaleMainline.ProduceAsync(context, cancellationToken)).Findings.Count > 0;

            var baseline = context.Baseline;
            var days = baseline.Commit is { } commit ? today.DayNumber - commit.Day.DayNumber : (int?)null;
            var due = days is null || days >= config.Checkup.RemindAfterDays;
            if (!due && confirmable == 0)
                return null;

            return Render(baseline, days, confirmable, problems, mainlineStale);
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

    private static string Render(CheckupBaseline baseline, int? days, int confirmable, int problems, bool mainlineStale) =>
        string.Join("\n",
        [
            "## Checkup reminder",
            BaselineLine(baseline, days),
            $"- Candidates that can be confirmed: {confirmable}.",
            $"- Format and rule problems: {problems}.",
            mainlineStale ? "- Mainline: stale." : "- Mainline: not stale.",
            "The user can run `checkup` to start a checkup; suggest it when the moment fits, and do not start one unasked.",
        ]);

    private static string BaselineLine(CheckupBaseline baseline, int? days) => baseline.Kind switch
    {
        BaselineKind.Checkup => $"Last checkup: {Days(days!.Value)} ago, on {baseline.Commit!.Day:yyyy-MM-dd}.",
        BaselineKind.Init => $"No checkup has been run yet; the repository was set up {Days(days!.Value)} ago, on {baseline.Commit!.Day:yyyy-MM-dd}.",
        _ => "No checkup has been run, and the repository has no init commit.",
    };

    private static string Days(int count) => count == 1 ? "1 day" : $"{count} days";
}
