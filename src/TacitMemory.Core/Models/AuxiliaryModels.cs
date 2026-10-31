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
    }

    public EmbeddingModel Embedding { get; }

    public RerankerModel Reranker { get; }
    public IDecisionModel Decision { get; }

    public void Dispose() => _http.Dispose();
}
