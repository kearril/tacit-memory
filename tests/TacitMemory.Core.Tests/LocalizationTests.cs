using System.Reflection;
using System.Text.RegularExpressions;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Pages;
using TacitMemory.Core.Show;

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
    public async Task Every_heading_init_writes_and_the_built_in_criteria_has_is_a_structure_word_of_that_language(MemoryLanguage language)
    {
        using var home = new TestHome();
        var result = await home.Tacit().InitAsync(new InitRequest { Language = language });
        var words = StructureWordsIn().Select(word => word.In(language)).ToHashSet();

        var files = new[] { "persona.md", "candidates.md", "workstreams.md" }.Select(name => Path.Combine(result.MemoryPath, name));
        var criteria = (await home.Tacit().ShowAsync(ShowTarget.Rules, useDefault: true)).Report.Split('\n');
        var headings = files.SelectMany(File.ReadLines).Concat(criteria).Where(line => line.StartsWith('#')).ToList();

        Assert.NotEmpty(headings);
        Assert.All(headings, heading => Assert.Contains(heading.TrimStart('#', ' '), words));
    }

    [Fact]
    public void The_page_parser_finds_the_leftovers_and_overturned_sections_in_both_languages()
    {
        var english = PageDocument.Parse("## Leftovers\n- a\n\n## Overturned\n- b\n", hasBom: false, "page");
        var chinese = PageDocument.Parse("## 遗留\n- a\n\n## 推翻记录\n- b\n", hasBom: false, "page");

        Assert.Equal("Leftovers", english.Leftovers?.Name);
        Assert.Equal("Overturned", english.Overturned?.Name);
        Assert.Equal("遗留", chinese.Leftovers?.Name);
        Assert.Equal("推翻记录", chinese.Overturned?.Name);
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
