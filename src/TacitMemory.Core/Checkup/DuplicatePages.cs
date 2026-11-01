using TacitMemory.Core.Decisions;
using TacitMemory.Core.Index;
using TacitMemory.Core.Pages;

namespace TacitMemory.Core.Checkup;

/// <summary>
/// The "Suspected duplicate pages" category. Each page changed since the last checkup is judged as a new page is judged
/// live: its nearest pages of the same project are the options, and the decision model answers the same question. A sure
/// match and an unsure one are listed once per unordered pair, with their tiers; a rejection is not listed.
/// </summary>
internal static class DuplicatePages
{
    private const string NotCheckedStatus =
        "Not checked: the decision or embedding model is unavailable, so whether changed pages repeat existing ones was not checked.";

    /// <summary>Reads the changed pages and submits each one's judgment; the result is awaited with the budget.</summary>
    public static async Task<AwaitAnswers<CheckupSection>> StartAsync(
        CheckupContext context, CheckupServices services, JudgmentSession judgments, CancellationToken cancellationToken)
    {
        // A manual edit may not have reached the index yet; the nearest pages come from the index, so bring it up to date first.
        services.Index.SyncAll();

        var threshold = context.Config.Decision.SamePage ?? Judgment.SamePage;
        var submitted = (await ChangedPages.ReadAsync(context, cancellationToken))
            .Select(page => (Page: page, Job: judgments.Submit(token => PrepareAsync(context.Config, services, page, token), threshold)))
            .ToList();

        return async () =>
        {
            var pairs = new PairFindings<PageId>(page => page.ToString());
            var reasons = new List<UncheckedReason?>(submitted.Count);
            foreach (var (page, job) in submitted)
            {
                var outcome = await judgments.CollectAsync(job);
                reasons.Add(outcome.Reason);
                if (outcome.Tier is Tier.Certain or Tier.Uncertain && outcome.Target is { } other)
                {
                    // Kept in ordinal order of the page identifiers, so a pair reads the same whichever page was judged first.
                    var (first, second) = string.CompareOrdinal(page.ToString(), other.ToString()) <= 0 ? (page, other) : (other, page);
                    pairs.Add(first, second, outcome.Tier.Value);
                }
            }

            var findings = pairs.Pairs.Select(pair =>
            {
                var phrase = pair.Tier == Tier.Certain ? "certain, the same matter" : "uncertain, possibly the same matter";
                return $"- {Describe(context.Config.MemoryPath, pair.First)} and {Describe(context.Config.MemoryPath, pair.Second)}: {phrase}.";
            }).ToList();

            var status = Unanswered.Of(reasons).Status(
                _ => NotCheckedStatus,
                count => $"{count} changed page(s) not checked yet, because the checkup ran out of time. Run `checkup` again to continue.");
            return new CheckupSection(CheckupTitles.DuplicatePages, findings, status);
        };
    }

    /// <summary>
    /// The question about a changed page, built as the live check builds it for a new page. Nothing is asked when the
    /// project has no other page, and the question is not prepared when the embedding model is not reachable.
    /// </summary>
    private static async Task<PreparedChoice<PageId>> PrepareAsync(TacitConfig config, CheckupServices services, PageId page, CancellationToken cancellationToken)
    {
        var memory = config.MemoryPath;
        if (PageDocument.TryLoad(memory, page) is not { } document || !services.Index.HasOtherPagesInProject(page))
            return new PreparedChoice<PageId>(null, false);

        var vector = await services.Embedding.EmbedAsync([string.Join('\n', [document.Header.Title, .. document.Header.Aliases])], cancellationToken);
        if (vector is null)
            return new PreparedChoice<PageId>(null, true);

        await services.Vectors.PrepareSearchAsync(vector[0].Length, cancellationToken);
        var options = DuplicatePageCheck.Describe(memory, DuplicatePageCheck.Nearest(services.Index, page, vector[0]))
            .Select(option => new ChoiceOption<PageId>(option.Text, option.Page))
            .ToList();
        if (options.Count == 0)
            return new PreparedChoice<PageId>(null, false);

        var sections = document.Sections.Select(section => new PageSectionInput(section.Name, section.Body)).ToList();
        var question = new ChoiceQuestion<PageId>(
            DuplicatePageCheck.State(document.Header, sections),
            DuplicatePageCheck.Instructions,
            DuplicatePageCheck.NoneText,
            options);
        return new PreparedChoice<PageId>(question, false);
    }

    /// <summary>A page as the report names it: its identifier and its title, or the identifier alone when it cannot be read.</summary>
    private static string Describe(string memory, PageId page) =>
        PageDocument.TryLoad(memory, page) is { } document ? $"{page} \"{document.Header.Title}\"" : page.ToString();
}
