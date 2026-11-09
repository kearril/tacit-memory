using Microsoft.Extensions.Logging;
using TacitMemory.Core.Logging;
using System.Globalization;
using System.Text.RegularExpressions;
using TacitMemory.Core.Decisions;
using TacitMemory.Core.Git;
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
internal sealed class PersonaTools(TacitOptions options, TacitConfig config, ServiceLog log, SamePreferenceCheck samePreference, RuleConflictCheck conflicts)
{
    /// <summary>Characters an editor shows as a line break, so none may appear in a text or source; the parser splits on LF only.</summary>
    private static readonly char[] LineBreaks = ['\r', '\n', '\u2028', '\u2029', '\u0085'];

    /// <summary>The parameters the propose op takes; any other parameter given with it is refused rather than ignored.</summary>
    private static readonly string[] EntryParameters = ["persona_section", "text", "source", "confirm_no_secret"];

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

    private readonly GitCli _git = new(config.MemoryPath, config.Language);

    /// <summary>The four sections as an agent names them, and the limit the config sets for each.</summary>
    private readonly (PersonaSection Kind, string Name, int Limit)[] Sections =
    [
        (PersonaSection.RedLines, ToolValues.PersonaSections.RedLines, config.Limits.Persona.RedLines),
        (PersonaSection.WorkingRules, ToolValues.PersonaSections.WorkingRules, config.Limits.Persona.WorkingRules),
        (PersonaSection.Preferences, ToolValues.PersonaSections.Preferences, config.Limits.Persona.Preferences),
        (PersonaSection.Profile, ToolValues.PersonaSections.Profile, config.Limits.Persona.Profile),
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

        var scopeText = string.IsNullOrWhiteSpace(request.Scope) ? "global" : request.Scope.Trim();
        if (!PersonaScope.TryParse(scopeText, out var scope))
            return ToolText.Refuse(
                $"Parameter scope {ToolText.Quote(scopeText)} is not a scope: it must be global, project:<name> or domain:<label>, "
                + "and the name uses only lowercase letters, digits, '.', '_' and '-'.",
                "Call write_persona again with op=add and scope set to global, project:<name> or domain:<label>.");

        var problems = EntryProblems(text, source, sectionKind);
        if (problems.Count > 0)
            return ToolText.Refuse(problems, "Fix these and call write_persona op=add again.");

        if (SecretCheck(request, text, source, ToolValues.WritePersonaOps.Add, trigger) is { } secret)
            return secret;

        var relative = PersonaFiles.Persona;
        var path = Path.Combine(config.MemoryPath, relative);
        var read = TextFile.Read(path);
        if (read is null)
            return NotUtf8(relative, "add");
        var (existing, hasBom) = read.Value;

        var persona = PersonaDocument.Parse(existing);
        var name = PersonaDocument.HeadingOf(sectionKind).En;
        if (LimitRefusal(persona, scope, trigger, sectionKind) is { } limited)
            return limited;

        var judgment = await conflicts.RunAsync(text, sectionKind, persona, null, cancellationToken);
        if (judgment.Conflict is { } conflict)
            return ConflictRefusal(conflict);

        // Checked before the write: a file that already had user edits gets that noted in its commit subject.
        var dirty = File.Exists(path) && await _git.IsDirtyAsync(relative, cancellationToken);

        var entry = EntryLines(text, source);
        // The trigger is written only when this write creates the domain; an existing domain keeps its own.
        var createdTrigger = scope.Kind == PersonaScopeKind.Domain && persona.BlockOf(scope) is null ? trigger : null;
        var (after, line) = scope.IsGlobal
            ? PersonaFile.Append(existing, sectionKind, entry, config.Language)
            : PersonaFile.AppendToBlock(existing, scope, createdTrigger, sectionKind, entry, config.Language);
        var code = PersonaDocument.Parse(after).CodesByLine()[line];

        TextFile.Write(path, after, hasBom);
        var subject = dirty ? $"persona: add {code} (with user edits)" : $"persona: add {code}";
        var reply = scope.IsGlobal
            ? WithNote($"Added to the {name} section as [{code}]. The section now has {persona.Entries(sectionKind).Count + 1} of {LimitOf(sectionKind)} entries.", judgment.Note)
            : WithNote(AddedToScopeReply(persona, scope, sectionKind, code, createdTrigger), judgment.Note);
        return await CommitAndReplyAsync(reply, subject, relative, cancellationToken);
    }

