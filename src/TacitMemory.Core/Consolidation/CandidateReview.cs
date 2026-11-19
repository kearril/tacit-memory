using TacitMemory.Core.Decisions;
using TacitMemory.Core.Index;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Persona;

namespace TacitMemory.Core.Consolidation;

/// <summary>One candidate of <c>candidates.md</c>, the number of days it appeared on, and its outcome of the same-preference judgment.</summary>
internal sealed record CandidateJudgment(PersonaCandidate Candidate, int Days, JudgmentOutcome<PreferenceMatch> Outcome);

/// <summary>
/// Every candidate of <c>candidates.md</c>, judged for the same preference as an entry of the persona or of the rejection
/// list, whatever its days. Both candidate categories read this one review, so each candidate is judged once.
/// <see cref="PersonaCodes"/> names the persona entries by their short codes.
/// </summary>
internal sealed record CandidateReview(IReadOnlyList<CandidateJudgment> Judgments, IReadOnlyDictionary<string, string> PersonaCodes)
{
    /// <summary>The status of both candidate categories when <c>candidates.md</c> is not valid UTF-8.</summary>
    public const string NotReadable = "Not checked: candidates.md is not valid UTF-8.";

    /// <summary>The kinds a candidate is compared with: the persona entries and the rejected candidates, never other candidates.</summary>
    private static readonly string[] OfferedKinds = [UnitKinds.PersonaEntry, UnitKinds.RejectedCandidate];

    /// <summary>The number of distinct days a candidate appeared on.</summary>
    public static int DayCount(PersonaCandidate candidate) => candidate.Occurrences.Select(occurrence => occurrence.Day).Distinct().Count();

    /// <summary>
    /// Reads the candidates and the persona, and submits every candidate's judgment. The returned step waits for the answers
    /// as the budget allows; it yields <c>null</c> when <c>candidates.md</c> is not valid UTF-8.
    /// </summary>
    public static async Task<AwaitAnswers<CandidateReview?>> StartAsync(
        ConsolidationContext context, ConsolidationServices services, JudgmentSession judgments)
    {
        var config = context.Config;
        var candidates = TextFile.Read(MemoryLayout.Full(config.MemoryPath, MemoryLayout.Candidates));
        if (candidates is null)
            return () => Task.FromResult<CandidateReview?>(null);

        var threshold = config.Decision.SamePreference ?? Judgment.SamePreference;
        var submitted = CandidatesDocument.Parse(candidates.Value.Text).Candidates
            .Select(candidate => (Candidate: candidate, Job: judgments.Submit(token => PrepareAsync(services, candidate, token), threshold)))
            .ToList();
        var persona = PersonaDocument.Read(MemoryLayout.Full(config.MemoryPath, MemoryLayout.Persona)) ?? PersonaDocument.Parse("");
        var codes = PersonaCodesOf(persona);

        return async () =>
        {
            var judged = new List<CandidateJudgment>(submitted.Count);
            foreach (var (candidate, job) in submitted)
                judged.Add(new CandidateJudgment(candidate, DayCount(candidate), await judgments.CollectAsync(job)));
            return new CandidateReview(judged, codes);
        };
    }

    /// <summary>
    /// The candidate's question: its nearest persona entries and rejected candidates by embedding. Nothing is asked when
    /// there are none, and the question is not prepared when the embedding model is not reachable.
    /// </summary>
    private static async Task<PreparedChoice<PreferenceMatch>> PrepareAsync(
        ConsolidationServices services, PersonaCandidate candidate, CancellationToken cancellationToken)
    {
        var vector = await services.Embedding.EmbedAsync([candidate.Text], cancellationToken);
        if (vector is null)
            return new PreparedChoice<PreferenceMatch>(null, ChoiceJudge.NoAnswerReason(services.Embedding.Configured));

        await services.Vectors.PrepareSearchAsync(vector[0].Length, cancellationToken);
        var options = new List<ChoiceOption<PreferenceMatch>>();
        foreach (var hit in services.Index.SearchJudgments(vector[0], SamePreferenceCheck.Sources, SamePreferenceCheck.OtherOptions, OfferedKinds))
        {
            if (hit.Section is not { } section)
                continue;
            options.Add(new ChoiceOption<PreferenceMatch>(
                SamePreferenceCheck.OptionText(hit.Kind, hit.Scope, section, hit.Text),
                new PreferenceMatch(hit.Kind, section, hit.Text, hit.Scope)));
        }

        if (options.Count == 0)
            return new PreparedChoice<PreferenceMatch>(null, null);
        var question = new ChoiceQuestion<PreferenceMatch>(
            SamePreferenceCheck.StateOf(candidate.Text, candidate.Section),
            SamePreferenceCheck.Instructions,
            SamePreferenceCheck.NoneText,
            options);
        return new PreparedChoice<PreferenceMatch>(question, null);
    }

    /// <summary>
    /// The short code of every persona entry, keyed by its scope and the text the judgment index holds for it; of identical
    /// entries the first wins.
    /// </summary>
    private static Dictionary<string, string> PersonaCodesOf(PersonaDocument persona)
    {
        var codes = persona.CodesByLine();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in persona.KnownEntries())
            result.TryAdd(CodeKey(entry.Scope, entry.Entry.JudgmentText), codes[entry.Entry.Line]);
        return result;
    }

    /// <summary>The key of a persona entry in <see cref="PersonaCodes"/>: its scope and its judgment text.</summary>
    private static string CodeKey(PersonaScope scope, string text) => scope + "\n" + text;

    /// <summary>A match as the report names it: the persona entry with its short code, or the rejected candidate.</summary>
    public string Describe(PreferenceMatch match)
    {
        var heading = PersonaDocument.HeadingOf(match.Section);
        var text = match.Text.ReplaceLineEndings(" ");
        var scope = match.Scope.IsGlobal ? "" : $" of {match.Scope}";
        if (match.Kind == UnitKinds.RejectedCandidate)
            return $"rejected candidate \"{text}\" in section {heading}{scope}";
        return PersonaCodes.TryGetValue(CodeKey(match.Scope, match.Text), out var code)
            ? $"persona entry [{code}] \"{text}\" in section {heading}{scope}"
            : $"persona entry \"{text}\" in section {heading}{scope}";
    }
}
