namespace TacitMemory.Core.Models;

/// <summary>The auxiliary models the memory service calls, sharing one HTTP client and one retry policy.</summary>
internal sealed class AuxiliaryModels : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public AuxiliaryModels(TacitConfig config, TimeProvider time)
    {
        var client = new ModelClient(_http, time);
        Embedding = new EmbeddingModel(client, config.Models.Embedding, new ModelHealth(time));
        Reranker = new RerankerModel(client, config.Models.Reranker, new ModelHealth(time));
        Decision = new JevDecisionModel(client, config.Models.Decision, new ModelHealth(time));
        // A missing key is known without a call; the decision model is paid, so it is never probed.
        if (JevDecisionModel.Key(config.Models.Decision) is null)
            Decision.Health.Disable();
    }

    public EmbeddingModel Embedding { get; }

    public RerankerModel Reranker { get; }

    public IDecisionModel Decision { get; }

    /// <summary>The models cooling down after a failed request, by role name.</summary>
    public IReadOnlyList<string> Unavailable =>
        new[] { ("embedding", Embedding.Health), ("reranker", Reranker.Health), ("decision", Decision.Health) }
            .Where(model => model.Item2.CoolingDown)
            .Select(model => model.Item1)
            .ToList();

    public void Dispose() => _http.Dispose();
}