    /// <summary>
    /// The refusal a write into <paramref name="scope"/> gets from the limits it falls under, or from the trigger a new domain
    /// needs; null when it may go ahead. Global sections and a domain's entries are capped, and a new domain counts against the
    /// domain limit. A project has no limit.
    /// </summary>
    private ToolResult? LimitRefusal(PersonaDocument persona, PersonaScope scope, string trigger, PersonaSection kind)
    {
        if (scope.IsGlobal)
        {
            var entries = persona.Entries(kind);
            var max = LimitOf(kind);
            if (entries.Count < max)
                return null;
            var codes = persona.CodesByLine();
            var name = PersonaDocument.HeadingOf(kind).En;
            return ToolText.Refuse(
                $"The {name} section already has {entries.Count} entries; the limit is {max}.",
                "Ask the user which entry to replace or delete. With their agreement, call write_persona again with op=replace "
                + "or op=delete and that entry's short code first; then add again.",
                $"{name}, {entries.Count} of {max} entries:\n"
                + string.Join("\n", entries.Select(item => Listed(item, codes[item.Line]))));
        }

        if (scope.Kind == PersonaScopeKind.Project)
            return null;

        if (persona.BlockOf(scope) is { } block)
        {
            var max = config.Limits.DomainEntries;
            if (block.Entries.Count < max)
                return null;
            var codes = persona.CodesByLine();
            return ToolText.Refuse(
                $"The domain {scope.Name} already has {block.Entries.Count} entries; the limit is {max}.",
                "Ask the user which entry of this domain to replace or delete. With their agreement, call write_persona again with "
                + "op=replace or op=delete and that entry's short code first; then add again.",
                $"Domain {scope.Name}, {block.Entries.Count} of {max} entries:\n"
                + string.Join("\n", block.Entries.Select(item => Listed(item, codes[item.Line]))));
        }

        var domains = persona.Blocks.Where(item => item.Scope.Kind == PersonaScopeKind.Domain).ToList();
        if (domains.Count >= config.Limits.Domains)
            return ToolText.Refuse(
                $"The persona already has {domains.Count} domains; the limit is {config.Limits.Domains}, so domain {scope.Name} is not created.",
                "Ask the user whether to merge this into one of the domains listed below or to remove one. Then call write_persona "
                + "again with op=add and scope set to an existing domain, and no trigger.",
                string.Join("\n", domains.Select(domain => $"Domain {domain.Scope.Name}, {domain.Entries.Count} entries, trigger: {domain.Trigger ?? "none"}")));

        var triggerLimit = config.Limits.TriggerLength;
        var reasons = new List<string>();
        if (trigger.Length == 0)
            reasons.Add($"Domain {scope.Name} is new, so it needs a trigger: one line saying when the domain applies.");
        else
        {
            if (trigger.IndexOfAny(LineBreaks) >= 0)
                reasons.Add("Trigger must be one line.");
            if (Length(trigger) > triggerLimit)
                reasons.Add($"Trigger has {Length(trigger)} characters; the limit is {triggerLimit}.");
        }

        return reasons.Count == 0
            ? null
            : ToolText.Refuse(
                reasons,
                $"Call write_persona again with op=add, scope={scope}, the same persona_section, text and source, and trigger set to "
                + $"one line of at most {triggerLimit} characters.");
    }

