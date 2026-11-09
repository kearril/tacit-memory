using System.Text;
using TacitMemory.Core.Git;
using TacitMemory.Core.Index;
using TacitMemory.Core.Models;

namespace TacitMemory.Core.Consolidation;

/// <summary>What the categories read without a model: the config, today's date, the repository's git and the baseline.</summary>
internal sealed record ConsolidationContext(TacitConfig Config, DateOnly Today, GitCli Git, ConsolidationBaseline Baseline);

/// <summary>What the categories that ask a model need: the index, the embedding model, the vector store and the judgment pool.</summary>
internal sealed record ConsolidationServices(MemoryIndex Index, EmbeddingModel Embedding, VectorSync Vectors, JudgmentPool Judgments);

/// <summary>One category of the report: its findings, each already written for the agent, and an optional status line.</summary>
/// <param name="Status">Why the category could not be fully checked, when it could not; shown above the findings.</param>
internal sealed record ConsolidationSection(string Title, IReadOnlyList<string> Findings, string? Status = null);

/// <summary>
/// A category whose judgments are already submitted: waiting for their answers as the budget allows, then its result.
/// </summary>
internal delegate Task<T> AwaitAnswers<T>();

/// <summary>The consolidation report: the baseline and the six categories, computed when asked for.</summary>
internal sealed record ConsolidationReport(ConsolidationBaseline Baseline, IReadOnlyList<ConsolidationSection> Sections)
{
    /// <summary>Most findings a category lists; the rest are counted.</summary>
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
        var candidates = await CandidateReview.StartAsync(context, services, judgments, cancellationToken);
        judgments.StartBudget(TimeSpan.FromSeconds(config.Consolidation.BudgetSeconds));

        var sections = new List<ConsolidationSection>
        {
            await duplicatePages(),
            await conflicts(),
        };
        var review = await candidates();
        sections.Add(ConfirmableCandidates.Produce(config, review));
        sections.Add(RedundantCandidates.Produce(config, review));
        sections.Add(FormatViolations.Produce(context));
        sections.Add(await StaleMainline.ProduceAsync(context, cancellationToken));
        return new ConsolidationReport(context.Baseline, sections);
    }

    /// <summary>The report as the consolidation report resource shows it, in English.</summary>
    public string Render()
    {
        var text = new StringBuilder();
        text.Append(BaselineLine(Baseline)).Append("\n\n");
        if (Sections.All(section => section.Findings.Count == 0 && section.Status is null))
            text.Append("No problems found in any category.\n\n");

        foreach (var section in Sections)
        {
            text.Append("### ").Append(section.Title).Append('\n');
            if (section.Status is { } status)
                text.Append(status).Append('\n');
            if (section.Findings.Count == 0 && section.Status is null)
                text.Append("None.\n");
            foreach (var finding in section.Findings.Take(MaxFindingsPerCategory))
                text.Append(finding).Append('\n');
            if (section.Findings.Count > MaxFindingsPerCategory)
                text.Append(section.Findings.Count - MaxFindingsPerCategory)
                    .Append(" more not listed. Run the tacit-consolidate skill again after handling these.\n");
            text.Append('\n');
        }

        return text.ToString();
    }

    private static string BaselineLine(ConsolidationBaseline baseline) => baseline.Kind switch
    {
        BaselineKind.Consolidation => $"Last consolidate: commit {baseline.Commit!.Hash}, dated {baseline.Commit.Day:yyyy-MM-dd}.",
        BaselineKind.Init => $"Last consolidate: none yet. Counting from the init commit {baseline.Commit!.Hash}, dated {baseline.Commit.Day:yyyy-MM-dd}.",
        _ => "Last consolidate: never. The repository has no consolidate: or init: commit.",
    };
}
