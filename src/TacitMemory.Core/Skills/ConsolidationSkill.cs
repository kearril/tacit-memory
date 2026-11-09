using System.Text;
using TacitMemory.Core.Consolidation;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Skills;

/// <summary>
/// The <c>tacit-consolidate</c> memory command: the opening, the six steps, the structure words of the memory language, and
/// examples of a scope block, a candidate block, a rejection list entry and summary lines. The facts of the moment (the
/// repository's path, today's date, the baseline and the report) are not here: the agent reads them from the consolidation
/// report resource. The report's category titles come from <see cref="ConsolidationTitles"/>, the definition the report uses.
/// </summary>
internal static class ConsolidationSkill
{
    public const string Name = "tacit-consolidate";

    /// <summary>The consolidation report resource the skill sends the agent to read first.</summary>
    public const string ReportUri = "tacit://consolidation-report";

    private const string Description =
        "Run a consolidation of the TacitMemory memory: write summaries, propose changes for the user to confirm in one list, and commit.";

    /// <summary>Stands for the repository's absolute path in the steps.</summary>
    private const string Repository = "<repository>";

    /// <summary>Placeholders for the examples: a date, an ISO week, a month and a domain label.</summary>
    private const string Date = "YYYY-MM-DD";
    private const string Week = "YYYY-Www";
    private const string Month = "YYYY-MM";
    private const string ExampleDomain = "network-proxy";

    /// <summary>The rejection example's text and the user's words, in each memory language.</summary>
    private static readonly Localized ExampleText = new("回答尽量简短", "Keep answers short");
    private static readonly Localized ExampleQuote = new("别写那么长，直接说结论", "Shorter, please, just the conclusion");

    /// <summary>The scope examples: a candidate scoped to a domain, the rule it becomes, and the domain's trigger.</summary>
    private static readonly Localized ExampleScopedText = new("单次测速不作结论", "A single speed test is no conclusion");
    private static readonly Localized ExampleScopedQuote = new("别拿单次测速下结论", "Do not draw conclusions from one speed test");
    private static readonly Localized ExampleComparison = new("性能比较", "Performance comparisons");
    private static readonly Localized ExampleTrigger = new("改代理、分流或测速时", "When changing the proxy, routing or speed tests");

    /// <summary>The structure words the files use, by the role each plays.</summary>
    private static readonly (string Role, Localized Word)[] Words =
    [
        ("Red lines section (persona.md)", StructureWords.RedLines),
        ("Working rules section (persona.md)", StructureWords.WorkingRules),
        ("Preferences section (persona.md)", StructureWords.Preferences),
        ("Profile section (persona.md)", StructureWords.Profile),
        ("Domain heading (persona.md scope block)", StructureWords.Domain),
        ("Project heading (persona.md scope block)", StructureWords.Project),
        ("Trigger line of a scope block", StructureWords.Trigger),
        ("Candidates section (candidates.md)", StructureWords.Candidates),
        ("Rejection list section (candidates.md)", StructureWords.RejectionList),
        ("Belongs-to line of a candidate", StructureWords.BelongsTo),
        ("Scope line of a candidate or rejection", StructureWords.Scope),
        ("Quote of the user's own words", StructureWords.Quote),
        ("Rejected mark on an occurrence line", StructureWords.Rejected),
        ("\"Instead\" in a red line", StructureWords.Instead),
        ("Mainline section (recent.md)", StructureWords.Mainline),
        ("Summary section (month file)", StructureWords.Summary),
        ("Leftovers section (work page)", StructureWords.Leftovers),
        ("Overturned section (decision page)", StructureWords.Overturned),
        ("Lesson section (incident page)", StructureWords.Lesson),
    ];

    /// <summary>The whole file. It depends on the code and the memory language only.</summary>
    public static string Compose(MemoryLanguage language) => CommandSkill.Compose(Name, Description, Body(language));