    /// <summary>The reply for an entry added to a domain or a project: where it went, the domain's count, and the trigger of a new domain.</summary>
    private string AddedToScopeReply(PersonaDocument persona, PersonaScope scope, PersonaSection kind, string code, string? createdTrigger)
    {
        var name = PersonaDocument.HeadingOf(kind).En;
        if (scope.Kind == PersonaScopeKind.Project)
            return $"Added to the {name} section of project {scope.Name} as [{code}].";

        var count = (persona.BlockOf(scope)?.Entries.Count ?? 0) + 1;
        var created = createdTrigger is null ? "" : $" The domain was created with trigger: {createdTrigger}";
        return $"Added to the {name} section of domain {scope.Name} as [{code}]. The domain has {count} of {config.Limits.DomainEntries} entries.{created}";
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

        var relative = PersonaFiles.Persona;
        var path = Path.Combine(config.MemoryPath, relative);
        var read = TextFile.Read(path);
        if (read is null)
            return NotUtf8(relative, "replace");
        var (existing, hasBom) = read.Value;

        var persona = PersonaDocument.Parse(existing);
        if (Find(persona, hash) is not { } entry)
            return NoEntry(hash, reasons, "replace");

        // The sentence form is checked against the section the replaced entry is in.
        if (text.Length > 0 && source.Length > 0)
            reasons.AddRange(EntryProblems(text, source, entry.Kind));
        if (reasons.Count > 0)
            return ToolText.Refuse(reasons, "Fix these and call write_persona op=replace again with the same hash.");

        if (SecretCheck(request, text, source, ToolValues.WritePersonaOps.Replace) is { } secret)
            return secret;

        var name = PersonaDocument.HeadingOf(entry.Kind).En;
        var lines = EntryLines(text, source);
        var after = PersonaFile.ReplaceEntry(existing, entry.Item, lines);
        if (after == existing)
            return ToolText.Ok($"[{entry.Code}] already reads exactly so in the {name} section; nothing was written.");

        var judgment = await conflicts.RunAsync(text, entry.Kind, persona, entry.Item, cancellationToken);
        if (judgment.Conflict is { } conflict)
            return ConflictRefusal(conflict);

        // Checked before the write: a file that already had user edits gets that noted in its commit subject.
        var dirty = await _git.IsDirtyAsync(relative, cancellationToken);
        TextFile.Write(path, after, hasBom);
        var code = PersonaDocument.Parse(after).CodesByLine()[entry.Item.Line];
        var subject = dirty ? $"persona: replace {code} (with user edits)" : $"persona: replace {code}";
        var reply = WithNote(
            $"Replaced [{entry.Code}] with [{code}] in the {name} section{ScopePhrase(entry.Scope)}. "
            + CountNote(persona, entry.Scope, entry.Kind, 0),
            judgment.Note);
        return await CommitAndReplyAsync(reply, subject, relative, cancellationToken);
    }

