using System.Text.RegularExpressions;
using TacitMemory.Core.Decisions;
using TacitMemory.Core.Index;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Pages;
using TacitMemory.Core.Tools;

namespace TacitMemory.Core.Persona;

/// <summary>
/// The persona side of the memory tools: the <c>write_persona</c> operations. Callers serialize writes; this class
/// assumes it runs one write at a time.
/// </summary>
internal sealed class PersonaTools(
    TacitOptions options, TacitConfig config, SamePreferenceCheck samePreference, RuleConflictCheck conflicts, MemoryWriter writer)
{
    /// <summary>
    /// The parameters the propose op takes: the entry's, and the scope it goes into. Any other parameter given with it is
    /// refused rather than ignored.
    /// </summary>
    private static readonly string[] EntryParameters = ["persona_section", "text", "source", "scope", "confirm_no_secret"];

    /// <summary>The parameters the add op takes: the entry's parameters, and the scope and the trigger of a domain.</summary>
    private static readonly string[] AddParameters = ["persona_section", "text", "source", "scope", "trigger", "confirm_no_secret"];

    /// <summary>The parameters the replace op takes; the section is the replaced entry's own.</summary>
    private static readonly string[] ReplaceParameters = ["hash", "text", "source", "confirm_no_secret"];

    /// <summary>The parameters the delete op takes: the short code alone.</summary>
    private static readonly string[] DeleteParameters = ["hash"];

    /// <summary>The parameters each op takes; any other parameter given with an op is refused rather than ignored.</summary>
    private static readonly Dictionary<string, string[]> OpParameters = new()
    {
        [ToolValues.WritePersonaOps.Add] = AddParameters,
        [ToolValues.WritePersonaOps.Propose] = EntryParameters,
        [ToolValues.WritePersonaOps.Replace] = ReplaceParameters,
        [ToolValues.WritePersonaOps.Delete] = DeleteParameters,
    };

    private static readonly string Ops = string.Join(", ", OpParameters.Keys);

    /// <summary>Every parameter <c>write_persona</c> takes, listed when the op is missing or unknown.</summary>
    private static readonly string AllParameters = string.Join(", ", new[] { "op" }.Concat(OpParameters.Values.SelectMany(taken => taken).Distinct()));

    /// <summary>The four sections as an agent names them.</summary>
    private readonly (PersonaSection Kind, string Name)[] Sections =
    [
        (PersonaSection.RedLines, ToolValues.PersonaSections.RedLines),
        (PersonaSection.WorkingRules, ToolValues.PersonaSections.WorkingRules),
        (PersonaSection.Preferences, ToolValues.PersonaSections.Preferences),
        (PersonaSection.Profile, ToolValues.PersonaSections.Profile),
    ];

    private string SectionNames => string.Join(", ", Sections.Select(section => section.Name));

    public async Task<ToolResult> WriteAsync(WritePersonaRequest request, CancellationToken cancellationToken)
    {
        var problems = ToolText.UnknownParameters(request.UnknownParameters);
        string nextStep;
        if (!OpParameters.TryGetValue(request.Op ?? "", out var taken))
        {
            problems.Add(string.IsNullOrWhiteSpace(request.Op) ? "Missing parameter op." : $"Unknown op {ToolText.Quote(request.Op)}.");
            nextStep = $"Call write_persona again with op set to one of: {Ops}; its parameters are: {AllParameters}.";
        }
        else
        {
            problems.AddRange(MisplacedParameters(request, taken).Select(name => $"Parameter {name} does not apply to op {request.Op}."));
            nextStep = $"Call write_persona again with only the parameters for op {request.Op}: {string.Join(", ", taken)}.";
        }
        if (problems.Count > 0)
            return ToolText.Refuse(problems, nextStep);

        return request.Op switch
        {
            ToolValues.WritePersonaOps.Add => await AddAsync(request, cancellationToken),
            ToolValues.WritePersonaOps.Propose => await ProposeAsync(request, cancellationToken),
            ToolValues.WritePersonaOps.Replace => await ReplaceAsync(request, cancellationToken),
            ToolValues.WritePersonaOps.Delete => await DeleteAsync(request, cancellationToken),
            _ => throw new InvalidOperationException($"Op {request.Op} passed the op check without a handler."),
        };
    }

    private async Task<ToolResult> AddAsync(WritePersonaRequest request, CancellationToken cancellationToken)
    {
        var section = (request.Section ?? "").Trim();
        var text = (request.Text ?? "").Trim();
        var source = (request.Source ?? "").Trim();
        var trigger = (request.Trigger ?? "").Trim();
        var kind = SectionOf(section);

        var reasons = MissingEntry(section, text, source, kind);
        if (reasons.Count > 0 || kind is not { } sectionKind)
            return ToolText.Refuse(
                reasons,
                $"Call write_persona again with op=add, persona_section (one of: {SectionNames}), text and source.");

        var (scope, scopeRefusal) = ScopeOf(request, "add");
        if (scopeRefusal is not null)
            return scopeRefusal;

        var problems = EntryProblems(text, source);
        if (problems.Count > 0)
            return ToolText.Refuse(problems, "Fix these and call write_persona op=add again.");

        if (SecretCheck(request, text, source, ToolValues.WritePersonaOps.Add, trigger) is { } secret)
            return secret;

        var relative = MemoryLayout.Persona;
        var path = MemoryLayout.Full(config.MemoryPath, relative);
        var read = TextFile.Read(path);
        if (read is null)
            return NotUtf8(relative, "add");
        var (existing, hasBom) = read.Value;

        var persona = PersonaDocument.Parse(existing);
        var name = PersonaDocument.HeadingOf(sectionKind);
        if (TriggerRefusal(persona, scope, trigger) is { } untriggered)
            return untriggered;

        var judgment = await conflicts.RunAsync(text, sectionKind, scope, persona, null, cancellationToken);
        if (judgment.Conflict is { } conflict)
            return ConflictRefusal(conflict);

        var entry = EntryLines(text, source);
        // The trigger is written only when this write creates the domain; an existing domain keeps its own.
        var createdTrigger = scope.Kind == PersonaScopeKind.Domain && persona.BlockOf(scope) is null ? trigger : null;
        var (after, line) = scope.IsGlobal
            ? PersonaFile.Append(existing, sectionKind, entry)
            : PersonaFile.AppendToBlock(existing, scope, createdTrigger, sectionKind, entry);
        var code = PersonaDocument.Parse(after).CodesByLine()[line];

        var reply = scope.IsGlobal
            ? WithNote($"Added to the {name} section as [{code}]. The section now has {persona.Entries(sectionKind).Count + 1} entries.", judgment.Note)
            : WithNote(AddedToScopeReply(persona, scope, sectionKind, code, createdTrigger), judgment.Note);
        return await WriteAsync(relative, after, hasBom, $"persona: add {code}", WrittenSource.Persona, reply, cancellationToken);
    }

    /// <summary>
    /// The scope a write names: global when the parameter is omitted. The refusal is set instead when the text is not a
    /// scope; <paramref name="op"/> names the op in its next step.
    /// </summary>
    private static (PersonaScope Scope, ToolResult? Refusal) ScopeOf(WritePersonaRequest request, string op)
    {
        var text = string.IsNullOrWhiteSpace(request.Scope) ? "global" : request.Scope.Trim();
        if (PersonaScope.TryParse(text, out var scope))
            return (scope, null);
        return (PersonaScope.Global, ToolText.Refuse(
            $"Parameter scope {ToolText.Quote(text)} is not a scope: it must be global, workstream:<name> or domain:<label>, "
            + "and the name uses only lowercase letters, digits, '.', '_' and '-'.",
            $"Call write_persona again with op={op} and scope set to global, workstream:<name> or domain:<label>."));
    }

    /// <summary>
    /// The refusal for a new domain without a one-line trigger; null when the write may go ahead. A domain that already
    /// exists keeps its trigger, and a workstream has none. Nothing is capped: sections, domains and their entries take any count.
    /// </summary>
    private static ToolResult? TriggerRefusal(PersonaDocument persona, PersonaScope scope, string trigger)
    {
        if (scope.Kind != PersonaScopeKind.Domain || persona.BlockOf(scope) is not null)
            return null;

        var reasons = new List<string>();
        if (trigger.Length == 0)
            reasons.Add($"Domain {scope.Name} is new, so it needs a trigger: one line saying when the domain applies.");
        else if (trigger.IndexOfAny(TextFile.LineBreaks) >= 0)
            reasons.Add("Trigger must be one line.");

        return reasons.Count == 0
            ? null
            : ToolText.Refuse(
                reasons,
                $"Call write_persona again with op=add, scope={scope}, the same persona_section, text and source, and trigger set to "
                + "one line saying when the domain applies.");
    }

    /// <summary>The reply for an entry added to a domain or a workstream: where it went, the domain's count, and the trigger of a new domain.</summary>
    private string AddedToScopeReply(PersonaDocument persona, PersonaScope scope, PersonaSection kind, string code, string? createdTrigger)
    {
        var name = PersonaDocument.HeadingOf(kind);
        if (scope.Kind == PersonaScopeKind.Workstream)
            return $"Added to the {name} section of workstream {scope.Name} as [{code}].";

        var count = (persona.BlockOf(scope)?.Entries.Count ?? 0) + 1;
        var created = createdTrigger is null ? "" : $" The domain was created with trigger: {createdTrigger}";
        return $"Added to the {name} section of domain {scope.Name} as [{code}]. The domain has {count} entries.{created}";
    }

    /// <summary>
    /// Replaces the entry the short code names, in place and in its own section: its text and a fresh source comment
    /// take the place of its lines. Refused with the whole persona when the code is missing or names no entry.
    /// </summary>
    private async Task<ToolResult> ReplaceAsync(WritePersonaRequest request, CancellationToken cancellationToken)
    {
        var hash = (request.Hash ?? "").Trim();
        var text = (request.Text ?? "").Trim();
        var source = (request.Source ?? "").Trim();

        var reasons = new List<string>();
        if (text.Length == 0)
            reasons.Add("Missing parameter text.");
        if (source.Length == 0)
            reasons.Add("Missing parameter source.");

        var relative = MemoryLayout.Persona;
        var path = MemoryLayout.Full(config.MemoryPath, relative);
        var read = TextFile.Read(path);
        if (read is null)
            return NotUtf8(relative, "replace");
        var (existing, hasBom) = read.Value;

        var persona = PersonaDocument.Parse(existing);
        if (Find(persona, hash) is not { } entry)
            return NoEntry(hash, reasons, "replace");

        if (text.Length > 0 && source.Length > 0)
            reasons.AddRange(EntryProblems(text, source));
        if (reasons.Count > 0)
            return ToolText.Refuse(reasons, "Fix these and call write_persona op=replace again with the same hash.");

        if (SecretCheck(request, text, source, ToolValues.WritePersonaOps.Replace) is { } secret)
            return secret;

        var name = PersonaDocument.HeadingOf(entry.Kind);
        var lines = EntryLines(text, source);
        var after = PersonaFile.ReplaceEntry(existing, entry.Entry, lines);
        if (after == existing)
            return ToolText.Ok($"[{entry.Code}] already reads exactly so in the {name} section; nothing was written.");

        var judgment = await conflicts.RunAsync(text, entry.Kind, entry.Scope, persona, entry.Entry, cancellationToken);
        if (judgment.Conflict is { } conflict)
            return ConflictRefusal(conflict);

        var code = PersonaDocument.Parse(after).CodesByLine()[entry.Entry.Line];
        var reply = WithNote(
            $"Replaced [{entry.Code}] with [{code}] in the {name} section{ScopePhrase(entry.Scope)}. "
            + CountNote(persona, entry.Scope, entry.Kind, 0),
            judgment.Note);
        return await WriteAsync(relative, after, hasBom, $"persona: replace {code}", WrittenSource.Persona, reply, cancellationToken);
    }

    /// <summary>
    /// Deletes the entry the short code names, with its continuation lines and source comment, and returns its text.
    /// </summary>
    private async Task<ToolResult> DeleteAsync(WritePersonaRequest request, CancellationToken cancellationToken)
    {
        var hash = (request.Hash ?? "").Trim();
        var relative = MemoryLayout.Persona;
        var path = MemoryLayout.Full(config.MemoryPath, relative);
        var read = TextFile.Read(path);
        if (read is null)
            return NotUtf8(relative, "delete");
        var (existing, hasBom) = read.Value;

        var persona = PersonaDocument.Parse(existing);
        if (Find(persona, hash) is not { } entry)
            return NoEntry(hash, [], "delete");

        var name = PersonaDocument.HeadingOf(entry.Kind);
        var after = PersonaFile.ReplaceEntry(existing, entry.Entry, []);
        var reply = $"Deleted [{entry.Code}] from the {name} section{ScopePhrase(entry.Scope)}. It read:\n{Listed(entry.Entry, entry.Code)}\n"
            + CountNote(persona, entry.Scope, entry.Kind, -1);
        return await WriteAsync(relative, after, hasBom, $"persona: delete {entry.Code}", WrittenSource.Persona, reply, cancellationToken);
    }

    /// <summary>
    /// Proposes a candidate for a preference or working rule the user has not asked to remember. The same-preference check
    /// runs first: a match merges into its candidate, or is answered without writing when it is on the rejection list or
    /// already in the persona. Otherwise a new candidate is appended at the end of the candidates section. Candidates have
    /// no short code in the reply and no limit.
    /// </summary>
    private async Task<ToolResult> ProposeAsync(WritePersonaRequest request, CancellationToken cancellationToken)
    {
        var section = (request.Section ?? "").Trim();
        var text = (request.Text ?? "").Trim();
        var source = (request.Source ?? "").Trim();
        var kind = SectionOf(section);

        var reasons = MissingEntry(section, text, source, kind);
        if (reasons.Count > 0 || kind is not { } sectionKind)
            return ToolText.Refuse(
                reasons,
                $"Call write_persona again with op=propose, persona_section (one of: {SectionNames}), text and source.");

        if (sectionKind == PersonaSection.RedLines)
            return ToolText.Refuse(
                "A red line cannot be proposed as a candidate; only the user sets red lines.",
                "Ask the user whether this should be a red line. Only if they explicitly agree, call write_persona again with "
                + "op=add and persona_section=red_lines.");

        var (scope, scopeRefusal) = ScopeOf(request, "propose");
        if (scopeRefusal is not null)
            return scopeRefusal;

        var problems = EntryProblems(text, source);
        if (problems.Count > 0)
            return ToolText.Refuse(problems, "Fix these and call write_persona op=propose again.");

        if (SecretCheck(request, text, source, ToolValues.WritePersonaOps.Propose) is { } secret)
            return secret;

        var relative = MemoryLayout.Candidates;
        var path = MemoryLayout.Full(config.MemoryPath, relative);
        var read = TextFile.Read(path);
        if (read is null)
            return NotUtf8(relative, "propose");
        var (existing, hasBom) = read.Value;

        var judged = await samePreference.RunAsync(text, sectionKind, cancellationToken);
        if (judged.Same is { } same
            && await AnswerSameAsync(same, source, existing, hasBom, relative, cancellationToken) is { } answer)
            return answer;

        var after = CandidatesFile.AppendCandidate(existing, text, sectionKind, scope, options.Today, source);
        var code = ShortCode.Of(ShortCode.Normalize(text));
        var reply = $"Recorded as a new candidate (scope: {scope})."
            + (judged.Note is { } note ? $"\n\n{note}" : "");
        return await WriteAsync(relative, after, hasBom, $"candidates: propose {code}", WrittenSource.Candidates, reply, cancellationToken);
    }

    /// <summary>
    /// Answers a proposal whose same-preference check found a match. Null when the match is not in the files as they are
    /// now, so the proposal is recorded as a new candidate instead.
    /// </summary>
    private async Task<ToolResult?> AnswerSameAsync(
        PreferenceMatch same, string source, string existing, bool hasBom, string relative, CancellationToken cancellationToken)
    {
        return same.Kind switch
        {
            UnitKinds.Candidate => await MergeCandidateAsync(same, source, existing, hasBom, relative, cancellationToken),
            UnitKinds.RejectedCandidate => RejectedReply(same, existing),
            UnitKinds.PersonaEntry => PersonaReply(same),
            _ => null,
        };
    }

    /// <summary>
    /// Appends the proposal's words as an occurrence of the matching candidate, once per day. The candidate keeps its own
    /// scope: the one the proposal names is not used for a merge.
    /// </summary>
    private async Task<ToolResult?> MergeCandidateAsync(
        PreferenceMatch same, string source, string existing, bool hasBom, string relative, CancellationToken cancellationToken)
    {
        var candidate = CandidatesDocument.Parse(existing).Candidates
            .FirstOrDefault(item => item.Text == same.Text && item.Section == same.Section && item.Scope == same.Scope);
        if (candidate is null)
            return null;

        var today = options.Today;
        if (candidate.Occurrences.Any(occurrence => occurrence.Day == today))
            return ToolText.Ok($"Already recorded today: \"{candidate.Text}\" (scope: {candidate.Scope}).");

        var after = CandidatesFile.AppendOccurrence(existing, candidate.Text, candidate.Section, candidate.Scope, today, source);
        if (after is null)
            return null;

        var code = ShortCode.Of(ShortCode.Normalize(candidate.Text));
        var reply = $"Merged into the candidate \"{candidate.Text}\" (scope: {candidate.Scope}).";
        return await WriteAsync(relative, after, hasBom, $"candidates: merge {code}", WrittenSource.Candidates, reply, cancellationToken);
    }

    /// <summary>The answer for a proposal on the rejection list: what was rejected, when and in what words.</summary>
    private ToolResult? RejectedReply(PreferenceMatch same, string existing)
    {
        var rejected = CandidatesDocument.Parse(existing).RejectionList
            .FirstOrDefault(item => item.Text == same.Text && item.Section == same.Section && item.Scope == same.Scope);
        if (rejected is null)
            return null;

        var occurrence = rejected.Occurrences.LastOrDefault(item => item.Rejected) ?? rejected.Occurrences.LastOrDefault();
        var when = occurrence is null ? "" : $" on {occurrence.Day:yyyy-MM-dd}, in the words 「{occurrence.Quote}」";
        var scope = rejected.Scope.IsGlobal ? "" : $", scope={rejected.Scope}";
        return ToolText.Ok(
            $"Not recorded: \"{rejected.Text}\" is on the rejection list, rejected{when}.\n"
            + $"Next step: do not propose it again in this session. If the user now clearly asks for it, call write_persona again "
            + $"with op=add, persona_section={SectionName(same.Section)}{scope} and their words as source.");
    }

    /// <summary>The answer for a proposal the persona already holds: the entry and its short code.</summary>
    private ToolResult? PersonaReply(PreferenceMatch same)
    {
        var read = TextFile.Read(MemoryLayout.Full(config.MemoryPath, MemoryLayout.Persona));
        if (read is null)
            return null;
        var persona = PersonaDocument.Parse(read.Value.Text);
        var entry = persona.EntriesIn(same.Scope, same.Section).FirstOrDefault(item => item.JudgmentText == same.Text);
        if (entry is null)
            return null;
        var code = persona.CodesByLine()[entry.Line];
        return ToolText.Ok($"Not recorded: the persona already has this as [{code}]: {entry.Text}\nNext step: nothing to record; do not propose it again.");
    }

    /// <summary>Hands the changed file to the writer and returns <paramref name="reply"/> with what the write adds to it.</summary>
    private async Task<ToolResult> WriteAsync(
        string relative, string text, bool hasBom, string subject, WrittenSource source, string reply, CancellationToken cancellationToken) =>
        (await writer.WriteAsync(new MemoryWrite(relative, text, hasBom, subject, source), cancellationToken)).Reply(reply);

    /// <summary>The name an agent gives a section.</summary>
    private string SectionName(PersonaSection kind) => Sections.Single(section => section.Kind == kind).Name;

    /// <summary>The section an agent named, or null for a name that is not one of the four.</summary>
    private PersonaSection? SectionOf(string name) =>
        Sections.Where(section => section.Name == name).Select(section => (PersonaSection?)section.Kind).FirstOrDefault();

    /// <summary>An entry's lines as the file holds them: its text, then the source comment dated today.</summary>
    private List<string> EntryLines(string text, string source) =>
    [
        $"- {text}",
        $"  <!-- {options.Today:yyyy-MM-dd} {StructureWords.Quote}「{source}」 -->",
    ];

    /// <summary>One entry as a refusal lists it: its short code, its text and any continuation lines.</summary>
    private static string Listed(PersonaEntry item, string code) =>
        string.Join("\n", new[] { $"- [{code}] {item.Text}" }.Concat(item.ContinuationWithoutComments));

    /// <summary>The parameters that were given but the op does not take.</summary>
    private static List<string> MisplacedParameters(WritePersonaRequest request, string[] allowed)
    {
        var given = new (string Name, bool IsGiven)[]
        {
            ("persona_section", request.Section is not null),
            ("text", request.Text is not null),
            ("source", request.Source is not null),
            ("hash", request.Hash is not null),
            ("scope", request.Scope is not null),
            ("trigger", request.Trigger is not null),
            ("confirm_no_secret", request.ConfirmNoSecret is not null),
        };
        return given.Where(parameter => parameter.IsGiven && !allowed.Contains(parameter.Name)).Select(parameter => parameter.Name).ToList();
    }

    /// <summary>What a sentence-form entry is missing, or names wrongly: its section, text and source.</summary>
    private List<string> MissingEntry(string section, string text, string source, PersonaSection? kind)
    {
        var reasons = new List<string>();
        if (section.Length == 0)
            reasons.Add("Missing parameter persona_section.");
        else if (kind is null)
            reasons.Add($"Parameter persona_section {ToolText.Quote(section)} is not a section; it must be one of: {SectionNames}.");
        if (text.Length == 0)
            reasons.Add("Missing parameter text.");
        if (source.Length == 0)
            reasons.Add("Missing parameter source.");
        return reasons;
    }

    /// <summary>
    /// The problems with an entry: one line each for text and source, and the source comment's end marker. The wording is
    /// the agent's, which the memory skill guides; no sentence form is enforced.
    /// </summary>
    private static List<string> EntryProblems(string text, string source)
    {
        var problems = new List<string>();
        if (text.IndexOfAny(TextFile.LineBreaks) >= 0)
            problems.Add("Text must be one line.");
        if (source.IndexOfAny(TextFile.LineBreaks) >= 0)
            problems.Add("Source must be one line.");
        if (source.Contains("-->", StringComparison.Ordinal))
            problems.Add("Source must not contain \"-->\", which would end its comment.");
        return problems;
    }

    /// <summary>
    /// The credential refusal for the text, source and trigger a write would store; null when there is none or the agent
    /// confirmed the write.
    /// </summary>
    private static ToolResult? SecretCheck(WritePersonaRequest request, string text, string source, string op, string? trigger = null)
    {
        if (request.ConfirmNoSecret == true)
            return null;
        List<WrittenText> written = [new("text", text), new("source", source)];
        if (!string.IsNullOrEmpty(trigger))
            written.Add(new("trigger", trigger));
        return CredentialCheck.Run(written, $"write_persona op={op}");
    }

    /// <summary>The entry of one of the four sections, in any scope, whose short code is <paramref name="hash"/>; null when none has it.</summary>
    private static FoundEntry? Find(PersonaDocument persona, string hash)
    {
        if (hash.Length == 0)
            return null;

        var codes = persona.CodesByLine();
        foreach (var entry in persona.KnownEntries())
        {
            if (string.Equals(codes[entry.Entry.Line], hash, StringComparison.OrdinalIgnoreCase))
                return new FoundEntry(entry.Scope, entry.Kind, entry.Entry, codes[entry.Entry.Line]);
        }

        return null;
    }

    /// <summary>" of domain x" or " of workstream x" for a scoped entry; empty for a global one.</summary>
    private static string ScopePhrase(PersonaScope scope) => scope.Kind switch
    {
        PersonaScopeKind.Domain => $" of domain {scope.Name}",
        PersonaScopeKind.Workstream => $" of workstream {scope.Name}",
        _ => "",
    };

    /// <summary>
    /// The count a reply states for the section or domain an entry is in, after <paramref name="delta"/> entries are added or
    /// removed. Empty for a workstream.
    /// </summary>
    private string CountNote(PersonaDocument persona, PersonaScope scope, PersonaSection kind, int delta)
    {
        if (scope.IsGlobal)
            return $"The section has {persona.Entries(kind).Count + delta} entries.";
        if (scope.Kind == PersonaScopeKind.Domain)
            return $"The domain has {(persona.BlockOf(scope)?.Entries.Count ?? 0) + delta} entries.";
        return "";
    }

    /// <summary>
    /// Refuses a short code that is missing or names no entry, with the reasons already found. The next step sends the agent to
    /// <c>recall kind=persona</c> for the codes, so the refusal itself carries no persona.
    /// </summary>
    private static ToolResult NoEntry(string hash, List<string> reasons, string op)
    {
        reasons.Insert(0, hash.Length == 0
            ? "Missing parameter hash."
            : $"Parameter hash {ToolText.Quote(hash)} matches no entry in the persona; the short code is stale or wrong.");
        return ToolText.Refuse(
            reasons,
            $"Call recall with kind=persona to read the short codes, then call write_persona again with op={op} and the short code of the entry.");
    }

    /// <summary>
    /// The refusal for an entry that conflicts with, duplicates or supersedes an existing one. The related content is that
    /// entry, so the agent can take it up with the user.
    /// </summary>
    private static ToolResult ConflictRefusal(ConflictingEntry conflict) => ToolText.Refuse(
        "The new entry conflicts with, duplicates or supersedes an existing persona entry.",
        $"Ask the user whether to change that entry instead. Only if they agree, call write_persona again with op=replace, "
        + $"hash={conflict.Code}, and the new text and source; otherwise leave the persona as it is.",
        $"{PersonaDocument.HeadingOf(conflict.Section)} section{ScopePhrase(conflict.Scope)}:\n{Listed(conflict.Entry, conflict.Code)}");

    /// <summary>A success reply, with the note that a skipped judgment adds at its end.</summary>
    private static string WithNote(string reply, string? note) => note is null ? reply : $"{reply}\n\n{note}";

    private static ToolResult NotUtf8(string relative, string op) => ToolText.Refuse(
        $"{relative} is not valid UTF-8, so writing to it would change its text.",
        $"Save {relative} as UTF-8, then call write_persona op={op} again.");

    /// <summary>An entry of one of the four sections, in its scope, found by its short code.</summary>
    private sealed record FoundEntry(PersonaScope Scope, PersonaSection Kind, PersonaEntry Entry, string Code);
}
