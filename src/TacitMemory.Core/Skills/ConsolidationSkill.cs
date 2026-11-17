using System.Text;
using TacitMemory.Core.Consolidation;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Skills;

/// <summary>
/// The <c>tacit-consolidate</c> memory command: the opening, the six steps, and examples of a scope block, a candidate
/// block, a rejection list entry, a workstream entry and summary lines, which show the structure words. The facts of the moment (the
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

    /// <summary>The rejection example's text and the user's words.</summary>
    private const string ExampleText = "Keep answers short";
    private const string ExampleQuote = "Shorter, please, just the conclusion";

    /// <summary>The scope examples: a candidate scoped to a domain, the rule it becomes, and the domain's trigger.</summary>
    private const string ExampleScopedText = "A single speed test is no conclusion";
    private const string ExampleScopedQuote = "Do not draw conclusions from one speed test";
    private const string ExampleComparison = "Performance comparisons";
    private const string ExampleTrigger = "When changing the proxy, routing or speed tests";

    /// <summary>The workstream example: its name, its background line and its recent line, which states the phase, not the list of work done.</summary>
    private const string ExampleWorkstream = "home-server";
    private const string ExampleBackground = "The home server that runs proxy routing and self-hosted services.";
    private const string ExampleRecent = "The service stack is set up and is being trimmed: rarely used services are being removed.";

    /// <summary>The whole file. It depends on the code only.</summary>
    public static string Compose() => CommandSkill.Compose(Name, Description, Body());

    private static string Body()
    {
        var quote = StructureWords.Quote;
        var summary = StructureWords.Summary;
        var text = new StringBuilder();
        text.Append("# Memory consolidation\n\n");
        text.Append("Start the TacitMemory consolidation now.\n").Append(CommandSkill.UserText)
            .Append(" For example, \"only summaries\" runs the summary part of step 1 and no proposals. \"only workstreams\", or \"only workstream home-server\" ")
            .Append("for one named workstream, runs just the workstream material of step 1 and the workstream proposal material of step 3, with the workstream changes of step 4. ")
            .Append("The pass still ends with the `consolidate:` commit of step 6.\n\n");
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
        text.Append("   - \"").Append(ConsolidationTitles.WorkstreamMaterial).Append("\": for each workstream the report lists, write only its recent line in workstreams.md (adding one when the entry has none), ")
            .Append("from the activities the report gives, and leave its background line unchanged. The background line says what the workstream is. ")
            .Append("The recent line is a high-level summary of its recent phase and direction: where it stands and where it is going. It is never a list of what was done. ")
            .Append("A workstream the report does not list stays as it is.\n");
        text.Append("   Change nothing else in this step: no persona entry, scope block or page changes here.\n");
        text.Append("2. Read what Part \"").Append(ConsolidationTitles.ProposalPart).Append("\" needs: the pages, persona entries and material its items name. Change nothing in this step.\n");
        text.Append("3. Draft every proposal and send them to the user in one numbered list. Cover each item under \"").Append(ConsolidationTitles.ProposalPart).Append("\":\n");
        text.Append("   - \"").Append(ConsolidationTitles.PersonaEvidence).Append("\": each candidate becomes a persona proposal that names its section and its scope. ")
            .Append("Give a preference the global scope only when it holds in at least two different situations (the generalization test); otherwise propose a domain scope with a trigger line, or a workstream scope.\n");
        text.Append("   - \"").Append(ConsolidationTitles.IncidentLessons).Append("\": propose a domain rule where a lesson or a preference belongs to one domain.\n");
        text.Append("   - \"").Append(ConsolidationTitles.ChangedPages).Append("\" and \"").Append(ConsolidationTitles.DuplicatePages)
            .Append("\": propose a type for each page without one, a split for an entity page, a pointer in place of text that restates the environment, and a merge of duplicate pages.\n");
        text.Append("   - \"").Append(ConsolidationTitles.ConflictingEntries).Append("\" and \"").Append(ConsolidationTitles.DomainHygiene)
            .Append("\": propose the move, rewrite or merge that resolves each.\n");
        text.Append("   - \"").Append(ConsolidationTitles.LengthHints)
            .Append("\": propose a shorter wording for each item the report lists, keeping its meaning and its source; the user confirms before it is written.\n");
        text.Append("   - \"").Append(ConsolidationTitles.WorkstreamProposals)
            .Append("\": draft one proposal per workstream change: a new workstream (its name, a background line, a recent line, and the detail pages it takes into its folder), ")
            .Append("an unestablished name merged into an existing workstream or its label dropped, a split of a workstream, or a merge of two. ")
            .Append("Each proposal names the pages it moves. Topic pages under a listed prefix may form a new workstream when they belong together; otherwise they stay in topics.\n");
        text.Append("   Give each item a number, what would change, and its evidence (a short code, a quote or a page identifier). ")
            .Append("Close the list with how to reply, for example `1 2 4 confirm, 3 reject, 5 defer, 6 change to workstream scope`. An item the reply does not mention is deferred.\n");
        text.Append("4. Carry out the reply item by item. Changes to persona entries, scope blocks and pages happen only after the user confirms the item:\n");
        text.Append("   - confirm: write the item as proposed. A persona entry goes into its section or scope block, with an indented line below it `<!-- ")
            .Append(Date).Append(' ').Append(quote).Append("「…」 -->` holding today's date and the user's words; a candidate is then deleted from candidates.md. A page change is applied.\n");
        text.Append("   - reject: for a candidate, move it into the rejection list, with its scope line if it has one, and add an occurrence line marked as rejected, dated today. For any other item, leave it unchanged.\n");
        text.Append("   - change: apply the version the user gave, and treat it as confirmed.\n");
        text.Append("   - defer, or no reply: leave the item exactly as it is. A candidate stays in candidates.md and comes back at the next consolidation; any other item comes back only when it changes again.\n");
        text.Append("   - a confirmed workstream item is carried out by editing the entries in workstreams.md (add, remove or change one), moving the detail pages into their new folder and fixing the related lines of other pages that name them. ")
            .Append("For a merge, also move the entries of the merged workstream's scope block in persona.md into the target's block and set their scope to the target. ")
            .Append("Leave the labels of past activities as they are.\n");
        text.Append("5. Show the user the summaries you wrote and the workstream entries you changed.\n");
        text.Append("6. Commit in the memory repository with a message that starts with `consolidate:`: `git -C \"").Append(Repository)
            .Append("\" add -A`, then `git -C \"").Append(Repository).Append("\" commit -m \"consolidate: ...\"`. If nothing changed, commit with `--allow-empty`: `git -C \"")
            .Append(Repository).Append("\" commit --allow-empty -m \"consolidate: no changes\"`.\n\n");

        text.Append("## Examples\n");
        text.Append("The examples show the structure words of the memory files: write them exactly as they appear here, in English, whatever the memory language. ")
            .Append("Write the content (entries, titles, quotes, background and recent lines, summaries) in the memory language the server instructions name.\n");
        text.Append("In the examples, ").Append(Date).Append(" stands for the date, ").Append(Week).Append(" for an ISO week and ")
            .Append(Month).Append(" for a month.\n\n");

        text.Append("Example of a scope block in persona.md:\n");
        text.Append("```\n");
        text.Append("## ").Append(StructureWords.Field(StructureWords.Domain, ExampleDomain)).Append('\n');
        text.Append(StructureWords.Field(StructureWords.Trigger, ExampleTrigger)).Append('\n');
        text.Append("### ").Append(StructureWords.WorkingRules).Append('\n');
        text.Append("- ").Append(ExampleComparison).Append(" → ").Append(ExampleScopedText).Append('\n');
        text.Append("  <!-- ").Append(Date).Append(' ').Append(quote).Append("「…」 -->\n");
        text.Append("```\n\n");

        text.Append("Example of a candidate block in candidates.md, scoped to a domain:\n");
        text.Append("```\n");
        text.Append("## ").Append(StructureWords.Candidates).Append('\n');
        text.Append("### ").Append(ExampleScopedText).Append('\n');
        text.Append(StructureWords.Field(StructureWords.BelongsTo, StructureWords.Preferences)).Append('\n');
        text.Append(StructureWords.Field(StructureWords.Scope, "domain:" + ExampleDomain)).Append('\n');
        text.Append("- ").Append(Date).Append(' ').Append(quote).Append('「').Append(ExampleScopedQuote).Append("」\n");
        text.Append("```\n\n");

        text.Append("Example of an entry in the rejection list of candidates.md:\n");
        text.Append("```\n");
        text.Append("## ").Append(StructureWords.RejectionList).Append('\n');
        text.Append("### ").Append(ExampleText).Append('\n');
        text.Append(StructureWords.Field(StructureWords.BelongsTo, StructureWords.Preferences)).Append('\n');
        text.Append("- ").Append(Date).Append(' ').Append(StructureWords.Rejected).Append(' ')
            .Append(quote).Append('「').Append(ExampleQuote).Append("」\n");
        text.Append("```\n\n");

        text.Append("Example of a workstream entry in workstreams.md. The background line says what the workstream is; the recent line says where it stands and where it is going:\n");
        text.Append("```\n");
        text.Append("## ").Append(StructureWords.Workstream).Append('\n');
        text.Append("### ").Append(ExampleWorkstream).Append('\n');
        text.Append(StructureWords.Field(StructureWords.Background, ExampleBackground)).Append('\n');
        text.Append(StructureWords.Field(StructureWords.Recent, ExampleRecent)).Append('\n');
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
