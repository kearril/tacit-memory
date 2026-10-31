namespace TacitMemory.Core.Tools;

/// <summary>The shape of tool replies. Agent-facing, so always English.</summary>
internal static class ToolText
{
    public static ToolResult Ok(string text) => new(text.TrimEnd(), IsError: false);

    /// <summary>
    /// A refusal in three parts: why, what the agent can do next, and the content it needs for that.
    /// Several reasons are listed one per line.
    /// </summary>
    public static ToolResult Refuse(IReadOnlyList<string> reasons, string nextStep, string? related = null)
    {
        var text = reasons.Count == 1 ? $"Refused: {reasons[0]}" : "Refused:\n" + string.Join('\n', reasons.Select(reason => $"- {reason}"));
        text += $"\nNext step: {nextStep}";
        if (!string.IsNullOrWhiteSpace(related))
            text += $"\nRelated:\n{related.TrimEnd()}";
        return new ToolResult(text, IsError: true);
    }

    public static ToolResult Refuse(string reason, string nextStep, string? related = null) => Refuse([reason], nextStep, related);

    public static string Quote(string value) => $"\"{value}\"";
}
