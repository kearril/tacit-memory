using System.Text;
using TacitMemory.Core.Git;
using TacitMemory.Core.Index;
using TacitMemory.Core.Models;
using TacitMemory.Core.Recent;

namespace TacitMemory.Core.Consolidation;

/// <summary>What the categories read without a model: the config, today's date, the repository's git and the baseline.</summary>
internal sealed record ConsolidationContext(TacitConfig Config, DateOnly Today, GitCli Git, ConsolidationBaseline Baseline)
{
    private Task<IReadOnlyList<(RecentFile.Activity Activity, string? Comment)>>? _newActivities;

    /// <summary>The activities added since the baseline, read once and shared by every category of the report.</summary>
    public Task<IReadOnlyList<(RecentFile.Activity Activity, string? Comment)>> NewActivitiesAsync(CancellationToken cancellationToken) =>
        _newActivities ??= NewActivities.ReadAsync(this, cancellationToken);
}

/// <summary>What the categories that ask a model need: the index, the embedding model, the vector store and the judgment pool.</summary>
internal sealed record ConsolidationServices(MemoryIndex Index, EmbeddingModel Embedding, VectorSync Vectors, JudgmentPool Judgments);

/// <summary>
/// One category of the report: its findings, each already written for the agent, and an optional status line. Findings that
/// stand whatever the baseline are capped at <see cref="ConsolidationReport.MaxFindingsPerCategory"/>; the increments since
/// the baseline are all listed, after them, as a pass that leaves the baseline in place would otherwise see only the same
/// first ones every time.
/// </summary>
/// <param name="Findings">The findings that do not depend on the baseline; the ones past the cap are counted.</param>
/// <param name="Status">Why the category could not be fully checked, when it could not; shown above the findings.</param>
/// <param name="Increments">The findings about what changed since the baseline; every one is listed.</param>
internal sealed record ConsolidationSection(
    string Title, IReadOnlyList<string> Findings, string? Status = null, IReadOnlyList<string>? Increments = null)
{
    /// <summary>A category made only of increments since the baseline.</summary>
    public static ConsolidationSection OfIncrements(string title, IReadOnlyList<string> increments, string? status = null) =>
        new(title, [], status, increments);

    /// <summary>Whether the report lists only the first findings of this category.</summary>
    public bool CutShort => Findings.Count > ConsolidationReport.MaxFindingsPerCategory;
}

/// <summary>
/// A category whose judgments are already submitted: waiting for their answers as the budget allows, then its result.
/// </summary>
internal delegate Task<T> AwaitAnswers<T>();

