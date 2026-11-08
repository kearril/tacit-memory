namespace TacitMemory.Core;

/// <summary>
/// The auxiliary model roles: their order in every report, their names (the keys under <c>models</c> in config.yaml, used by
/// the status report and the probe), and the one rule that tells which of them the config leaves out.
/// </summary>
public static class ModelRoles
{
    /// <summary>Every role, in the order the reports list them.</summary>
    public static IReadOnlyList<ModelRole> All { get; } = [ModelRole.Embedding, ModelRole.Reranker, ModelRole.Decision];

    /// <summary>The role's name as config.yaml, the status report and the probe spell it.</summary>
    public static string Name(ModelRole role) => role switch
    {
        ModelRole.Embedding => "embedding",
        ModelRole.Reranker => "reranker",
        ModelRole.Decision => "decision",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    /// <summary>Whether the config gives the role a model. The only place that decides it.</summary>
    internal static bool Configured(ModelsConfig models, ModelRole role) => role switch
    {
        ModelRole.Embedding => models.Embedding is not null,
        ModelRole.Reranker => models.Reranker is not null,
        ModelRole.Decision => models.Decision is not null,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    /// <summary>The roles the config leaves out, in report order.</summary>
    internal static IReadOnlyList<ModelRole> Unconfigured(ModelsConfig models) =>
        All.Where(role => !Configured(models, role)).ToList();
}