    /// <summary>
    /// Deletes the entry the short code names, with its continuation lines and source comment, and returns its text.
    /// The limit does not apply: a section over it can be taken down.
    /// </summary>
    private async Task<ToolResult> DeleteAsync(WritePersonaRequest request, CancellationToken cancellationToken)
    {
        var hash = (request.Hash ?? "").Trim();
        var relative = PersonaFiles.Persona;
        var path = Path.Combine(config.MemoryPath, relative);
        var read = TextFile.Read(path);
        if (read is null)
            return NotUtf8(relative, "delete");
        var (existing, hasBom) = read.Value;

        var persona = PersonaDocument.Parse(existing);
        if (Find(persona, hash) is not { } entry)
            return NoEntry(hash, [], "delete");

        var name = PersonaDocument.HeadingOf(entry.Kind).En;
        // Checked before the write: a file that already had user edits gets that noted in its commit subject.
        var dirty = await _git.IsDirtyAsync(relative, cancellationToken);
        var after = PersonaFile.ReplaceEntry(existing, entry.Item, []);
        TextFile.Write(path, after, hasBom);
        var subject = dirty ? $"persona: delete {entry.Code} (with user edits)" : $"persona: delete {entry.Code}";
        var reply = $"Deleted [{entry.Code}] from the {name} section{ScopePhrase(entry.Scope)}. It read:\n{Listed(entry.Item, entry.Code)}\n"
            + CountNote(persona, entry.Scope, entry.Kind, -1);
        return await CommitAndReplyAsync(reply, subject, relative, cancellationToken);
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

        var problems = EntryProblems(text, source, sectionKind);
        if (problems.Count > 0)
            return ToolText.Refuse(problems, "Fix these and call write_persona op=propose again.");

        if (SecretCheck(request, text, source, ToolValues.WritePersonaOps.Propose) is { } secret)
            return secret;

        var relative = PersonaFiles.Candidates;
        var path = Path.Combine(config.MemoryPath, relative);
        var read = TextFile.Read(path);
        if (read is null)
            return NotUtf8(relative, "propose");
        var (existing, hasBom) = read.Value;

        // Checked before the write: a file that already had user edits gets that noted in its commit subject.
        var dirty = File.Exists(path) && await _git.IsDirtyAsync(relative, cancellationToken);

        var judged = await samePreference.RunAsync(text, sectionKind, cancellationToken);
        if (judged.Same is { } same
            && await AnswerSameAsync(same, source, existing, hasBom, dirty, path, relative, cancellationToken) is { } answer)
            return answer;

        var after = CandidatesFile.AppendCandidate(existing, text, sectionKind, options.Today, source, config.Language);
        TextFile.Write(path, after, hasBom);

        var code = ShortCode.Of(ShortCode.Normalize(text));
        var subject = dirty ? $"candidates: propose {code} (with user edits)" : $"candidates: propose {code}";
        var minDays = config.CandidateMinDays;
        var reply = $"Recorded as a candidate: it has appeared on 1/{minDays} days."
            + ConsolidationNote(1, minDays)
            + (judged.Note is { } note ? $"\n\n{note}" : "");
        return await CommitAndReplyAsync(reply, subject, relative, cancellationToken);
    }

    /// <summary>
    /// Answers a proposal whose same-preference check found a match. Null when the match is not in the files as they are
    /// now, so the proposal is recorded as a new candidate instead.
    /// </summary>
    private async Task<ToolResult?> AnswerSameAsync(
        PreferenceMatch same, string source, string existing, bool hasBom, bool dirty, string path, string relative, CancellationToken cancellationToken)
    {
        return same.Kind switch
        {
            UnitKinds.Candidate => await MergeCandidateAsync(same, source, existing, hasBom, dirty, path, relative, cancellationToken),
            UnitKinds.RejectedCandidate => RejectedReply(same, existing),
            UnitKinds.PersonaEntry => PersonaReply(same),
            _ => null,
        };
    }

    /// <summary>Appends the proposal's words as an occurrence of the matching candidate, once per day.</summary>
    private async Task<ToolResult?> MergeCandidateAsync(
        PreferenceMatch same, string source, string existing, bool hasBom, bool dirty, string path, string relative, CancellationToken cancellationToken)
    {
        var candidate = CandidatesDocument.Parse(existing).Candidates
            .FirstOrDefault(item => item.Text == same.Text && item.Section == same.Section);
        if (candidate is null)
            return null;

        var today = options.Today;
        var days = candidate.Occurrences.Select(occurrence => occurrence.Day).Distinct().Count();
        var minDays = config.CandidateMinDays;
        if (candidate.Occurrences.Any(occurrence => occurrence.Day == today))
            return ToolText.Ok($"Already recorded today: \"{candidate.Text}\" has appeared on {days}/{minDays} days." + ConsolidationNote(days, minDays));

        var after = CandidatesFile.AppendOccurrence(existing, candidate.Text, candidate.Section, today, source, config.Language);
        if (after is null)
            return null;
        TextFile.Write(path, after, hasBom);

        var code = ShortCode.Of(ShortCode.Normalize(candidate.Text));
        var subject = dirty ? $"candidates: merge {code} (with user edits)" : $"candidates: merge {code}";
        var reply = $"Merged into the candidate \"{candidate.Text}\", which has appeared on {days + 1}/{minDays} days."
            + ConsolidationNote(days + 1, minDays);
        return await CommitAndReplyAsync(reply, subject, relative, cancellationToken);
    }

