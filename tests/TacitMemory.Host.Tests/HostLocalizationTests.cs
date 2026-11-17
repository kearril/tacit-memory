using TacitMemory.Core.Tests;
using TacitMemory.Host.Localization;

namespace TacitMemory.Host.Tests;

/// <summary>The host's own human-facing texts are checked as Core's are: both translations written, the same placeholders.</summary>
public sealed class HostLocalizationTests
{
    public static TheoryData<string> Texts() => [.. LocalizedTexts.In(typeof(HostMessages).Assembly).Select(entry => entry.Name)];

    [Theory]
    [MemberData(nameof(Texts))]
    public void Both_translations_are_present_and_take_the_same_placeholders(string name) =>
        LocalizedTexts.AssertTranslated(LocalizedTexts.In(typeof(HostMessages).Assembly).Single(entry => entry.Name == name).Text);
}
