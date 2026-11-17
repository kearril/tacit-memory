using TacitMemory.Core.Decisions;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Persona;

namespace TacitMemory.Core.Consolidation;

/// <summary>
/// Candidates that state a preference the persona or the rejection list already holds, whatever their days: the agent
/// deletes them without asking, so each finding names what it repeats. An unsure match is not listed here; that candidate
/// stays a candidate and is hinted there.
/// </summary>
internal static class RedundantCandidates
{
    public static ConsolidationSection Produce(TacitConfig config, CandidateReview? review)
    {
        if (review is null)
            return new ConsolidationSection(ConsolidationTitles.RedundantCandidates, [], CandidateReview.NotReadable);

        var language = config.Language;
        var findings = new List<string>();
        foreach (var judgment in review.Judgments)
        {
            if (judgment.Outcome.Tier == Tier.Certain && judgment.Outcome.Target is { } match)
                findings.Add($"- \"{judgment.Candidate.Text}\"; section: {PersonaDocument.HeadingOf(judgment.Candidate.Section)}; the same preference as {review.Describe(match)}.");
        }

        var status = Unanswered.Of(review.Judgments.Select(judgment => judgment.Outcome.Reason)).Status(
            count => $"Not checked: the decision or embedding model is unavailable, so whether {count} candidate(s) repeat the persona or the rejection list was not checked.",
            count => $"Not checked (not configured): the decision or embedding model is not configured, so whether {count} candidate(s) repeat the persona or the rejection list was not checked.",
            count => $"{count} candidate(s) not checked yet, because the consolidation ran out of time. Run the tacit-consolidate skill again to continue.");
        return new ConsolidationSection(ConsolidationTitles.RedundantCandidates, findings, status);
    }
}
