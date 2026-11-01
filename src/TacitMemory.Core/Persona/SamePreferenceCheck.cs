using System.Globalization;
using TacitMemory.Core.Decisions;
using TacitMemory.Core.Index;
using TacitMemory.Core.Models;

namespace TacitMemory.Core.Persona;

/// <summary>A persona candidate, rejected candidate or persona entry that states the same preference as a proposal.</summary>
/// <param name="Kind">The index unit kind: <see cref="UnitKinds.Candidate"/>, <see cref="UnitKinds.RejectedCandidate"/> or <see cref="UnitKinds.PersonaEntry"/>.</param>
internal sealed record PreferenceMatch(string Kind, PersonaSection Section, string Text);

/// <summary>What the same-preference check found: the matching entry, or a note for the agent when the check could not run.</summary>
internal sealed record SamePreferenceVerdict(PreferenceMatch? Same, string? Note);

/// <summary>
/// Before a candidate is proposed, asks whether the same preference is already a candidate, on the rejection list or in
/// the persona: the nearest of those by embedding, plus "none of these", go to the decision model as a choice question.
/// Nothing is asked when none of the three exists.
/// </summary>
internal sealed class SamePreferenceCheck(TacitConfig config, MemoryIndex index, EmbeddingModel embedding, VectorSync vectors, IDecisionModel decision)
{
    /// <summary>Entries offered besides "none of these"; the decision model takes at most 10 options.</summary>
    public const int OtherOptions = 9;

    /// <summary>The sources whose units are compared: <c>persona.md</c> and <c>candidates.md</c> (which holds the rejection list).</summary>
    public static readonly string[] Sources = [SourceKinds.Persona, SourceKinds.Candidates];

    public const string DecisionDown =
        "Note: the decision model is unavailable, so whether this preference is already a candidate, on the rejection list or in the persona was not checked.";

    public const string EmbeddingDown =
        "Note: the embedding model is unavailable, so whether this preference is already a candidate, on the rejection list or in the persona was not checked.";

    private const string None = "none";

    internal const string Instructions =
        "The state is a new preference or working rule proposed from a conversation, with its section in brackets. "
        + "Each option is an existing entry, with its source and section in brackets: candidate (a pending preference not yet confirmed), "
        + "rejected (a preference the user declined), or persona (a confirmed entry of the user's persona file). "
        + "Entries are kept as written, in whatever language they are in. "
        + "Choose the option if it states the same preference or rule as the new one: the same thing asked for, even if phrased differently. "
        + "Entries on the same topic that ask for different things are different, and so are opposite preferences. "
        + "If no option states the same preference or rule, choose the none option. Answer with the option key only.";

    internal const string NoneText = "None of the options states the same preference or rule as the new one.";

    /// <summary>Longest description of one option.</summary>
    private const int OptionLength = 250;

    /// <summary>One existing entry as the question offers it: its kind and section in brackets, cut to the option length.</summary>
    internal static string OptionText(string kind, PersonaSection section, string text, MemoryLanguage language)
    {
        var label = $"[{kind} | {PersonaDocument.HeadingOf(section).In(language)}] {text}";
        return label.Length > OptionLength ? label[..OptionLength] : label;
    }

    /// <summary>The new preference as the question states it: its section in brackets, then its text.</summary>
    internal static string StateOf(string text, PersonaSection section, MemoryLanguage language) =>
        $"[{PersonaDocument.HeadingOf(section).In(language)}] {text}";

    public async Task<SamePreferenceVerdict> RunAsync(string text, PersonaSection section, CancellationToken cancellationToken)
    {
        if (!index.HasJudgments(Sources))
            return new SamePreferenceVerdict(null, null);

        var vector = await embedding.EmbedAsync([text], cancellationToken);
        if (vector is null)
            return new SamePreferenceVerdict(null, EmbeddingDown);

        await vectors.PrepareSearchAsync(vector[0].Length, cancellationToken);
        var nearest = index.SearchJudgments(vector[0], Sources, OtherOptions);
        if (nearest.Count == 0)
            return new SamePreferenceVerdict(null, null);

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var matches = new Dictionary<string, PreferenceMatch>(StringComparer.Ordinal);
        foreach (var hit in nearest)
        {
            if (hit.Section is not { } hitSection)
                continue;
            var key = "o" + (matches.Count + 1).ToString(CultureInfo.InvariantCulture);
            var match = new PreferenceMatch(hit.Kind, hitSection, hit.Text);
            options[key] = OptionText(hit.Kind, hitSection, hit.Text, config.Language);
            matches[key] = match;
        }

        options[None] = NoneText;
        var state = StateOf(text, section, config.Language);
        var probabilities = await decision.ChooseAsync(state, Instructions, options, cancellationToken);
        if (probabilities is null)
            return new SamePreferenceVerdict(null, DecisionDown);

        var verdict = Judgment.Choice(probabilities, None, config.Decision.SamePreference ?? Judgment.SamePreference);
        return verdict.Tier == Tier.Certain
            ? new SamePreferenceVerdict(matches[verdict.Option!], null)
            : new SamePreferenceVerdict(null, null);
    }
}
