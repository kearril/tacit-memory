using TacitMemory.Core.Decisions;
using TacitMemory.Core.Git;
using TacitMemory.Core.Recent;

namespace TacitMemory.Core.Consolidation;

/// <summary>
/// The numbers the consolidation reminder and <c>tacit status</c> both show: the last consolidation, and the pending counts. Every
/// count comes from the report's categories that need no model, so reading the summary never calls one.
/// </summary>
/// <param name="Baseline">The last consolidation, or the init commit standing in for it, or none.</param>
/// <param name="DaysSince">Days from the baseline's commit to today; <c>null</c> when there is no baseline commit.</param>
/// <param name="CandidatesReadable">Whether <c>candidates.md</c> is valid UTF-8; the candidate count is zero otherwise.</param>
/// <param name="Confirmable">Candidates that are not a sure repeat of a persona entry or a rejected candidate.</param>
/// <param name="Problems">Format and rule problems.</param>
/// <param name="MainlineEmpty">Whether the mainline has no items.</param>
internal sealed record ConsolidationSummary(
    ConsolidationBaseline Baseline,
    int? DaysSince,
    bool CandidatesReadable,
    int Confirmable,
    int Problems,
    bool MainlineEmpty)
{
    /// <summary>
    /// Reads the repository. Throws when git cannot read it, so each caller decides what that means: the reminder gives
    /// none, the status report says why.
    /// </summary>
    /// <exception cref="TacitException">The repository's history cannot be read.</exception>
    /// <exception cref="IOException">A file the counts read is held open by another program.</exception>
    public static async Task<ConsolidationSummary> ReadAsync(TacitConfig config, DateOnly today, CancellationToken cancellationToken)
    {
        var git = new GitCli(config.MemoryPath, config.Language);
        var baseline = await ConsolidationBaseline.ReadAsync(git, cancellationToken);
        var context = new ConsolidationContext(config, today, git, baseline);
        // Candidates are counted without a model: every one that is not a sure repeat of an entry counts.
        var review = CandidateReview.Unjudged(config);
        var confirmable = review?.Judgments.Count(judgment => judgment.Outcome.Tier != Tier.Certain) ?? 0;
        var problems = FormatViolations.Issues(context).Count;
        var mainlineEmpty = MainlineSection.ReadItems(config.MemoryPath).Count == 0;

        var days = baseline.Commit is { } commit ? today.DayNumber - commit.Day.DayNumber : (int?)null;
        return new ConsolidationSummary(baseline, days, review is not null, confirmable, problems, mainlineEmpty);
    }
}
