namespace TacitMemory.Core.Consolidation;

/// <summary>
/// The judgments of one consolidation. Judgments are submitted to the pool as they are prepared and start at once, but the
/// consolidation waits for them only while the budget lasts. The budget is started explicitly, after every category has
/// submitted its judgments, so reading the repository is never counted. When it runs out, a judgment that has not
/// got a slot is not started, and one already running is left to finish, so its answer still reaches the cache.
/// </summary>
internal sealed class JudgmentSession(JudgmentPool pool, TimeProvider time, CancellationToken cancellationToken) : IDisposable
{
    private readonly CancellationToken _request = cancellationToken;

    /// <summary>Cancelled when the budget runs out or the consolidation ends: a judgment that has not started stops here.</summary>
    private readonly CancellationTokenSource _queue = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

    /// <summary>Stops the budget timer when the consolidation ends.</summary>
    private readonly CancellationTokenSource _ended = new();

    private Task? _expired;

    /// <summary>Whether a judgment was left unanswered because the budget ran out; the report's header says so.</summary>
    public bool BudgetRanOut { get; private set; }

    /// <summary>Submits a choice judgment. Nothing is awaited here, so the judgments of a consolidation run side by side.</summary>
    public Task<JudgmentOutcome<T>> Submit<T>(Func<CancellationToken, Task<PreparedChoice<T>>> prepare, Threshold threshold) =>
        pool.Start(prepare, threshold, _queue.Token);

    /// <summary>Starts the budget. Call it once, after the last judgment of the consolidation is submitted.</summary>
    public void StartBudget(TimeSpan budget)
    {
        if (_expired is not null)
            throw new InvalidOperationException("The budget of a consolidation is started once.");
        _expired = Task.Delay(budget, time, _ended.Token).ContinueWith(_ => _queue.Cancel(), TaskScheduler.Default);
    }

    /// <summary>
    /// The outcome of a submitted judgment: its own once it has finished, or <see cref="UncheckedReason.Budget"/> when the
    /// budget ran out first. The judgment itself is not stopped.
    /// </summary>
    public async Task<JudgmentOutcome<T>> CollectAsync<T>(Task<JudgmentOutcome<T>> judgment)
    {
        var expired = _expired ?? throw new InvalidOperationException("The budget of a consolidation starts before its judgments are collected.");
        _request.ThrowIfCancellationRequested();
        if (!judgment.IsCompleted)
            await Task.WhenAny(judgment, expired);
        _request.ThrowIfCancellationRequested();
        if (judgment.IsCompleted)
            return await judgment;
        BudgetRanOut = true;
        return JudgmentOutcome<T>.Unchecked(UncheckedReason.Budget);
    }

    /// <summary>Ends the consolidate: judgments that have not started are not started, and the budget timer stops.</summary>
    public void Dispose()
    {
        _ended.Cancel();
        _queue.Cancel();
    }
}
