using System.Globalization;
using TacitMemory.Core.Decisions;
using TacitMemory.Core.Models;

namespace TacitMemory.Core.Recent;

/// <summary>What the same-activity check found: today's activity that repeats the new one, or a note for the agent.</summary>
internal sealed record SameActivityVerdict(RecentEntry? Repeat, string? Note);

/// <summary>
/// Before an activity is added, asks whether today's log already records the same thing: the latest activities of
/// the day, plus "none of these", go to the decision model as a choice question.
/// </summary>
internal sealed class SameActivityCheck(TacitConfig config, IDecisionModel decision)
{
    /// <summary>Candidate activities offered besides "none of these"; the decision model takes at most 10 options.</summary>
    public const int Candidates = 9;

    public const string DecisionDown =
        "Note: the decision model is unavailable, so whether this activity repeats one from today was not checked.";

    private const string None = "none";

    private const string Instructions =
        "The state is a new activity note from today's log, written in Chinese. "
        + "Each option is an earlier activity note from the same day, also in Chinese. "
        + "Choose the option if it is about the same thing as the new note: the same matter, even if phrased differently, "
        + "or a later step, result or follow-up of that matter on the same day. "
        + "Project tags in square brackets do not decide this; ignore them. "
        + "Different problems or tasks are different things, even within the same project. "
        + "If no option is the same thing, choose the none option. "
        + "Answer with the option key only.";

    private const string NoneText = "None of the earlier activity notes is about the same thing as the new note.";

    /// <summary>Longest description of one candidate.</summary>
    private const int OptionLength = 250;

    /// <summary>Asks the decision model about <paramref name="state"/> against the latest <see cref="Candidates"/> of <paramref name="today"/>.</summary>
    public async Task<SameActivityVerdict> RunAsync(string state, IReadOnlyList<RecentEntry> today, CancellationToken cancellationToken)
    {
        var candidates = today.TakeLast(Candidates).ToList();
        if (candidates.Count == 0)
            return new SameActivityVerdict(null, null);

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var entries = new Dictionary<string, RecentEntry>(StringComparer.Ordinal);
        foreach (var entry in candidates)
        {
            var key = "p" + (entries.Count + 1).ToString(CultureInfo.InvariantCulture);
            options[key] = entry.Body.Length > OptionLength ? entry.Body[..OptionLength] : entry.Body;
            entries[key] = entry;
        }

        options[None] = NoneText;
        var probabilities = await decision.ChooseAsync(state, Instructions, options, cancellationToken);
        if (probabilities is null)
            return new SameActivityVerdict(null, DecisionDown);

        var verdict = Judgment.Choice(probabilities, None, config.Decision.SameActivity ?? Judgment.SameActivity);
        return verdict.Tier == Tier.Certain
            ? new SameActivityVerdict(entries[verdict.Option!], null)
            : new SameActivityVerdict(null, null);
    }
}
