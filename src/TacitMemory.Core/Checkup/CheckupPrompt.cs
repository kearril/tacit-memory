using System.Globalization;
using System.Text;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Checkup;

/// <summary>
/// The text of the <c>checkup</c> prompt: the opening with the repository's absolute path, the steps, the structure
/// words of the memory language with examples of a candidate block and a rejection list entry, and the checkup report.
/// Everything is computed from the repository as it is when the prompt is asked for.
/// </summary>
internal static class CheckupPrompt
{
    /// <summary>The example's text: a preference written into candidates.md, in each memory language.</summary>
    private static readonly Localized ExampleText = new("回答尽量简短", "Keep answers short");

    /// <summary>The example's quote: the user's own words, in each memory language.</summary>
    private static readonly Localized ExampleQuote = new("别写那么长，直接说结论", "Shorter, please, just the conclusion");

    /// <summary>The structure words the files use, by the role each plays.</summary>
    private static readonly (string Role, Localized Word)[] Words =
    [
        ("Red lines section (persona.md)", StructureWords.RedLines),
        ("Working rules section (persona.md)", StructureWords.WorkingRules),
        ("Preferences section (persona.md)", StructureWords.Preferences),
        ("Profile section (persona.md)", StructureWords.Profile),
        ("Candidates section (candidates.md)", StructureWords.Candidates),
        ("Rejection list section (candidates.md)", StructureWords.RejectionList),
        ("Belongs-to line of a candidate", StructureWords.BelongsTo),
        ("Quote of the user's own words", StructureWords.Quote),
        ("Rejected mark on an occurrence line", StructureWords.Rejected),
        ("\"Instead\" in a red line", StructureWords.Instead),
        ("Mainline section (recent.md)", StructureWords.Mainline),
    ];

    public static async Task<string> RenderAsync(TacitOptions options, TacitConfig config, CheckupServices services, CancellationToken cancellationToken)
    {
        var language = config.Language;
        var path = config.MemoryPath;
        var today = options.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var report = await CheckupReport.BuildAsync(config, services, options.Today, cancellationToken);

        var text = new StringBuilder();
        text.Append("Start the TacitMemory checkup now.\n");
        text.Append("The memory repository is at ").Append(path).Append(". Edit its files by that absolute path, and run git in that directory (git -C \"").Append(path).Append("\" ...), whatever the current directory is.\n\n");

        text.Append("## Steps\n");
        text.Append("1. Fix the items under \"").Append(CheckupTitles.FormatViolations).Append("\" that say they are fixed directly, without asking the user. Items that only give a hint need no change.\n");
        text.Append("2. Delete every item under \"").Append(CheckupTitles.RedundantCandidates).Append("\" directly, without asking: the persona or the rejection list already holds the same preference.\n");
        text.Append("3. For each item under \"").Append(CheckupTitles.ConfirmableCandidates).Append("\", ask the user one at a time, and accept \"confirm\", \"reject\" or \"defer\":\n");
        text.Append("   - confirm: move the candidate into its persona section as an entry, its text as written, and below it an indented comment line `<!-- ")
            .Append(today).Append(' ').Append(StructureWords.Quote.In(language)).Append("「quote」 -->` with today's date and the quote; then delete the candidate from candidates.md;\n");
        text.Append("   - reject: move the candidate into the rejection list in candidates.md, and add an occurrence line marked as rejected, dated today;\n");
        text.Append("   - defer: leave the candidate exactly as it is; it is reported again at the next checkup.\n");
        text.Append("4. For each item under \"").Append(CheckupTitles.DuplicatePages).Append("\", \"").Append(CheckupTitles.ConflictingEntries)
            .Append("\", and each section over its limit, ask the user one at a time. Merge pages or rewrite entries only after the user agrees.\n");
        text.Append("5. If the mainline is stale, or over its limit, rewrite the mainline in recent.md from the activities of the last month.\n");
        text.Append("6. Review the whole persona for duplicate entries and entries worded almost alike. Propose each merge to the user, and apply it only after the user confirms.\n");
        text.Append("7. Commit in the memory repository with a message that starts with `checkup:`: `git -C \"").Append(path)
            .Append("\" add -A`, then `git -C \"").Append(path).Append("\" commit -m \"checkup: ...\"`. If nothing changed, commit with `--allow-empty`: `git -C \"")
            .Append(path).Append("\" commit --allow-empty -m \"checkup: no changes\"`.\n\n");

        text.Append("## Structure words\n");
        text.Append("The memory language is ").Append(language == MemoryLanguage.Zh ? "Chinese" : "English")
            .Append(". Write these words exactly as they appear here.\n\n");
        text.Append("| Role | Word |\n| :--- | :--- |\n");
        foreach (var (role, word) in Words)
            text.Append("| ").Append(role).Append(" | ").Append(word.In(language).TrimEnd()).Append(" |\n");
        text.Append('\n');

        text.Append("Example of a candidate block in candidates.md:\n");
        text.Append("```\n");
        text.Append("## ").Append(StructureWords.Candidates.In(language)).Append('\n');
        text.Append("### ").Append(ExampleText.In(language)).Append('\n');
        text.Append(StructureWords.BelongsTo.In(language)).Append(StructureWords.Preferences.In(language)).Append('\n');
        text.Append("- ").Append(today).Append(' ').Append(StructureWords.Quote.In(language)).Append('「').Append(ExampleQuote.In(language)).Append("」\n");
        text.Append("```\n\n");

        text.Append("Example of an entry in the rejection list of candidates.md:\n");
        text.Append("```\n");
        text.Append("## ").Append(StructureWords.RejectionList.In(language)).Append('\n');
        text.Append("### ").Append(ExampleText.In(language)).Append('\n');
        text.Append(StructureWords.BelongsTo.In(language)).Append(StructureWords.Preferences.In(language)).Append('\n');
        text.Append("- ").Append(today).Append(' ').Append(StructureWords.Rejected.In(language)).Append(' ')
            .Append(StructureWords.Quote.In(language)).Append('「').Append(ExampleQuote.In(language)).Append("」\n");
        text.Append("```\n\n");

        text.Append("## Checkup report\n");
        text.Append(report.Render());

        return text.ToString();
    }
}
