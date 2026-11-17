using TacitMemory.Core.Decisions;
using TacitMemory.Core.Index;
using TacitMemory.Core.Models;

namespace TacitMemory.Core.Persona;

/// <summary>An existing persona entry the new one conflicts with: its scope, its section, its item and its short code.</summary>
internal sealed record ConflictingEntry(PersonaScope Scope, PersonaSection Section, PersonaItem Item, string Code);

/// <summary>What the rule-conflict check found: the entry the new one conflicts with, or a note for the agent when the check was skipped.</summary>
internal sealed record RuleConflictVerdict(ConflictingEntry? Conflict, string? Note);

/// <summary>
/// Before an entry is added to the persona or replaces one, asks whether it conflicts with, duplicates or supersedes an
/// existing entry of any of the four sections: the nearest entries by embedding, plus "none of these", go to the decision
/// model as a choice question. The consolidation asks the same question about the entries it finds changed.
/// </summary>
internal sealed class RuleConflictCheck(TacitConfig config, MemoryIndex index, EmbeddingModel embedding, VectorSync vectors, ChoiceJudge judge)
{
    /// <summary>Entries offered besides "none of these"; the decision model takes at most 10 options.</summary>
    public const int OtherOptions = 9;

    public const string DecisionDown =
        "Note: the decision model is unavailable, so whether the new entry conflicts with an existing persona entry was not checked.";

    public const string EmbeddingDown =
        "Note: the embedding model is unavailable, so whether the new entry conflicts with an existing persona entry was not checked.";


    internal const string Instructions =
        "The state is a new entry for the user's persona file, with its section in brackets. "
        + "Each option is an existing persona entry from any of the four sections, with its section in brackets. "
        + "Entries are kept as written, in whatever language they are in. "
        + "Choose the option if the new entry conflicts with it, duplicates it, or supersedes it: both cannot hold as written, "
        + "they say the same thing, or the new entry replaces the old one. Entries that are related but can both hold are not a match. "
        + "If no option conflicts with, duplicates or supersedes the new entry, choose the none option. Answer with the option key only.";

    internal const string NoneText = "None of the existing entries conflicts with, duplicates or supersedes the new entry.";

    /// <summary>Longest description of one option.</summary>
    private const int OptionLength = 250;

    /// <param name="text">The new entry's text, as written.</param>
    /// <param name="section">The section the new entry goes into.</param>
    /// <param name="scope">The scope the new entry goes into, or the scope of the entry it replaces.</param>
    /// <param name="persona">The persona as it is before the write.</param>
    /// <param name="replaced">The entry the write replaces, which is never offered as an option; <c>null</c> for an add.</param>
    public async Task<RuleConflictVerdict> RunAsync(
        string text, PersonaSection section, PersonaScope scope, PersonaDocument persona, PersonaItem? replaced, CancellationToken cancellationToken)
    {
        var embeddingFailed = false;
        var outcome = await judge.AskAsync(
            Neighbours.PersonaEntries,
            async token =>
            {
                var others = persona.KnownEntries()
                    .Where(entry => JudgedAgainst(entry.Scope, scope) && entry.Item.Line != replaced?.Line)
                    .ToList();
                if (others.Count == 0)
                    return PreparedChoice<ConflictingEntry>.Nothing;
                var vector = await embedding.EmbedAsync([text], token);
                if (vector is null)
                {
                    embeddingFailed = true;
                    return new PreparedChoice<ConflictingEntry>(null, ChoiceJudge.NoAnswerReason(embedding.Configured));
                }
                await vectors.PrepareSearchAsync(vector[0].Length, token);
                // One more than OtherOptions: the replaced entry may be among the nearest and is skipped by Nearest.
                var hits = index.SearchJudgments(vector[0], [SourceKinds.Persona], OtherOptions + 1, scopes: ScopesJudged(scope));
                var options = Nearest(hits, persona, replaced?.Line)
                    .Select(entry => new ChoiceOption<ConflictingEntry>(OptionText(entry.Section, entry.Item.JudgmentText, config.Language), entry))
                    .ToList();
                return options.Count == 0
                    ? PreparedChoice<ConflictingEntry>.Nothing
                    : new PreparedChoice<ConflictingEntry>(new ChoiceQuestion<ConflictingEntry>(StateOf(text, section, config.Language), Instructions, NoneText, options), null);
            },
            config.Decision.RuleConflict ?? Judgment.RuleConflict,
            cancellationToken);
        return new RuleConflictVerdict(outcome.Certain, ChoiceJudge.NoteOf(outcome.Reason, embeddingFailed, EmbeddingDown, DecisionDown));
    }

    /// <summary>The new entry as the question states it: its section in brackets, then its text.</summary>
    internal static string StateOf(string text, PersonaSection section, MemoryLanguage language) =>
        $"[{PersonaDocument.HeadingOf(section)}] {text}";

    /// <summary>One existing entry as the question offers it: its section in brackets, then its text, cut to the option length.</summary>
    internal static string OptionText(PersonaSection section, string text, MemoryLanguage language)
    {
        var description = $"[{PersonaDocument.HeadingOf(section)}] {text.ReplaceLineEndings(" ")}";
        return description.Length > OptionLength ? description[..OptionLength] : description;
    }

    /// <summary>
    /// The scopes a new entry is judged against: the global entries, and the entries of its own scope. The index is asked
    /// for these scopes only, so entries of other domains and workstreams are never offered.
    /// </summary>
    internal static IReadOnlyList<PersonaScope> ScopesJudged(PersonaScope scope) => scope.IsGlobal ? [PersonaScope.Global] : [PersonaScope.Global, scope];

    /// <summary>Whether an entry of <paramref name="entryScope"/> is judged against an entry of <paramref name="scope"/>.</summary>
    internal static bool JudgedAgainst(PersonaScope entryScope, PersonaScope scope) => entryScope.IsGlobal || entryScope == scope;

    /// <summary>
    /// The persona entries the judgment hits name, in hit order: each hit is matched to the first entry of its scope, section
    /// and text that is not excluded, so identical entries are told apart. At most <see cref="OtherOptions"/> are returned.
    /// </summary>
    internal static IReadOnlyList<ConflictingEntry> Nearest(IReadOnlyList<JudgmentHit> hits, PersonaDocument persona, int? excludedLine)
    {
        var codes = persona.CodesByLine();
        var entries = persona.KnownEntries();
        var taken = new HashSet<int>();
        if (excludedLine is { } line)
            taken.Add(line);
        var nearest = new List<ConflictingEntry>();
        foreach (var hit in hits)
        {
            if (hit.Section is not { } kind)
                continue;
            var found = entries.FirstOrDefault(entry =>
                entry.Scope == hit.Scope && entry.Kind == kind && entry.Item.JudgmentText == hit.Text && !taken.Contains(entry.Item.Line));
            if (found is null)
                continue;
            taken.Add(found.Item.Line);
            nearest.Add(new ConflictingEntry(found.Scope, kind, found.Item, codes[found.Item.Line]));
            if (nearest.Count == OtherOptions)
                break;
        }

        return nearest;
    }
}
