namespace TacitMemory.Core.Checkup;

/// <summary>
/// The judgments of one category that were not answered, counted by reason. Each category words its own status lines,
/// and only the lines for the reasons that occurred are written.
/// </summary>
internal sealed record Unanswered(int ModelUnavailable, int OutOfBudget)
{
    public static Unanswered Of(IEnumerable<UncheckedReason?> reasons)
    {
        var all = reasons.ToList();
        return new Unanswered(all.Count(reason => reason == UncheckedReason.ModelUnavailable), all.Count(reason => reason == UncheckedReason.Budget));
    }

    /// <summary>The status lines, the model-unavailable one first; <c>null</c> when every judgment was answered.</summary>
    public string? Status(Func<int, string> modelUnavailable, Func<int, string> outOfBudget)
    {
        var lines = new List<string>();
        if (ModelUnavailable > 0)
            lines.Add(modelUnavailable(ModelUnavailable));
        if (OutOfBudget > 0)
            lines.Add(outOfBudget(OutOfBudget));
        return lines.Count == 0 ? null : string.Join('\n', lines);
    }
}
