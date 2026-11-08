using TacitMemory.Core.Logging;
using TacitMemory.Core;

namespace TacitMemory.Core.Models;

/// <summary>The auxiliary models the memory service calls, sharing one HTTP client and one retry policy.</summary>
internal sealed class AuxiliaryModels : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly ModelsConfig _models;

    public AuxiliaryModels(TacitConfig config, TimeProvider time, ServiceLog log)
    {
        _models = config.Models;
        var client = new ModelClient(_http, time);
        Embedding = new EmbeddingModel(client, config.Models.Embedding, new ModelHealth(time, ModelRoles.Name(ModelRole.Embedding), log));
        Reranker = new RerankerModel(client, config.Models.Reranker, new ModelHealth(time, ModelRoles.Name(ModelRole.Reranker), log));
        Decision = new SystemOneDecisionModel(client, config.Models.Decision, new ModelHealth(time, ModelRoles.Name(ModelRole.Decision), log));
    }

    public EmbeddingModel Embedding { get; }

    public RerankerModel Reranker { get; }

    public IDecisionModel Decision { get; }

    /// <summary>The configured models cooling down after a failed request, by role name. Unconfigured models are never listed.</summary>
    public IReadOnlyList<string> Unavailable =>
        ModelRoles.All.Where(role => HealthOf(role).CoolingDown).Select(ModelRoles.Name).ToList();

    /// <summary>The models that are not configured, by role name.</summary>
    public IReadOnlyList<string> Unconfigured => ModelRoles.Unconfigured(_models).Select(ModelRoles.Name).ToList();

    /// <summary>Each model's state, by role name, as the status report shows it.</summary>
    public IReadOnlyList<ModelStatus> Statuses => ModelRoles.All.Select(Describe).ToList();

    private ModelHealth HealthOf(ModelRole role) => role switch
    {
        ModelRole.Embedding => Embedding.Health,
        ModelRole.Reranker => Reranker.Health,
        _ => Decision.Health,
    };

    private ModelStatus Describe(ModelRole role)
    {
        var name = ModelRoles.Name(role);
        if (!ModelRoles.Configured(_models, role))
            return new ModelStatus(name, ModelState.Unconfigured, null);
        var health = HealthOf(role);
        if (!health.CoolingDown)
            return new ModelStatus(name, ModelState.Available, null);
        var seconds = health.CooldownRemaining?.TotalSeconds ?? 0;
        return new ModelStatus(name, ModelState.CoolingDown, (int)Math.Ceiling(seconds));
    }

    public void Dispose() => _http.Dispose();
}
