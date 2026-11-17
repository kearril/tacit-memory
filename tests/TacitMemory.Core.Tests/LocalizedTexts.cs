using System.Reflection;
using System.Text.RegularExpressions;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Tests;

/// <summary>The <see cref="Localized"/> texts of an assembly, and the check each of them must pass. Shared with the host's tests.</summary>
internal static partial class LocalizedTexts
{
    /// <summary>Every <see cref="Localized"/> field of a type in <paramref name="assembly"/>, including private ones inside templates.</summary>
    public static IEnumerable<(string Name, Localized Text)> In(Assembly assembly) =>
        assembly.GetTypes()
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(field => field.FieldType == typeof(Localized) || field.FieldType == typeof(Localized[]))
                .SelectMany(field => field.GetValue(null) switch
                {
                    Localized one => [($"{type.Name}.{field.Name}", one)],
                    Localized[] many => many.Select((text, index) => ($"{type.Name}.{field.Name}[{index}]", text)),
                    _ => [],
                }));

    /// <summary>Both translations of <paramref name="text"/> are written and take the same format placeholders.</summary>
    public static void AssertTranslated(Localized text)
    {
        Assert.False(string.IsNullOrWhiteSpace(text.Zh));
        Assert.False(string.IsNullOrWhiteSpace(text.En));
        Assert.Equal(Placeholders(text.En), Placeholders(text.Zh));
    }

    private static string[] Placeholders(string text) =>
        [.. PlaceholderPattern().Matches(text).Select(match => match.Value).Distinct().Order()];

    [GeneratedRegex(@"\{\w+\}")]
    private static partial Regex PlaceholderPattern();
}
