using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TacitMemory.Core;

namespace TacitMemory.Mcp;

/// <summary>The memory tools. Each one hands its arguments to <see cref="MemoryService"/>, which checks them.</summary>
[McpServerToolType]
internal sealed class MemoryTools(MemoryService service)
{
    [McpServerTool(Name = "recall", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [McpMeta("anthropic/alwaysLoad", true)]
    [Description("""
        Search the user's memory, or read one detail page in full. Give query to search, page to read one page, only workstream for that workstream's brief (its conventions, open items, pages and recent activity), or kind=persona / kind=rules alone.
        Results come per page with the matching sections in full and a short code for each section.
        kind=persona returns the whole persona, and kind=rules returns the rules of one domain (or of every domain), each entry with its short code. Neither searches: give kind alone, and domain only with rules.
        """)]
    public async Task<CallToolResult> Recall(
        RequestContext<CallToolRequestParams> request,
        [Description("What to look for, in natural language and in the memory language; names, terms and file names as written.")] string? query = null,
        [Description("Page ID to read in full: <workstream>/<slug>, for example my-app/db-upgrade.")] string? page = null,
        [Description("Search only this workstream.")] string? workstream = null,
        [Description("Earliest date, YYYY-MM-DD; convert phrases like \"last week\" into dates.")] string? from = null,
        [Description("Latest date, YYYY-MM-DD.")] string? to = null,
        [AllowedValues(ToolValues.RecallKinds.Pages, ToolValues.RecallKinds.Activities, ToolValues.RecallKinds.Rules, ToolValues.RecallKinds.Persona)]
        [Description("pages or activities, searched together when omitted; rules or persona to read the persona's rules or the whole persona instead of searching.")] string? kind = null,
        [Description("With kind=rules only: the domain label whose rules to return, as the persona names it; omitted, the rules of every domain are returned.")] string? domain = null,
        CancellationToken cancellationToken = default) =>
        Reply(await service.RecallAsync(
            new RecallRequest { Query = query, Page = page, Workstream = workstream, From = from, To = to, Kind = kind, Domain = domain, UnknownParameters = UnknownArguments.Of(request) },
            cancellationToken));

    [McpServerTool(Name = "write_page", ReadOnly = false, Idempotent = false, Destructive = true, OpenWorld = false)]
    [Description("""
        Write a detail page: one page per matter, organized in ## sections.
        op=create: page, title, type, aliases and sections; related is optional.
        op=upsert_section: page, section and body; hash (the section's short code from recall) when the section exists.
        op=delete_section: page, section and hash (the section's short code from recall).
        op=update_header: page and at least one of title, type, add_aliases, related; related replaces the whole list ([] clears it).
        type, by what the page records:
        - incident: a fault, its cause and its fix.
        - decision: one decision and the reasons for it.
        - entity: a standing system or tool, as pointers to its documentation, the user's stance and open to-dos.
        - work: one unit of work and its record, such as a slice or a release.
        Record where a credential is kept, never its value.
        """)]
    public async Task<CallToolResult> WritePage(
        RequestContext<CallToolRequestParams> request,
        [Required, AllowedValues(ToolValues.WritePageOps.Create, ToolValues.WritePageOps.UpsertSection, ToolValues.WritePageOps.DeleteSection, ToolValues.WritePageOps.UpdateHeader)]
        [Description("Operation: create, upsert_section, delete_section or update_header.")] string? op = null,
        [Required]
        [Description("Page ID: <workstream>/<slug>. workstream is the name of a continuing matter in lowercase, which need not be a code repository, or _topics for a subject that belongs to no workstream; slug is lowercase words joined by hyphens.")] string? page = null,
        [Description("Page title, one line.")] string? title = null,
        [AllowedValues(ToolValues.PageTypes.Incident, ToolValues.PageTypes.Decision, ToolValues.PageTypes.Entity, ToolValues.PageTypes.Work)]
        [Description("The page's type, one of the four described above; required by create.")] string? type = null,
        [Description("Other names the user might call this by: spoken phrasings, Chinese and English names, nicknames. At least {minAliases}.")] string[]? aliases = null,
        [Description("The page's sections in order; each has a name (the ## heading, without '##') and a Markdown body.")] PageSectionInput[]? sections = null,
        [Description("Page IDs of related pages; each must exist.")] string[]? related = null,
        [Description("The section's name: its ## heading text, without '##'; one line.")] string? section = null,
        [Description("The section's Markdown body; no '#' or '##' headings, use '###' or deeper.")] string? body = null,
        [Description("Short code in brackets from the latest recall; required to replace or delete an existing section.")] string? hash = null,
        [Description("Aliases to add to the page; ones it already has are ignored.")] string[]? add_aliases = null,
        [Description("Set true only when the user confirms that the refused text holds no secret; skips the secret check.")] bool? confirm_no_secret = null,
        CancellationToken cancellationToken = default) =>
        Reply(await service.WritePageAsync(new WritePageRequest
        {
            Op = op,
            Page = page,
            Title = title,
            Type = type,
            Aliases = aliases,
            Sections = sections,
            Related = related,
            Section = section,
            Body = body,
            Hash = hash,
            AddAliases = add_aliases,
            ConfirmNoSecret = confirm_no_secret,
            UnknownParameters = UnknownArguments.Of(request),
        }, cancellationToken));

    [McpServerTool(Name = "write_recent", ReadOnly = false, Idempotent = false, Destructive = true, OpenWorld = false)]
    [Description("""
        Record an activity: one line in the activity log.
        text is required: one line that does not start with '['; workstream and source are optional.
        With replaces (a short code of today's activity), rewrites that activity in place instead of adding one; source then replaces its comment only if given.
        The mainline is not written here: the tacit-consolidate skill maintains it.
        Record where a credential is kept, never its value.
        """)]
    public async Task<CallToolResult> WriteRecent(
        RequestContext<CallToolRequestParams> request,
        [Required]
        [Description("The activity in one line, in the memory language; without a [workstream] prefix, which the workstream parameter adds.")] string? text = null,
        [Description("The workstream the activity belongs to: the name of a continuing matter in lowercase, which need not be a code repository, for example my-app.")] string? workstream = null,
        [Description("The user's own words behind the activity, one line; kept as a comment under the entry.")] string? source = null,
        [Description("The short code of an activity from today to rewrite with this text, from a refusal's list or from recall. Only today's activities match.")] string? replaces = null,
        [Description("Set true only when the user confirms that the refused text holds no secret; skips the secret check.")] bool? confirm_no_secret = null,
        CancellationToken cancellationToken = default) =>
        Reply(await service.WriteRecentAsync(new WriteRecentRequest
        {
            Text = text,
            Workstream = workstream,
            Source = source,
            Replaces = replaces,
            ConfirmNoSecret = confirm_no_secret,
            UnknownParameters = UnknownArguments.Of(request),
        }, cancellationToken));

    [McpServerTool(Name = "write_persona", ReadOnly = false, Idempotent = false, Destructive = true, OpenWorld = false)]
    [Description("""
        Change the user's persona; it loads into every session from the next one.
        op=add: needs persona_section, text and source; scope, and trigger for a new domain. Only after the user agrees.
        op=propose: needs persona_section, text and source, and takes scope; for a habit you observed, kept as a candidate for the next consolidation. Never propose a red line: only the user sets one.
        op=replace: needs hash, text and source.
        op=delete: needs hash only.
        hash is the entry's short code, from recall kind=persona.
        scope: global (the default), workstream:<name> or domain:<label>; names use lowercase letters, digits, '.', '_' and '-'. Reuse an existing domain label exactly.
        trigger: one line saying when a new domain applies; ignored for an existing domain.
        persona_section is red_lines, working_rules, preferences or profile. Sentence form of text: red_lines "Don't <behaviour>; instead <alternative>"; working_rules "<situation> → <action>"; preferences "<dimension>: <description>"; profile "<field>: <fact>".
        text is one line, in the memory language; source is the user's own words behind it, one line.
        Record where a credential is kept, never its value.
        """)]
    public async Task<CallToolResult> WritePersona(
        RequestContext<CallToolRequestParams> request,
        [Required, AllowedValues(ToolValues.WritePersonaOps.Add, ToolValues.WritePersonaOps.Propose, ToolValues.WritePersonaOps.Replace, ToolValues.WritePersonaOps.Delete)]
        [Description("Operation: add, propose, replace or delete.")] string? op = null,
        [AllowedValues(ToolValues.PersonaSections.RedLines, ToolValues.PersonaSections.WorkingRules, ToolValues.PersonaSections.Preferences, ToolValues.PersonaSections.Profile)]
        [Description("The section: red_lines, working_rules, preferences or profile. Used by add and propose only.")] string? persona_section = null,
        [Description("The entry in one line, in the memory language and in its section's sentence form.")] string? text = null,
        [Description("The user's own words behind the entry, one line; kept as a comment under it.")] string? source = null,
        [Description("Short code of the entry to replace or delete, from recall kind=persona or an earlier reply; required by both. Finds an entry in any scope. Not used by add or propose.")] string? hash = null,
        [Description("Scope of the entry: global (the default), workstream:<name> or domain:<label>. Used by add and propose.")] string? scope = null,
        [Description("Trigger of a new domain: one line saying when the domain applies. Used by add only; required when add creates the domain, ignored for an existing one.")] string? trigger = null,
        [Description("Set true only when the user confirms that the refused text holds no secret; skips the secret check.")] bool? confirm_no_secret = null,
        CancellationToken cancellationToken = default) =>
        Reply(await service.WritePersonaAsync(new WritePersonaRequest
        {
            Op = op,
            Section = persona_section,
            Text = text,
            Source = source,
            Hash = hash,
            Scope = scope,
            Trigger = trigger,
            ConfirmNoSecret = confirm_no_secret,
            UnknownParameters = UnknownArguments.Of(request),
        }, cancellationToken));

    private static CallToolResult Reply(ToolResult result) =>
        new() { Content = [new TextContentBlock { Text = result.Text }], IsError = result.IsError };
}