    /// <summary>The answer for a proposal on the rejection list: what was rejected, when and in what words.</summary>
    private ToolResult? RejectedReply(PreferenceMatch same, string existing)
    {
        var rejected = CandidatesDocument.Parse(existing).RejectionList
            .FirstOrDefault(item => item.Text == same.Text && item.Section == same.Section);
        if (rejected is null)
            return null;

        var occurrence = rejected.Occurrences.LastOrDefault(item => item.Rejected) ?? rejected.Occurrences.LastOrDefault();
        var when = occurrence is null ? "" : $" on {occurrence.Day:yyyy-MM-dd}, in the words 「{occurrence.Quote}」";
        return ToolText.Ok(
            $"Not recorded: \"{rejected.Text}\" is on the rejection list, rejected{when}.\n"
            + $"Next step: do not propose it again in this session. If the user now clearly asks for it, call write_persona again "
            + $"with op=add, persona_section={SectionName(same.Section)} and their words as source.");
    }

    /// <summary>The answer for a proposal the persona already holds: the entry and its short code.</summary>
    private ToolResult? PersonaReply(PreferenceMatch same)
    {
        var read = TextFile.Read(Path.Combine(config.MemoryPath, PersonaFiles.Persona));
        if (read is null)
            return null;
        var persona = PersonaDocument.Parse(read.Value.Text);
        var entry = persona.Entries(same.Section).FirstOrDefault(item => item.JudgmentText == same.Text);
        if (entry is null)
            return null;
        var code = persona.CodesByLine()[entry.Line];
        return ToolText.Ok($"Not recorded: the persona already has this as [{code}]: {entry.Text}\nNext step: nothing to record; do not propose it again.");
    }

    /// <summary>Commits the file a write changed and returns the reply; a failed commit is noted, and the file stays written.</summary>
    private async Task<ToolResult> CommitAndReplyAsync(string reply, string subject, string relative, CancellationToken cancellationToken)
    {
        var commitProblem = await CommitAsync(subject, relative, cancellationToken);
        return commitProblem is null
            ? ToolText.Ok(reply) with { Written = true }
            : new ToolResult($"{reply}\n\n{commitProblem}", IsError: true, Written: true);
    }

    /// <summary>Commits the persona file; on failure, tells the user and returns the note for the agent. The file stays written.</summary>
    private async Task<string?> CommitAsync(string message, string relativePath, CancellationToken cancellationToken)
    {
        try
        {
            await _git.CommitAsync(message, [relativePath], cancellationToken);
            return null;
        }
        catch (TacitException e) when (e.Error == TacitError.GitFailed)
        {
            log.Notify(LogLevel.Warning, e.Message);
            return $"Not committed: the file is saved, but git failed ({e.Message}). Nothing to redo: it is committed with the next write to this file.";
        }
    }

    /// <summary>The limit the config sets for a section.</summary>
    private int LimitOf(PersonaSection kind) => Sections.Single(section => section.Kind == kind).Limit;

    /// <summary>The name an agent gives a section.</summary>
    private string SectionName(PersonaSection kind) => Sections.Single(section => section.Kind == kind).Name;

    /// <summary>The section an agent named, or null for a name that is not one of the four.</summary>
    private PersonaSection? SectionOf(string name) =>
        Sections.Where(section => section.Name == name).Select(section => (PersonaSection?)section.Kind).FirstOrDefault();

    /// <summary>Whether the text contains the structure word in either language: Chinese as written, English as a whole word.</summary>
    internal static bool HasStructureWord(string text, Localized word) =>
        text.Contains(word.Zh, StringComparison.Ordinal)
        || Regex.IsMatch(text, $@"\b{Regex.Escape(word.En)}\b", RegexOptions.IgnoreCase);

