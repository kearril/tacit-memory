using TacitMemory.Core.Models;

namespace TacitMemory.Core.Index;

/// <summary>
/// Gives every index unit a vector. Units without one are embedded in batches; when the embedding model is down
/// they wait, and the next successful call to the model fills them in. One fill runs at a time.
/// </summary>
internal sealed class VectorSync(PageIndex index, EmbeddingModel model)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _requested;

    /// <summary>Embeds every unit still missing a vector. <c>false</c> when the model could not be reached.</summary>
    public async Task<bool> FillAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            while (true)
            {
                var pending = index.PendingUnits(EmbeddingModel.BatchSize * 4);
                if (pending.Count == 0)
                    return true;
                var vectors = await model.EmbedAsync(pending.Select(unit => unit.Text).ToList(), cancellationToken);
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

    /// <summary>Starts a fill in the background unless one is already queued; failures only leave units waiting.</summary>
    public void FillInBackground()
    {
        if (Interlocked.Exchange(ref _requested, 1) == 1)
            return;
        Background = Task.Run(async () =>
        {
            Interlocked.Exchange(ref _requested, 0);
            try
            {
                await FillAsync(CancellationToken.None);
            }
            catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or IOException)
            {
                // The service is shutting down, or the index is busy: the units stay pending for the next fill.
            }
        });
    }

    /// <summary>The latest background fill; tests wait for it.</summary>
    public Task Background { get; private set; } = Task.CompletedTask;
}
