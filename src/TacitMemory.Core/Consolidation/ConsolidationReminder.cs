namespace TacitMemory.Core.Consolidation;

/// <summary>
/// The consolidation reminder the server instructions end with. When a consolidation is due (see
/// <see cref="ConsolidationSummary.IsDue"/>) it says how long ago the last consolidation was, how many weeks wait for a summary,
/// how many candidates there are, how many format problems there are and whether the workstream is empty. Every count comes
/// from <see cref="ConsolidationSummary"/>, which asks no model, so building the reminder never calls one.
/// </summary>
internal static class ConsolidationReminder
{
    /// <summary>The reminder text, or null when no consolidation is due.</summary>
    /// <remarks>
    /// The reminder is advice, so a repository that cannot be read gives no reminder rather than stopping the service.
    /// </remarks>
    public static async Task<string?> RenderAsync(TacitConfig config, DateOnly today, CancellationToken cancellationToken)
    {
        try
        {
            var summary = await ConsolidationSummary.ReadAsync(config, today, cancellationToken);
            return summary.IsDue(config.Consolidation) ? Render(summary) : null;
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

    private static string Render(ConsolidationSummary summary) =>
        string.Join("\n",
        [
            "## Consolidation reminder",
            BaselineLine(summary.Baseline, summary.DaysSince),
            $"- Weeks to summarize: {summary.UnsummarizedWeeks}.",
            $"- Persona candidates: {summary.Candidates}.",
            $"- Format and rule problems: {summary.Problems}.",
            summary.WorkstreamEmpty ? "- Workstream: empty." : "- Workstream: not empty.",
            "The user can run the tacit-consolidate skill to start a consolidation; suggest it when the moment fits, and do not start one unasked.",
        ]);

    private static string BaselineLine(ConsolidationBaseline baseline, int? days) => baseline.Kind switch
    {
        BaselineKind.Consolidation => $"Last consolidation: {Days(days!.Value)} ago, on {baseline.Commit!.Day:yyyy-MM-dd}.",
        BaselineKind.Init => $"No consolidation has been run yet; the repository was set up {Days(days!.Value)} ago, on {baseline.Commit!.Day:yyyy-MM-dd}.",
        _ => "No consolidation has been run, and the repository has no init commit.",
    };

    private static string Days(int count) => count == 1 ? "1 day" : $"{count} days";
}
