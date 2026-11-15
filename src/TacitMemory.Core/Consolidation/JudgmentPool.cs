using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TacitMemory.Core.Decisions;
using TacitMemory.Core.Index;
using TacitMemory.Core.Models;

namespace TacitMemory.Core.Consolidation;

/// <summary>One option of a choice question: the text the decision model reads, and the target the consolidation takes when it is chosen.</summary>
internal sealed record ChoiceOption<T>(string Text, T Target);

/// <summary>
/// A choice question: its state and instructions, its options, and the text of the "none of these" option that the pool
/// adds last. Options are keyed by their position.
/// </summary>
internal sealed record ChoiceQuestion<T>(string State, string Instructions, string NoneText, IReadOnlyList<ChoiceOption<T>> Options);

/// <summary>
/// What a judgment prepared before it asks: the question, or <c>null</c> when there is nothing to compare with; or the reason
/// it is not checked, when the text could not be prepared because a model gave no answer.
/// </summary>
internal sealed record PreparedChoice<T>(ChoiceQuestion<T>? Question, UncheckedReason? Unchecked);

/// <summary>
/// Why a judgment of a consolidation was not answered: the models could not answer it, they are not configured, or the budget
/// ran out first.
/// </summary>
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
}

/// <summary>
/// The choice judgments of the consolidation, shared by every consolidation of the service. At most <c>consolidation.maxConcurrency</c> of
/// them run at once, each in one slot: its text is prepared (embedded and searched), then its question is answered from
/// the judgment cache, or asked of the decision model and cached. The budget holds back only the asking: a judgment that
/// holds a slot still reads the cache after the budget ran out, and a model that is cooling down or not configured is
/// not called. Only the consolidation reads and writes the cache; the live judgments never do. A judgment that has started always
/// runs to its end, so its answer is cached even when the consolidation that asked stopped waiting for it.
/// </summary>
internal sealed class JudgmentPool(MemoryIndex index, IDecisionModel decision, TimeProvider time, int maxConcurrency)
{
    /// <summary>The key of the "none of these" option, which every choice question ends with.</summary>
    private const string NoneKey = "none";

    private readonly SemaphoreSlim _slots = new(maxConcurrency, maxConcurrency);
    private readonly Lock _gate = new();
    private readonly List<Task> _running = [];

    /// <summary>The reason a text whose model gave no answer is not checked: the model is unavailable, or it is not configured.</summary>
    public static UncheckedReason NoAnswerReason(bool configured) =>
        configured ? UncheckedReason.ModelUnavailable : UncheckedReason.Unconfigured;

    /// <summary>Starts the judgments of one consolidation. The budget is started by the session, once every judgment is submitted.</summary>
    public JudgmentSession Begin(CancellationToken cancellationToken) => new(this, time, cancellationToken);

    /// <summary>Completes when every judgment started so far has finished, including those a consolidation stopped waiting for.</summary>
    public Task Completion
    {
        get
        {
            Task[] running;
            lock (_gate)
                running = [.. _running];
            return Task.WhenAll(running.Select(task => task.ContinueWith(_ => { }, TaskScheduler.Default)));
        }
    }

    /// <summary>Starts one judgment. It waits for a slot, and does not start at all when <paramref name="queue"/> is cancelled first.</summary>
    internal Task<JudgmentOutcome<T>> Start<T>(
        Func<CancellationToken, Task<PreparedChoice<T>>> prepare, Threshold threshold, CancellationToken queue)
    {
        var judgment = RunAsync(prepare, threshold, queue);
        lock (_gate)
            _running.Add(judgment);
        _ = judgment.ContinueWith(finished =>
        {
            lock (_gate)
                _running.Remove(finished);
        }, TaskScheduler.Default);
        return judgment;
    }

    private async Task<JudgmentOutcome<T>> RunAsync<T>(
        Func<CancellationToken, Task<PreparedChoice<T>>> prepare, Threshold threshold, CancellationToken queue)
    {
        try
        {
            await _slots.WaitAsync(queue);
        }
        catch (OperationCanceledException)
        {
            // The budget ran out before this judgment got a slot: it is not started.
            return JudgmentOutcome<T>.Unchecked(UncheckedReason.Budget);
        }

        try
        {
            var prepared = await prepare(CancellationToken.None);
            if (prepared.Unchecked is { } reason)
                return JudgmentOutcome<T>.Unchecked(reason);
            if (prepared.Question is not { } question)
                return JudgmentOutcome<T>.NothingToAsk;
            return await AskAsync(question, threshold, queue);
        }
        finally
        {
            _slots.Release();
        }
    }

    /// <summary>
    /// Answers the question when the decision model is configured: from the cache when it is there, otherwise by asking the
    /// model, unless it is cooling down or the budget has run out. An unconfigured model answers nothing and the cache is not
    /// read, so a cached answer never stands in for a model the config has removed.
    /// </summary>
    private async Task<JudgmentOutcome<T>> AskAsync<T>(ChoiceQuestion<T> question, Threshold threshold, CancellationToken queue)
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

        var cacheKey = CacheKey(question.State, question.Instructions, texts);
        if (!decision.Configured)
            return JudgmentOutcome<T>.Unchecked(UncheckedReason.Unconfigured);
        var probabilities = index.CachedJudgment(cacheKey);
        if (probabilities is null)
        {
            if (decision.Health.CoolingDown)
                return JudgmentOutcome<T>.Unchecked(UncheckedReason.ModelUnavailable);
            if (queue.IsCancellationRequested)
                return JudgmentOutcome<T>.Unchecked(UncheckedReason.Budget);
            probabilities = await decision.ChooseAsync(question.State, question.Instructions, texts, CancellationToken.None);
            if (probabilities is null)
                return JudgmentOutcome<T>.Unchecked(UncheckedReason.ModelUnavailable);
            index.StoreJudgment(cacheKey, probabilities, time.GetUtcNow());
        }

        var verdict = Judgment.Choice(probabilities, NoneKey, threshold);
        var target = verdict.Option is null ? default : targets[verdict.Option];
        return new JudgmentOutcome<T>(verdict.Tier, target, null);
    }

    /// <summary>
    /// The cache key of a question: the hash of its kind, its state, its instructions and every option's key and text,
    /// in key order. The same question always has the same key, and a changed one never reads an old answer.
    /// </summary>
    internal static string CacheKey(string state, string instructions, IReadOnlyDictionary<string, string> options)
    {
        var content = JsonSerializer.Serialize(
            new CacheKeyContent(
                "choice", state, instructions,
                options.OrderBy(option => option.Key, StringComparer.Ordinal).Select(option => new[] { option.Key, option.Value }).ToArray()),
            CoreJson.Default.CacheKeyContent);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    }

    /// <summary>What a cache key hashes, as JSON with these member names in this order.</summary>
    internal sealed record CacheKeyContent(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("instructions")] string Instructions,
        [property: JsonPropertyName("options")] string[][] Options);
}
