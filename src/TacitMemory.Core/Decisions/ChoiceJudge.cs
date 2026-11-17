using System.Globalization;
using TacitMemory.Core.Index;
using TacitMemory.Core.Models;

namespace TacitMemory.Core.Decisions;

/// <summary>One option of a choice question: the text the decision model reads, and the target taken when it is chosen.</summary>
internal sealed record ChoiceOption<T>(string Text, T Target);

/// <summary>
/// A choice question: its state and instructions, its options, and the text of the "none of these" option that is added
/// last. Options are keyed by their position.
/// </summary>
internal sealed record ChoiceQuestion<T>(string State, string Instructions, string NoneText, IReadOnlyList<ChoiceOption<T>> Options);

/// <summary>
/// What a judgment prepared before it asks: the question, or <c>null</c> when there is nothing to compare with; or the reason
/// it is not checked, when the text could not be prepared because a model gave no answer.
/// </summary>
internal sealed record PreparedChoice<T>(ChoiceQuestion<T>? Question, UncheckedReason? Unchecked)
{
    /// <summary>Nothing to compare with: the judgment finds no match without asking.</summary>
    public static PreparedChoice<T> Nothing { get; } = new(null, null);
}

/// <summary>Why a judgment was not answered: the models could not answer it, they are not configured, or the budget ran out first.</summary>
internal enum UncheckedReason
{
    ModelUnavailable,
    Unconfigured,
    Budget,
}

/// <summary>
/// How one judgment ended: its tier and, unless the tier is <see cref="Tier.Unlikely"/>, the target of the option it chose;
/// or why it was not checked. <see cref="NothingToAsk"/> is a judgment with no option to compare with, which is no match.
/// </summary>
internal sealed record JudgmentOutcome<T>(Tier? Tier, T? Target, UncheckedReason? Reason)
{
    /// <summary>No option to compare with: nothing matches.</summary>
    public static JudgmentOutcome<T> NothingToAsk { get; } = new(Decisions.Tier.Unlikely, default, null);

    public static JudgmentOutcome<T> Unchecked(UncheckedReason reason) => new(null, default, reason);

    /// <summary>The target of a certain match; <c>null</c> for any other outcome.</summary>
    public T? Certain => Tier == Decisions.Tier.Certain ? Target : default;
}

/// <summary>
/// Where a question's options come from: the persona and candidate entries of the index, which a judgment brings up to date
/// with their files first, or anywhere else.
/// </summary>
internal enum Neighbours
{
    Elsewhere,
    PersonaEntries,
}

/// <summary>
/// A choice question as the decision model receives it: the options keyed <c>o1</c>, <c>o2</c> and so on, then <c>none</c>,
/// and the target behind each key.
/// </summary>
internal sealed record NumberedChoice<T>(
    string State, string Instructions, IReadOnlyDictionary<string, string> Options, IReadOnlyDictionary<string, T> Targets)
{
    /// <summary>The outcome the model's <paramref name="probabilities"/> give by <paramref name="threshold"/>.</summary>
    public JudgmentOutcome<T> Outcome(IReadOnlyDictionary<string, double> probabilities, Threshold threshold)
    {
        var verdict = Judgment.Choice(probabilities, ChoiceJudge.NoneKey, threshold);
        var target = verdict.Option is null ? default : Targets[verdict.Option];
        return new JudgmentOutcome<T>(verdict.Tier, target, null);
    }
}

/// <summary>
/// Asks the decision model a choice question and sorts its answer by a threshold: the one way a judgment is asked, before
/// a write and in the consolidation. A question whose options are persona or candidate entries brings those entries of the
/// index up to date with their files first, so a manual edit is judged as it is now.
/// </summary>
internal sealed class ChoiceJudge(MemoryIndex index, SystemOneDecisionModel decision)
{
    /// <summary>The key of the "none of these" option, which every choice question ends with.</summary>
    public const string NoneKey = "none";

    public bool Configured => decision.Configured;

    public ModelHealth Health => decision.Health;

    /// <summary>The question with its options keyed by position and "none of these" last.</summary>
    public static NumberedChoice<T> Number<T>(ChoiceQuestion<T> question)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        var targets = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var option in question.Options)
        {
            var key = "o" + (texts.Count + 1).ToString(CultureInfo.InvariantCulture);
            texts[key] = option.Text;
            targets[key] = option.Target;
        }
        texts[NoneKey] = question.NoneText;
        return new NumberedChoice<T>(question.State, question.Instructions, texts, targets);
    }

    /// <summary>Brings the persona and candidate entries of the index up to date with their files.</summary>
    public void SyncPersonaEntries() => index.SyncJudgments();

    /// <summary>The model's probability of each option; <c>null</c> when it is not configured or cannot answer.</summary>
    public Task<IReadOnlyDictionary<string, double>?> ProbabilitiesAsync<T>(NumberedChoice<T> choice, CancellationToken cancellationToken) =>
        decision.ChooseAsync(choice.State, choice.Instructions, choice.Options, cancellationToken);

    /// <summary>
    /// Prepares the question and asks it. A model that is not configured is not asked; one that gives no answer leaves the
    /// judgment unchecked, as does a model the preparation found without an answer.
    /// </summary>
    public async Task<JudgmentOutcome<T>> AskAsync<T>(
        Neighbours neighbours, Func<CancellationToken, Task<PreparedChoice<T>>> prepare, Threshold threshold, CancellationToken cancellationToken)
    {
        if (neighbours == Neighbours.PersonaEntries)
            SyncPersonaEntries();
        var prepared = await prepare(cancellationToken);
        if (prepared.Unchecked is { } reason)
            return JudgmentOutcome<T>.Unchecked(reason);
        if (prepared.Question is not { } question)
            return JudgmentOutcome<T>.NothingToAsk;
        if (!decision.Configured)
            return JudgmentOutcome<T>.Unchecked(UncheckedReason.Unconfigured);

        var choice = Number(question);
        var probabilities = await ProbabilitiesAsync(choice, cancellationToken);
        return probabilities is null
            ? JudgmentOutcome<T>.Unchecked(UncheckedReason.ModelUnavailable)
            : choice.Outcome(probabilities, threshold);
    }

    /// <summary>
    /// The note a write check gives the agent when its judgment was not answered: which model was down, or nothing when the
    /// models are not configured. <paramref name="embeddingFailed"/> says the preparation found the embedding model without an answer.
    /// </summary>
    public static string? NoteOf(UncheckedReason? reason, bool embeddingFailed, string embeddingDown, string decisionDown) =>
        reason == UncheckedReason.ModelUnavailable ? embeddingFailed ? embeddingDown : decisionDown : null;

    /// <summary>The reason a text whose model gave no answer is not checked: the model is unavailable, or it is not configured.</summary>
    public static UncheckedReason NoAnswerReason(bool configured) =>
        configured ? UncheckedReason.ModelUnavailable : UncheckedReason.Unconfigured;
}
