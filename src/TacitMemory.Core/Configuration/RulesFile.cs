using System.Text;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Setup;

namespace TacitMemory.Core.Configuration;

/// <summary>
/// <c>rules.md</c>: the "worth recording" and "not recorded" sections, each a list of top-level <c>- </c> items.
/// Headings are recognized in every language; other sections and continuation lines are ignored.
/// </summary>
internal static class RulesFile
{
    public const int MaxItemsPerSection = 10;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static RecordingCriteria Load(string tacitHome, MemoryLanguage language)
    {
        var path = Path.Combine(tacitHome, "rules.md");
        if (!File.Exists(path))
            File.WriteAllText(path, Templates.Rules(language), Utf8);

        var worth = new List<string>();
        var notRecorded = new List<string>();
        List<string>? section = null;
        foreach (var line in File.ReadLines(path, Utf8))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var heading = line[3..].Trim();
                section = Is(StructureWords.WorthRecording, heading) ? worth
                    : Is(StructureWords.NotRecorded, heading) ? notRecorded
                    : null;
            }
            else if (section is not null && line.StartsWith("- ", StringComparison.Ordinal) && line[2..].Trim() is { Length: > 0 } item)
            {
                section.Add(item);
            }
        }

        Check(worth, StructureWords.WorthRecording);
        Check(notRecorded, StructureWords.NotRecorded);
        return new RecordingCriteria(worth, notRecorded);

        void Check(List<string> items, Localized heading)
        {
            if (items.Count > MaxItemsPerSection)
                throw new TacitException(TacitError.RulesInvalid, Messages.RulesTooMany.Format(
                    language, path, heading.In(language), items.Count, MaxItemsPerSection, items.Count - MaxItemsPerSection));
        }
    }

    private static bool Is(Localized word, string heading) =>
        string.Equals(heading, word.Zh, StringComparison.Ordinal) || string.Equals(heading, word.En, StringComparison.OrdinalIgnoreCase);
}
