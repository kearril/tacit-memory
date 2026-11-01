using TacitMemory.Core.Decisions;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Persona;

namespace TacitMemory.Core.Checkup;

/// <summary>
/// Candidates that state a preference the persona or the rejection list already holds, whatever their days: the agent
/// deletes them without asking, so each finding names what it repeats. An unsure match is not listed here; that candidate
/// stays confirmable and is hinted there.
/// </summary>
internal static class RedundantCandidates
{
    public static CheckupSection Produce(TacitConfig config, CandidateReview? review)
    {
        if (review is null)
            return new CheckupSection(CheckupTitles.RedundantCandidates, [], CandidateReview.NotReadable);

        var language = config.Language;
        var findings = new List<string>();
        foreach (var judgment in review.Judgments)
        {
            if (judgment.Outcome.Tier == Tier.Certain && judgment.Outcome.Target is { } match)
                findings.Add($"- \"{judgment.Candidate.Text}\"; section: {PersonaDocument.HeadingOf(judgment.Candidate.Section).In(language)}; the same preference as {review.Describe(match)}.");
        }

        var status = Unanswered.Of(review.Judgments.Select(judgment => judgment.Outcome.Reason)).Status(
            count => $"Not checked: the decision or embedding model is unavailable, so whether {count} candidate(s) repeat the persona or the rejection list was not checked.",
            count => $"{count} candidate(s) not checked yet, because the checkup ran out of time. Run `checkup` again to continue.");
        return new CheckupSection(CheckupTitles.RedundantCandidates, findings, status);
    }
}
