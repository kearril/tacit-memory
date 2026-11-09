using TacitMemory.Core.Decisions;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Persona;

namespace TacitMemory.Core.Consolidation;

/// <summary>
/// Candidates that are not a sure repeat of the persona or the rejection list: the user is asked to confirm, reject or defer
/// each one. There is no day threshold any more. Each finding gives the text, the section it belongs to, the day count and
/// every quote. A candidate the decision model finds possibly the same as an existing entry says so. When a listed
/// candidate could not be compared, whether the models were unavailable or not configured, or the budget ran out, the
/// section says so and asks the agent to compare the candidates itself.
/// </summary>
internal static class ConfirmableCandidates
{
    public static ConsolidationSection Produce(TacitConfig config, CandidateReview? review)
    {
        if (review is null)
            return new ConsolidationSection(ConsolidationTitles.ConfirmableCandidates, [], CandidateReview.NotReadable);

        var language = config.Language;
        var findings = new List<string>();
        var listed = new List<CandidateJudgment>();
        foreach (var judgment in review.Judgments)
        {
            var candidate = judgment.Candidate;
            if (judgment.Outcome.Tier == Tier.Certain)
                continue;

            listed.Add(judgment);
            var lines = new List<string>
            {
                $"- \"{candidate.Text}\"; section: {PersonaDocument.HeadingOf(candidate.Section).In(language)}; appeared on {judgment.Days} days.",
            };
            if (judgment.Outcome.Tier == Tier.Uncertain && judgment.Outcome.Target is { } match)
                lines.Add($"  - possibly the same as {review.Describe(match)}; compare before asking the user.");
            lines.AddRange(candidate.Occurrences.Select(occurrence =>
                $"  - {occurrence.Day:yyyy-MM-dd} {StructureWords.Quote.In(language)}「{occurrence.Quote}」"));
            findings.Add(string.Join("\n", lines));
        }

        var unanswered = Unanswered.Of(listed.Select(judgment => judgment.Outcome.Reason));
        var because = new List<string>();
        if (unanswered.ModelUnavailable > 0)
            because.Add("the decision or embedding model is unavailable");
        if (unanswered.Unconfigured > 0)
            because.Add("the decision or embedding model is not configured");
        if (unanswered.OutOfBudget > 0)
            because.Add("the consolidation ran out of time");
        var status = because.Count == 0
            ? null
            : $"Not checked for an existing same preference: {string.Join(", and ", because)}. "
              + "Compare each candidate below with the persona yourself before asking the user.";
        return new ConsolidationSection(ConsolidationTitles.ConfirmableCandidates, findings, status);
    }
}
