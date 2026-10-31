namespace TacitMemory.Core.Models;

/// <summary>The auxiliary models the memory service calls, sharing one HTTP client.</summary>
internal sealed class AuxiliaryModels : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public AuxiliaryModels(TacitConfig config, TimeProvider time)
    {
        Embedding = new EmbeddingModel(_http, config.Models.Embedding, new ModelHealth(time));
        Reranker = new RerankerModel(_http, config.Models.Reranker, new ModelHealth(time));
        Decision = new JevDecisionModel(_http, config.Models.Decision, new ModelHealth(time));
        // A missing key is known without a call; the decision model is paid, so it is never probed.
        if (JevDecisionModel.Key(config.Models.Decision) is null)
            Decision.Health.Failed();
    }

    public EmbeddingModel Embedding { get; }

    public RerankerModel Reranker { get; }

    public IDecisionModel Decision { get; }

    /// <summary>The models whose last call failed, by role name.</summary>
    public IReadOnlyList<string> Unavailable =>
        new[] { ("embedding", Embedding.Health), ("reranker", Reranker.Health), ("decision", Decision.Health) }
            .Where(model => model.Item2.Available == false)
            .Select(model => model.Item1)
            .ToList();

    public void Dispose() => _http.Dispose();
}
