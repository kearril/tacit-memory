using TacitMemory.Core.Decisions;
using TacitMemory.Core.Index;
using TacitMemory.Core.Persona;

namespace TacitMemory.Core.Consolidation;

/// <summary>
/// The persona entries that are new or rewritten since the last consolidation, each judged against the other entries of the
/// persona with the live rule-conflict question. A sure or unsure conflict, duplicate or supersession is listed once per
/// unordered pair of entries, with both entries' sections, texts, short codes and the tier.
/// </summary>
internal static class ConflictingEntries
{
    public const string NotReadable = "Not checked: persona.md is not valid UTF-8.";

    /// <summary>Reads the persona and submits a judgment for each changed entry; the result is awaited with the budget.</summary>
    public static async Task<AwaitAnswers<ConsolidationSection>> StartAsync(
        ConsolidationContext context, ConsolidationServices services, JudgmentSession judgments, CancellationToken cancellationToken)
    {
        var config = context.Config;
        var persona = PersonaDocument.Read(MemoryLayout.Full(config.MemoryPath, MemoryLayout.Persona));
        if (persona is null)
            return () => Task.FromResult(new ConsolidationSection(ConsolidationTitles.ConflictingEntries, [], NotReadable));

        // The judgments search the index, so bring it up to date with a manual edit first.
        services.Index.SyncJudgments();

        var codes = persona.CodesByLine();
        var entries = persona.KnownEntries()
            .Select(entry => new ConflictingEntry(entry.Scope, entry.Kind, entry.Item, codes[entry.Item.Line]))
            .ToList();
        var known = await BaselineCodesAsync(context, cancellationToken);
        var changed = entries.Where(entry => known is null || !known.Contains(entry.Code)).ToList();

        var threshold = config.Decision.RuleConflict ?? Judgment.RuleConflict;
        var submitted = changed
            .Select(entry => (Entry: entry, Job: judgments.Submit(token => PrepareAsync(config, services, persona, entries.Count, entry, token), threshold)))
            .ToList();

        return async () =>
        {
            var pairs = new PairFindings<ConflictingEntry>(entry => entry.Code);
            var reasons = new List<UncheckedReason?>(submitted.Count);
            foreach (var (entry, job) in submitted)
            {
                var outcome = await judgments.CollectAsync(job);
                reasons.Add(outcome.Reason);
                if (outcome.Tier is Tier.Certain or Tier.Uncertain && outcome.Target is { } other)
                    pairs.Add(entry, other, outcome.Tier.Value);
            }

            var language = config.Language;
            var lines = pairs.Pairs
                .Select(pair => $"- {(pair.Tier == Tier.Certain ? "Certain" : "Unsure")}: {Describe(pair.First, language)} and {Describe(pair.Second, language)}.")
                .ToList();
            var status = Unanswered.Of(reasons).Status(
                count => $"Not checked: the decision or embedding model is unavailable, so whether {count} changed persona entr{(count == 1 ? "y" : "ies")} conflict with others was not checked.",
                count => $"Not checked (not configured): the decision or embedding model is not configured, so whether {count} changed persona entr{(count == 1 ? "y" : "ies")} conflict with others was not checked.",
                count => $"{count} changed persona entr{(count == 1 ? "y" : "ies")} not checked yet, because the consolidation ran out of time. Run the tacit-consolidate skill again to continue.");
            return new ConsolidationSection(ConsolidationTitles.ConflictingEntries, lines, status);
        };
    }

    /// <summary>
    /// The short codes of the persona as the last consolidation saw it, or <c>null</c> when there is no consolidation or init commit:
    /// then every entry counts as changed. A persona file absent from the baseline commit has no codes.
    /// </summary>
    private static async Task<HashSet<string>?> BaselineCodesAsync(ConsolidationContext context, CancellationToken cancellationToken)
    {
        if (context.Baseline.Commit is not { } commit)
            return null;
        var text = await context.Git.ShowAsync(commit.Hash, MemoryLayout.Persona, cancellationToken);
        return PersonaDocument.Parse(text ?? "").CodesByLine().Values.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// The entry's question: the nearest other persona entries by embedding, the rule-conflict question as the live check
    /// asks it. Nothing is asked when the persona has no other entry, and the question is not prepared when the embedding
    /// model is not reachable.
    /// </summary>
    private static async Task<PreparedChoice<ConflictingEntry>> PrepareAsync(
        TacitConfig config, ConsolidationServices services, PersonaDocument persona, int entryCount, ConflictingEntry entry, CancellationToken cancellationToken)
    {
        if (entryCount < 2)
            return new PreparedChoice<ConflictingEntry>(null, null);

        var vector = await services.Embedding.EmbedAsync([entry.Item.JudgmentText], cancellationToken);
        if (vector is null)
            return new PreparedChoice<ConflictingEntry>(null, JudgmentPool.NoAnswerReason(services.Embedding.Configured));

        await services.Vectors.PrepareSearchAsync(vector[0].Length, cancellationToken);
        // One more than the offered count: the entry itself may be among the nearest and is skipped by Nearest.
        var hits = services.Index.SearchJudgments(
            vector[0], [SourceKinds.Persona], RuleConflictCheck.OtherOptions + 1, scopes: RuleConflictCheck.ScopesJudged(entry.Scope));
        var nearest = RuleConflictCheck.Nearest(hits, persona, entry.Item.Line);
        if (nearest.Count == 0)
            return new PreparedChoice<ConflictingEntry>(null, null);

        var language = config.Language;
        var options = nearest
            .Select(other => new ChoiceOption<ConflictingEntry>(RuleConflictCheck.OptionText(other.Section, other.Item.JudgmentText, language), other))
            .ToList();
        var question = new ChoiceQuestion<ConflictingEntry>(
            RuleConflictCheck.StateOf(entry.Item.JudgmentText, entry.Section, language),
            RuleConflictCheck.Instructions,
            RuleConflictCheck.NoneText,
            options);
        return new PreparedChoice<ConflictingEntry>(question, null);
    }

    /// <summary>
    /// An entry as the report names it: its short code, its text, its section in the memory language, and its scope when it
    /// is not the global one.
    /// </summary>
    private static string Describe(ConflictingEntry entry, MemoryLanguage language) =>
        $"[{entry.Code}] \"{entry.Item.JudgmentText.ReplaceLineEndings(" ")}\" in section {PersonaDocument.HeadingOf(entry.Section).In(language)}"
        + (entry.Scope.IsGlobal ? "" : $" of {entry.Scope}");
}