    private static string Body(MemoryLanguage language)
    {
        var quote = StructureWords.Quote.In(language);
        var summary = StructureWords.Summary.In(language);
        var colon = language == MemoryLanguage.Zh ? "：" : ": ";
        var text = new StringBuilder();
        text.Append("# Memory consolidation\n\n");
        text.Append("Start the TacitMemory consolidation now.\n").Append(CommandSkill.UserText)
            .Append(" For example, \"only summaries\" runs the summary part of step 1 and no proposals.\n\n");
        text.Append("First read the resource ").Append(ReportUri).Append(" from the MCP server named tacit. It is computed when read and gives ")
            .Append("the memory repository's absolute path, today's date, the baseline commit, the budget and the report's categories. ")
            .Append("If it cannot be read, stop and ask the user to start the TacitMemory service with `tacit serve`; do not check the memory from what you remember.\n");
        text.Append("Edit the repository's files by the absolute path the report gives, and run git in that directory (git -C \"")
            .Append(Repository).Append("\" ...), whatever the current directory is. Below, ").Append(Repository).Append(" stands for that path.\n\n");

        text.Append("## Steps\n");
        text.Append("1. Part \"").Append(ConsolidationTitles.AutomaticPart).Append("\": do these without asking the user.\n");
        text.Append("   - \"").Append(ConsolidationTitles.FormatViolations).Append("\": fix each item that says it is fixed directly; an item that only gives a hint needs no change.\n");
        text.Append("   - \"").Append(ConsolidationTitles.RedundantCandidates).Append("\": delete each listed candidate from candidates.md.\n");
        text.Append("   - \"").Append(ConsolidationTitles.WeeksToSummarize).Append("\" and \"").Append(ConsolidationTitles.MonthsToSummarize)
            .Append("\": write one summary line per listed period into the `## ").Append(summary).Append("` section of the month file the report names: ")
            .Append("a week as `- ").Append(Week).Append("：<one line>`, a month as `- ").Append(Month).Append("：<one line>`. ")
            .Append("Write each line from the activities the report gives, and add nothing they do not say.\n");
        text.Append("   - \"").Append(ConsolidationTitles.MainlineMaterial).Append("\": rewrite the mainline in recent.md from the summarized weeks it lists and the current mainline, within the limit the report gives.\n");
        text.Append("   Change nothing else in this step: no persona entry, scope block or page changes here.\n");
        text.Append("2. Read what Part \"").Append(ConsolidationTitles.ProposalPart).Append("\" needs: the pages, persona entries and material its items name. Change nothing in this step.\n");
        text.Append("3. Draft every proposal and send them to the user in one numbered list. Cover each item under \"").Append(ConsolidationTitles.ProposalPart).Append("\":\n");
        text.Append("   - \"").Append(ConsolidationTitles.PersonaEvidence).Append("\": each candidate becomes a persona proposal that names its section and its scope. ")
            .Append("Give a preference the global scope only when it holds in at least two different situations (the generalization test); otherwise propose a domain scope with a trigger line, or a project scope.\n");
        text.Append("   - \"").Append(ConsolidationTitles.IncidentLessons).Append("\": propose a domain rule where a lesson or a preference belongs to one domain.\n");
        text.Append("   - \"").Append(ConsolidationTitles.ChangedPages).Append("\" and \"").Append(ConsolidationTitles.DuplicatePages)
            .Append("\": propose a type for each page without one, a split for an entity page, a pointer in place of text that restates the environment, and a merge of duplicate pages.\n");
        text.Append("   - \"").Append(ConsolidationTitles.ConflictingEntries).Append("\" and \"").Append(ConsolidationTitles.SectionsOverLimit)
            .Append("\": propose the move, rewrite or merge that resolves each.\n");
        text.Append("   Give each item a number, what would change, and its evidence (a short code, a quote or a page identifier). ")
            .Append("Close the list with how to reply, for example `1 2 4 confirm, 3 reject, 5 defer, 6 change to project scope`. An item the reply does not mention is deferred.\n");
        text.Append("4. Carry out the reply item by item. Changes to persona entries, scope blocks and pages happen only after the user confirms the item:\n");
        text.Append("   - confirm: write the item as proposed. A persona entry goes into its section or scope block, with an indented line below it `<!-- ")
            .Append(Date).Append(' ').Append(quote).Append("「…」 -->` holding today's date and the user's words; a candidate is then deleted from candidates.md. A page change is applied.\n");
        text.Append("   - reject: for a candidate, move it into the rejection list, with its scope line if it has one, and add an occurrence line marked as rejected, dated today. For any other item, leave it unchanged.\n");
        text.Append("   - change: apply the version the user gave, and treat it as confirmed.\n");
        text.Append("   - defer, or no reply: leave the item exactly as it is; it is reported again at the next consolidation.\n");
        text.Append("5. Show the user the summaries you wrote and the new mainline.\n");
        text.Append("6. Commit in the memory repository with a message that starts with `consolidate:`: `git -C \"").Append(Repository)
            .Append("\" add -A`, then `git -C \"").Append(Repository).Append("\" commit -m \"consolidate: ...\"`. If nothing changed, commit with `--allow-empty`: `git -C \"")
            .Append(Repository).Append("\" commit --allow-empty -m \"consolidate: no changes\"`.\n\n");

        text.Append("## Structure words\n");
        text.Append("The memory language is ").Append(language == MemoryLanguage.Zh ? "Chinese" : "English")
            .Append(". Write these words exactly as they appear here.\n\n");
        text.Append("| Role | Word |\n| :--- | :--- |\n");
        foreach (var (role, word) in Words)
            text.Append("| ").Append(role).Append(" | ").Append(word.In(language).TrimEnd()).Append(" |\n");
        text.Append('\n');

        text.Append("In the examples, ").Append(Date).Append(" stands for the date, ").Append(Week).Append(" for an ISO week and ")
            .Append(Month).Append(" for a month.\n\n");

        text.Append("Example of a scope block in persona.md:\n");
        text.Append("```\n");
        text.Append("## ").Append(StructureWords.Domain.In(language)).Append(colon).Append(ExampleDomain).Append('\n');
        text.Append(StructureWords.Trigger.In(language)).Append(colon).Append(ExampleTrigger.In(language)).Append('\n');
        text.Append("### ").Append(StructureWords.WorkingRules.In(language)).Append('\n');
        text.Append("- ").Append(ExampleComparison.In(language)).Append(" → ").Append(ExampleScopedText.In(language)).Append('\n');
        text.Append("  <!-- ").Append(Date).Append(' ').Append(quote).Append("「…」 -->\n");
        text.Append("```\n\n");

        text.Append("Example of a candidate block in candidates.md, scoped to a domain:\n");
        text.Append("```\n");
        text.Append("## ").Append(StructureWords.Candidates.In(language)).Append('\n');
        text.Append("### ").Append(ExampleScopedText.In(language)).Append('\n');
        text.Append(StructureWords.BelongsTo.In(language)).Append(StructureWords.Preferences.In(language)).Append('\n');
        text.Append(StructureWords.Scope.In(language)).Append("domain:").Append(ExampleDomain).Append('\n');
        text.Append("- ").Append(Date).Append(' ').Append(quote).Append('「').Append(ExampleScopedQuote.In(language)).Append("」\n");
        text.Append("```\n\n");

        text.Append("Example of an entry in the rejection list of candidates.md:\n");
        text.Append("```\n");
        text.Append("## ").Append(StructureWords.RejectionList.In(language)).Append('\n');
        text.Append("### ").Append(ExampleText.In(language)).Append('\n');
        text.Append(StructureWords.BelongsTo.In(language)).Append(StructureWords.Preferences.In(language)).Append('\n');
        text.Append("- ").Append(Date).Append(' ').Append(StructureWords.Rejected.In(language)).Append(' ')
            .Append(quote).Append('「').Append(ExampleQuote.In(language)).Append("」\n");
        text.Append("```\n\n");

        text.Append("Example of summary lines at the end of a month file:\n");
        text.Append("```\n");
        text.Append("## ").Append(summary).Append('\n');
        text.Append("- ").Append(Week).Append("：<one line>\n");
        text.Append("- ").Append(Month).Append("：<one line>\n");
        text.Append("```");

        return text.ToString();
    }
}