    /// <summary>An entry's lines as the file holds them: its text, then the source comment dated today.</summary>
    private List<string> EntryLines(string text, string source) =>
    [
        $"- {text}",
        $"  <!-- {options.Today:yyyy-MM-dd} {StructureWords.Quote.In(config.Language)}「{source}」 -->",
    ];

    /// <summary>One entry as a refusal lists it: its short code, its text and any continuation lines.</summary>
    private static string Listed(PersonaItem item, string code) =>
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
    /// The problems with an entry written into <paramref name="section"/>: one line each for text and source, the item
    /// length limit, the sentence form of red lines and working rules, and the source comment's end marker.
    /// </summary>
    private List<string> EntryProblems(string text, string source, PersonaSection section)
    {
        var problems = new List<string>();
        var limit = config.Limits.ItemLength;
        if (text.IndexOfAny(LineBreaks) >= 0)
            problems.Add("Text must be one line.");
        if (Length(text) > limit)
            problems.Add($"Text has {Length(text)} characters; the limit is {limit}.");
        if (section == PersonaSection.RedLines && !HasStructureWord(text, StructureWords.Instead))
            problems.Add(
                "A red line must say what to do instead: name the alternative after \"instead\" (or \"改为\"), "
                + "for example: Don't publish a release without asking; instead ask the user first.");
        if (section == PersonaSection.WorkingRules && !text.Contains('→'))
            problems.Add(
                "A working rule must have the form situation → action, with → between them, "
                + "for example: When the request is unclear → ask first, then plan.");
        if (source.IndexOfAny(LineBreaks) >= 0)
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
            if (string.Equals(codes[entry.Item.Line], hash, StringComparison.OrdinalIgnoreCase))
                return new FoundEntry(entry.Scope, entry.Kind, entry.Item, codes[entry.Item.Line]);
        }

        return null;
    }

    /// <summary>" of domain x" or " of project x" for a scoped entry; empty for a global one.</summary>
    private static string ScopePhrase(PersonaScope scope) => scope.Kind switch
    {
        PersonaScopeKind.Domain => $" of domain {scope.Name}",
        PersonaScopeKind.Project => $" of project {scope.Name}",
        _ => "",
    };

    /// <summary>
    /// The count a reply states for the section or domain an entry is in, after <paramref name="delta"/> entries are added or
    /// removed. Empty for a project, which has no limit.
    /// </summary>
    private string CountNote(PersonaDocument persona, PersonaScope scope, PersonaSection kind, int delta)
    {
        if (scope.IsGlobal)
            return $"The section has {persona.Entries(kind).Count + delta} of {LimitOf(kind)} entries.";
        if (scope.Kind == PersonaScopeKind.Domain)
            return $"The domain has {(persona.BlockOf(scope)?.Entries.Count ?? 0) + delta} of {config.Limits.DomainEntries} entries.";
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
        $"{PersonaDocument.HeadingOf(conflict.Section).En} section:\n{Listed(conflict.Item, conflict.Code)}");

    /// <summary>A success reply, with the note that a skipped judgment adds at its end.</summary>
    private static string WithNote(string reply, string? note) => note is null ? reply : $"{reply}\n\n{note}";

    /// <summary>The note a candidate at or past the minimum number of days gets: it can be confirmed at the next consolidation.</summary>
    private static string ConsolidationNote(int days, int minDays) => days >= minDays ? " It can be confirmed at the next consolidation." : "";

    private static ToolResult NotUtf8(string relative, string op) => ToolText.Refuse(
        $"{relative} is not valid UTF-8, so writing to it would change its text.",
        $"Save {relative} as UTF-8, then call write_persona op={op} again.");

    /// <summary>An entry of one of the four sections, in its scope, found by its short code.</summary>
    private sealed record FoundEntry(PersonaScope Scope, PersonaSection Kind, PersonaItem Item, string Code);

    internal static int Length(string text) => new StringInfo(text).LengthInTextElements;
}
