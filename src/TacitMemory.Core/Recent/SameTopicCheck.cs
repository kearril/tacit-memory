using TacitMemory.Core.Decisions;
using TacitMemory.Core.Models;

namespace TacitMemory.Core.Recent;

/// <summary>What the same-topic check found: today's activity about the same thing as the new one, or a note for the agent.</summary>
internal sealed record SameTopicVerdict(RecentFile.Activity? Same, string? Note);

/// <summary>
/// Before an activity is added, asks whether today's log already records the same thing: the latest activities of
/// the day, plus "none of these", go to the decision model as a choice question.
/// </summary>
internal sealed class SameTopicCheck(TacitConfig config, ChoiceJudge judge)
{
    /// <summary>Activities offered besides "none of these"; the decision model takes at most 10 options.</summary>
    public const int OtherOptions = 9;

    public const string DecisionDown =
        "Note: the decision model is unavailable, so whether this activity is about the same thing as one from today was not checked.";

    private const string Instructions =
        "The state is a new activity note from today's log. "
        + "Each option is an earlier activity note from the same day. "
        + "Choose the option if it is about the same thing as the new note: the same matter, even if phrased differently, "
        + "or a later step, result or follow-up of that matter on the same day. "
        + "Workstream tags in square brackets do not decide this; ignore them. "
        + "Different problems or tasks are different things, even within the same workstream. "
        + "If no option is the same thing, choose the none option. "
        + "Answer with the option key only.";

    private const string NoneText = "None of the earlier activity notes is about the same thing as the new note.";

    /// <summary>Longest description of one candidate.</summary>
    private const int OptionLength = 250;

    /// <summary>Asks the decision model about <paramref name="state"/> against the latest <see cref="OtherOptions"/> of <paramref name="today"/>.</summary>
    public async Task<SameTopicVerdict> RunAsync(string state, IReadOnlyList<RecentFile.Activity> today, CancellationToken cancellationToken)
    {
        var options = today.TakeLast(OtherOptions)
            .Select(entry => new ChoiceOption<RecentFile.Activity>(
                entry.Labeled.Length > OptionLength ? entry.Labeled[..OptionLength] : entry.Labeled, entry))
            .ToList();
        var outcome = await judge.AskAsync(
            Neighbours.Elsewhere,
            _ => Task.FromResult(options.Count == 0
                ? PreparedChoice<RecentFile.Activity>.Nothing
                : new PreparedChoice<RecentFile.Activity>(new ChoiceQuestion<RecentFile.Activity>(state, Instructions, NoneText, options), null)),
            config.Decision.SameTopic ?? Judgment.SameTopic,
            cancellationToken);
        return new SameTopicVerdict(outcome.Certain, ChoiceJudge.NoteOf(outcome.Reason, embeddingFailed: false, "", DecisionDown));
    }
}
