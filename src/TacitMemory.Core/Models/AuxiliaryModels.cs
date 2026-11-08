using TacitMemory.Core.Logging;
using TacitMemory.Core;

namespace TacitMemory.Core.Models;

/// <summary>The auxiliary models the memory service calls, sharing one HTTP client and one retry policy.</summary>
internal sealed class AuxiliaryModels : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public AuxiliaryModels(TacitConfig config, TimeProvider time, ServiceLog log)
    {
        var client = new ModelClient(_http, time);
        Embedding = new EmbeddingModel(client, config.Models.Embedding, new ModelHealth(time, "embedding", log));
        Reranker = new RerankerModel(client, config.Models.Reranker, new ModelHealth(time, "reranker", log));
        Decision = new SystemOneDecisionModel(client, config.Models.Decision, new ModelHealth(time, "decision", log));
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

    /// <summary>Each model's state, by role name, as the status report shows it.</summary>
    public IReadOnlyList<ModelStatus> Statuses =>
        [Describe("embedding", Embedding.Health), Describe("reranker", Reranker.Health), Describe("decision", Decision.Health)];

    private static ModelStatus Describe(string role, ModelHealth health)
    {
        if (!health.CoolingDown)
            return new ModelStatus(role, ModelState.Available, null);
        var seconds = health.CooldownRemaining?.TotalSeconds ?? 0;
        return new ModelStatus(role, ModelState.CoolingDown, (int)Math.Ceiling(seconds));
    }

    public void Dispose() => _http.Dispose();
}
