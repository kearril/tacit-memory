namespace TacitMemory.Core.Decisions;

/// <summary>How sure a judgment is.</summary>
internal enum Tier
{
    /// <summary>Yes: act on it.</summary>
    Certain,

    /// <summary>Not sure either way: let it through and leave it to the consolidation.</summary>
    Uncertain,

    /// <summary>No.</summary>
    Unlikely,
}

/// <summary>The outcome of a choice question: its tier and, when not <see cref="Tier.Unlikely"/>, the chosen option.</summary>
internal sealed record ChoiceVerdict(Tier Tier, string? Option);

/// <summary>Turns the decision model's probabilities into tiers using a judgment's two thresholds.</summary>
internal static class Judgment
{
    /// <summary>Defaults for same-preference checks, measured on Chinese persona-write samples with a System One model; <c>config.yaml</c> may override them.</summary>
    public static readonly Threshold SamePreference = new(Certain: 0.85, Unlikely: 0.25);

    /// <summary>Defaults measured on real Chinese samples with a System One model; <c>config.yaml</c> may override them.</summary>
    public static readonly Threshold SamePage = new(Certain: 0.85, Unlikely: 0.25);

    /// <summary>Defaults for same-topic activity checks: the values of <c>samePage</c>, measured on Chinese same-day samples; <c>config.yaml</c> may override them.</summary>
    public static readonly Threshold SameTopic = new(Certain: 0.85, Unlikely: 0.25);

    /// <summary>Defaults for rule-conflict checks on the persona file, measured on Chinese persona-write samples with a System One model; <c>config.yaml</c> may override them.</summary>
    public static readonly Threshold RuleConflict = new(Certain: 0.85, Unlikely: 0.25);

    /// <summary>
    /// A choice among options plus a "none of these" option: none ranked first is <see cref="Tier.Unlikely"/>;
    /// otherwise the best real option at or above <c>certain</c> is <see cref="Tier.Certain"/>, at or below
    /// <c>unlikely</c> is <see cref="Tier.Unlikely"/>, and anything between is <see cref="Tier.Uncertain"/>.
    /// </summary>
    public static ChoiceVerdict Choice(IReadOnlyDictionary<string, double> probabilities, string noneOption, Threshold threshold)
    {
        var best = probabilities.Where(option => option.Key != noneOption).MaxBy(option => option.Value);
        if (best.Key is null || probabilities.GetValueOrDefault(noneOption) >= best.Value)
            return new ChoiceVerdict(Tier.Unlikely, null);
        if (best.Value >= threshold.Certain)
            return new ChoiceVerdict(Tier.Certain, best.Key);
        return best.Value <= threshold.Unlikely ? new ChoiceVerdict(Tier.Unlikely, null) : new ChoiceVerdict(Tier.Uncertain, best.Key);
    }
}
