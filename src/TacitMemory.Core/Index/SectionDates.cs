using System.Globalization;

namespace TacitMemory.Core.Index;

/// <summary>How a section's last changed date appears in a result.</summary>
internal static class SectionDates
{
    /// <summary>The note that follows a section's heading, such as <c> (last changed 2026-11-02)</c>; empty when the date is unknown.</summary>
    public static string Note(IReadOnlyDictionary<string, DateOnly> changed, string section) =>
        changed.TryGetValue(section, out var date)
            ? $" (last changed {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)})"
            : "";
}
