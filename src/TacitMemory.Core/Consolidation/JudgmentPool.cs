using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TacitMemory.Core.Decisions;
using TacitMemory.Core.Index;
using TacitMemory.Core.Models;

namespace TacitMemory.Core.Consolidation;

/// <summary>
/// The choice judgments of the consolidation, shared by every consolidation of the service. At most <c>consolidation.maxConcurrency</c> of
/// them run at once, each in one slot: its text is prepared (embedded and searched), then its question is answered from
/// the judgment cache, or asked of the decision model and cached. The budget holds back only the asking: a judgment that
/// holds a slot still reads the cache after the budget ran out, and a model that is cooling down or not configured is
/// not called. Only the consolidation reads and writes the cache; the live judgments never do. A judgment that has started always
/// runs to its end, so its answer is cached even when the consolidation that asked stopped waiting for it.
/// </summary>
internal sealed class JudgmentPool(MemoryIndex index, ChoiceJudge judge, TimeProvider time, int maxConcurrency)
{
    private readonly SemaphoreSlim _slots = new(maxConcurrency, maxConcurrency);
    private readonly Lock _gate = new();
    private readonly List<Task> _running = [];

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
            var prepared = await judge.PrepareAsync(prepare, CancellationToken.None);
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
        var choice = ChoiceJudge.Number(question);
        var cacheKey = CacheKey(choice.State, choice.Instructions, choice.Options);
        if (!judge.Configured)
            return JudgmentOutcome<T>.Unchecked(UncheckedReason.Unconfigured);
        var probabilities = index.CachedJudgment(cacheKey);
        if (probabilities is null)
        {
            if (judge.Health.CoolingDown)
                return JudgmentOutcome<T>.Unchecked(UncheckedReason.ModelUnavailable);
            if (queue.IsCancellationRequested)
                return JudgmentOutcome<T>.Unchecked(UncheckedReason.Budget);
            probabilities = await judge.ProbabilitiesAsync(choice, CancellationToken.None);
            if (probabilities is null)
                return JudgmentOutcome<T>.Unchecked(UncheckedReason.ModelUnavailable);
            index.StoreJudgment(cacheKey, probabilities, time.GetUtcNow());
        }
        return choice.Outcome(probabilities, threshold);
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
