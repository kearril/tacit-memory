using System.Reflection;
using System.Text.RegularExpressions;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Tests;

/// <summary>
/// Every human-facing text exists in both languages by construction (<see cref="Localized"/> takes both). What the
/// compiler cannot see is a translation that drops or invents a format placeholder, or a blank one.
/// </summary>
public sealed partial class LocalizationTests
{
    public static TheoryData<string> Texts() => [.. AllLocalized().Select(entry => entry.Name)];

    [Theory]
    [MemberData(nameof(Texts))]
    public void Both_translations_are_present_and_take_the_same_placeholders(string name)
    {
        var text = AllLocalized().Single(entry => entry.Name == name).Text;

        Assert.False(string.IsNullOrWhiteSpace(text.Zh));
        Assert.False(string.IsNullOrWhiteSpace(text.En));
        Assert.Equal(Placeholders(text.En), Placeholders(text.Zh));
    }

    [Fact]
    public void Structure_words_are_distinct_across_both_languages()
    {
        var words = StructureWordsIn().SelectMany(word => new[] { word.Zh.Trim(), word.En.Trim() }).ToList();

        Assert.Equal(words.Count, words.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData(MemoryLanguage.Zh)]
    [InlineData(MemoryLanguage.En)]
    public async Task Every_heading_init_writes_is_a_structure_word_of_that_language(MemoryLanguage language)
    {
        using var home = new TestHome();
        var result = await home.Tacit().InitAsync(new InitRequest { Language = language });
        var words = StructureWordsIn().Select(word => word.In(language)).ToHashSet();

        string[] files =
        [
            Path.Combine(home.TacitHome, "rules.md"),
            .. new[] { "persona.md", "candidates.md", "recent.md" }.Select(name => Path.Combine(result.MemoryPath, name)),
        ];
        var headings = files.SelectMany(File.ReadLines).Where(line => line.StartsWith('#')).ToList();

        Assert.NotEmpty(headings);
        Assert.All(headings, heading => Assert.Contains(heading.TrimStart('#', ' '), words));
    }

    private static IEnumerable<Localized> StructureWordsIn() =>
        typeof(StructureWords).GetFields(BindingFlags.Public | BindingFlags.Static).Select(field => (Localized)field.GetValue(null)!);

    /// <summary>Every <see cref="Localized"/> field of a type in Core, including private ones inside templates.</summary>
    private static IEnumerable<(string Name, Localized Text)> AllLocalized() =>
        typeof(Localized).Assembly.GetTypes()
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(field => field.FieldType == typeof(Localized) || field.FieldType == typeof(Localized[]))
                .SelectMany(field => field.GetValue(null) switch
                {
                    Localized one => [($"{type.Name}.{field.Name}", one)],
                    Localized[] many => many.Select((text, index) => ($"{type.Name}.{field.Name}[{index}]", text)),
                    _ => [],
                }));

    private static string[] Placeholders(string text) =>
        [.. PlaceholderPattern().Matches(text).Select(match => match.Value).Distinct().Order()];

    [GeneratedRegex(@"\{\w+\}")]
    private static partial Regex PlaceholderPattern();
}