/// <summary>
/// The consolidation report: the baseline, the budget's outcome, and the categories of its two parts, computed when asked for.
/// </summary>
/// <param name="BudgetSeconds">The budget of the model judgments, in seconds.</param>
/// <param name="BudgetRanOut">Whether the budget ran out before every judgment was answered.</param>
/// <param name="ModelUnavailable">Whether a configured model failed or was cooling down, so some judgments were not answered.</param>
/// <param name="Automatic">Part A: the categories the agent handles itself.</param>
/// <param name="Proposals">Part B: the categories the agent gathers into one numbered list for the user.</param>
internal sealed record ConsolidationReport(
    ConsolidationBaseline Baseline,
    int BudgetSeconds,
    bool BudgetRanOut,
    bool ModelUnavailable,
    IReadOnlyList<ConsolidationSection> Automatic,
    IReadOnlyList<ConsolidationSection> Proposals)
{
    /// <summary>Most findings a category lists that do not depend on the baseline; the rest are counted.</summary>
    public const int MaxFindingsPerCategory = 20;

    /// <summary>
    /// Builds the report from the memory repository as it is now. Nothing in the repository is written; the judgments may
    /// add answers to the judgment cache. The categories read the repository and submit their judgments first, and only
    /// then does the budget start, so it covers just the wait for the models.
    /// </summary>
    public static async Task<ConsolidationReport> BuildAsync(TacitConfig config, ConsolidationServices services, DateOnly today, CancellationToken cancellationToken)
    {
        var git = new GitCli(config.MemoryPath, config.Language);
        var context = new ConsolidationContext(config, today, git, await ConsolidationBaseline.ReadAsync(git, cancellationToken));
        using var judgments = services.Judgments.Begin(cancellationToken);

        var duplicatePages = await DuplicatePages.StartAsync(context, services, judgments, cancellationToken);
        var conflicts = await ConflictingEntries.StartAsync(context, services, judgments, cancellationToken);
        var candidates = await CandidateReview.StartAsync(context, services, judgments);
        judgments.StartBudget(TimeSpan.FromSeconds(config.Consolidation.BudgetSeconds));

        var duplicateSection = await duplicatePages();
        var conflictSection = await conflicts();
        var review = await candidates();
        var (lessons, changed) = ChangedPageFindings.Produce(context, services, await ChangedPages.ReadAsync(context, cancellationToken));
        var evidence = await PersonaEvidence.ProduceAsync(context, review, cancellationToken);
        var issues = FormatViolations.Issues(context);
        var workstream = await SummaryMaterial.WorkstreamAsync(context, cancellationToken);
        var workstreams = await WorkstreamProposals.ProduceAsync(context, cancellationToken);
        var automatic = new[]
        {
            FormatViolations.Automatic(issues),
            RedundantCandidates.Produce(config, review),
            SummaryMaterial.Weeks(context),
            SummaryMaterial.Months(context),
            workstream,
        };
        var proposals = new[]
        {
            workstreams,
            evidence,
            lessons,
            changed,
            duplicateSection,
            conflictSection,
            new ConsolidationSection(ConsolidationTitles.DomainHygiene, DomainHygiene.Findings(config)),
            await LengthHints.ProduceAsync(context, cancellationToken),
        };
        return new ConsolidationReport(
            context.Baseline, config.Consolidation.BudgetSeconds, judgments.BudgetRanOut, judgments.ModelUnavailable, automatic, proposals);
    }

    /// <summary>
    /// Whether the report shows everything: every judgment answered within the budget by an available model, and no category
    /// cut short. Only a pass over a complete report may move the baseline. A model that is not configured does not make the
    /// report incomplete: without models the baseline would otherwise never move.
    /// </summary>
    public bool Complete => !BudgetRanOut && !ModelUnavailable && !Automatic.Concat(Proposals).Any(section => section.CutShort);

    /// <summary>The report as the consolidation report resource shows it, in English.</summary>
    public string Render()
    {
        var text = new StringBuilder();
        text.Append(CompletenessLine()).Append('\n');
        text.Append(BaselineLine(Baseline)).Append('\n');
        text.Append(BudgetLine()).Append("\n\n");
        RenderPart(text, ConsolidationTitles.AutomaticPart, Automatic);
        RenderPart(text, ConsolidationTitles.ProposalPart, Proposals);
        return text.ToString();
    }

    private static void RenderPart(StringBuilder text, string title, IReadOnlyList<ConsolidationSection> sections)
    {
        text.Append("## ").Append(title).Append("\n\n");
        foreach (var section in sections)
        {
            var increments = section.Increments ?? [];
            text.Append("### ").Append(section.Title).Append('\n');
            if (section.Status is { } status)
                text.Append(status).Append('\n');
            if (section.Findings.Count == 0 && increments.Count == 0 && section.Status is null)
                text.Append("None.\n");
            foreach (var finding in section.Findings.Take(MaxFindingsPerCategory))
                text.Append(finding).Append('\n');
            if (section.CutShort)
                text.Append(section.Findings.Count - MaxFindingsPerCategory)
                    .Append(" more not listed. Run the tacit-consolidate skill again after handling these.\n");
            foreach (var increment in increments)
                text.Append(increment).Append('\n');
            text.Append('\n');
        }
    }

    private string CompletenessLine()
    {
        if (Complete)
            return "Complete: yes. Every judgment finished in time and every category is listed in full; a pass that handles the whole report ends with a consolidate: commit.";

        var reasons = new List<string>();
        if (BudgetRanOut)
            reasons.Add("the budget ran out before every judgment finished");
        if (ModelUnavailable)
            reasons.Add("a model was unavailable, so some judgments were not made");
        var cut = Automatic.Concat(Proposals).Where(section => section.CutShort).Select(section => section.Title).ToList();
        if (cut.Count > 0)
            reasons.Add($"{string.Join(", ", cut)} list{(cut.Count == 1 ? "s" : "")} only {MaxFindingsPerCategory} findings");
        return $"Complete: no; {string.Join("; ", reasons)}. A pass over this report ends with a consolidate(partial): commit, which leaves the baseline in place.";
    }

    private string BudgetLine() => BudgetRanOut
        ? $"Budget: {BudgetSeconds} seconds for the model judgments; they ran out before all of them finished, and the categories say what is not checked yet."
        : $"Budget: {BudgetSeconds} seconds for the model judgments; all of them finished in time.";

    private static string BaselineLine(ConsolidationBaseline baseline) => baseline.Kind switch
    {
        BaselineKind.Consolidation => $"Last consolidate: commit {baseline.Commit!.Hash}, dated {baseline.Commit.Day:yyyy-MM-dd}.",
        BaselineKind.Init => $"Last consolidate: none yet. Counting from the init commit {baseline.Commit!.Hash}, dated {baseline.Commit.Day:yyyy-MM-dd}.",
        _ => "Last consolidate: never. The repository has no consolidate: or init: commit.",
    };
}
