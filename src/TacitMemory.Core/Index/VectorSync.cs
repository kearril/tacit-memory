using TacitMemory.Core.Models;

namespace TacitMemory.Core.Index;

/// <summary>
/// Gives every index unit a vector. Units without one are embedded in batches; when the embedding model is down
/// they wait, and the next successful call to the model fills them in. One fill runs at a time.
/// </summary>
internal sealed class VectorSync(MemoryIndex index, EmbeddingModel model)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _backgroundGate = new();

    /// <summary>
    /// Embeds every unit still missing a vector. <c>false</c> when the embedding model is configured but could not be reached.
    /// Without a configured embedding model nothing can be waited for, so it is <c>true</c> and nothing is tried.
    /// </summary>
    public async Task<bool> FillAsync(CancellationToken cancellationToken)
    {
        if (!model.Configured)
            return true;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            while (true)
            {
                var pending = index.PendingUnits(EmbeddingModel.BatchSize * 4);
                if (pending.Count == 0)
                    return true;
                var vectors = await model.EmbedAsync(pending.Select(unit => unit.Passage).ToList(), cancellationToken);
                if (vectors is null)
                    return false;
                index.StoreVectors(pending.Zip(vectors).ToList());
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Ensures stored vectors match <paramref name="dimension"/> and fills any missing vectors so a vector search right
    /// after can reach every indexed unit.
    /// </summary>
    public async Task PrepareSearchAsync(int dimension, CancellationToken cancellationToken)
    {
        if (index.Dimension is { } stored && stored != dimension)
            index.ResetVectors();
        if (index.PendingUnits(1).Count > 0)
            await FillAsync(cancellationToken);
    }

    /// <summary>Queues a fill in the background; runs one after another and failures only leave units waiting.</summary>
    public void FillInBackground()
    {
        lock (_backgroundGate)
        {
            Background = Background.ContinueWith(
                _ => FillSafelyAsync(),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default).Unwrap();
        }
    }

    /// <summary>Completes when all queued background fills have finished; tests wait for it.</summary>
    public Task Background { get; private set; } = Task.CompletedTask;

    private async Task FillSafelyAsync()
    {
        try
        {
            await FillAsync(CancellationToken.None);
        }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            // The service is shutting down, or the index is busy: the units stay pending for the next fill.
        }
    }
}
