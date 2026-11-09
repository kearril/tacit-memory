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
        Search the user's memory, or read one detail page in full. Give exactly one of query and page. Give only project to get that project's brief.
        Results come per page with the matching sections in full and a short code for each section.
        """)]
    public async Task<CallToolResult> Recall(
        RequestContext<CallToolRequestParams> request,
        [Description("What to look for, in natural language and in the memory language; names, terms and file names as written.")] string? query = null,
        [Description("Page ID to read in full: <project>/<slug>, for example my-app/db-upgrade.")] string? page = null,
        [Description("Search only this project.")] string? project = null,
        [Description("Earliest date, YYYY-MM-DD; convert phrases like \"last week\" into dates.")] string? from = null,
        [Description("Latest date, YYYY-MM-DD.")] string? to = null,
        [AllowedValues(ToolValues.RecallKinds.Pages, ToolValues.RecallKinds.Activities)]
        [Description("pages or activities; both when omitted.")] string? kind = null,
        CancellationToken cancellationToken = default) =>
        Reply(await service.RecallAsync(
            new RecallRequest { Query = query, Page = page, Project = project, From = from, To = to, Kind = kind, UnknownParameters = UnknownArguments.Of(request) },
            cancellationToken));

    [McpServerTool(Name = "write_page", ReadOnly = false, Idempotent = false, Destructive = true, OpenWorld = false)]
    [Description("""
        Write a detail page: one page per matter, organized in ## sections. Pages are committed to the memory repository.
        op=create: new page; needs page, title, type, aliases, sections; related is optional.
        op=upsert_section: needs page, section and body; hash is required when the section exists (its short code from recall).
        op=delete_section: needs page, section and hash (its short code from recall).
        op=update_header: needs page and at least one of title, type, add_aliases, related; related replaces the whole list ([] clears it).
        type is one of these, by what the page records:
        - incident: a fault, its cause and its fix.
        - decision: one decision and the reasons for it.
        - entity: a standing system or tool, as pointers to its documentation, the user's stance and open to-dos.
        - work: one unit of work and its record, such as a slice or a release.
        Writes are checked for secrets (passwords, keys, tokens); never store one.
        """)]
    public async Task<CallToolResult> WritePage(
        RequestContext<CallToolRequestParams> request,
        [Required, AllowedValues(ToolValues.WritePageOps.Create, ToolValues.WritePageOps.UpsertSection, ToolValues.WritePageOps.DeleteSection, ToolValues.WritePageOps.UpdateHeader)]
        [Description("Operation: create, upsert_section, delete_section or update_header.")] string? op = null,
        [Required]
        [Description("Page ID: <project>/<slug>. project is the code repository's name in lowercase, or _topics for a subject that belongs to no project; slug is lowercase words joined by hyphens.")] string? page = null,
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
        [Description("true only after a write was refused as a possible secret and you made sure with the user that it holds none; skips the secret check.")] bool? confirm_no_secret = null,
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
        Record an activity in the recent activity log: one line under the day's heading in the month's file.
        text is required: one line of at most {itemLength} characters that does not start with '['; project and source are optional.
        With replaces (a short code of today's activity), rewrites that activity in place instead of adding one; source then replaces its comment only if given.
        The mainline is not written here: the tacit-consolidate skill maintains it.
        Writes are checked for secrets (passwords, keys, tokens); never store one.
        """)]
    public async Task<CallToolResult> WriteRecent(
        RequestContext<CallToolRequestParams> request,
        [Required]
        [Description("The activity in one line, in the memory language; without a [project] prefix, which the project parameter adds.")] string? text = null,
        [Description("The project the activity belongs to: the code repository's name in lowercase, for example my-app.")] string? project = null,
        [Description("The user's own words behind the activity, one line; kept as a comment under the entry.")] string? source = null,
        [Description("The short code of an activity from today to rewrite with this text, from a refusal's list or from recall. Only today's activities match.")] string? replaces = null,
        [Description("true only after a write was refused as a possible secret and you made sure with the user that it holds none; skips the secret check.")] bool? confirm_no_secret = null,
        CancellationToken cancellationToken = default) =>
        Reply(await service.WriteRecentAsync(new WriteRecentRequest
        {
            Text = text,
            Project = project,
            Source = source,
            Replaces = replaces,
            ConfirmNoSecret = confirm_no_secret,
            UnknownParameters = UnknownArguments.Of(request),
        }, cancellationToken));

    [McpServerTool(Name = "write_persona", ReadOnly = false, Idempotent = false, Destructive = true, OpenWorld = false)]
    [Description("""
        Change the user's persona: one line in one of four sections, rendered into the resident context from the next session.
        op=add: needs persona_section, text and source; the entry is appended to the end of that section in its scope.
        scope: global (the default), project:<name> or domain:<label>; the name uses lowercase letters, digits, '.', '_' and '-'.
        A domain holds rules that apply only in some situations, and its trigger says when they apply. Reuse an existing domain
        label exactly and do not invent a near-duplicate; a refusal lists the existing domains. A new domain needs trigger: one
        line saying when it applies, at most {triggerLength} characters; trigger is ignored for an existing domain.
        A domain holds at most {domainEntries} entries and the persona at most {domains} domains; a refusal lists them.
        op=propose: needs persona_section, text and source, and takes scope as add does; the entry is recorded as a candidate in candidates.md, not in the persona, and is confirmed later at a consolidation. Never propose a red line: only the user sets one. A preference already a candidate is merged into it; one on the rejection list or already in the persona is not recorded, and the reply says so.
        op=replace: needs hash, text and source; rewrites the entry with that short code in place, in its section.
        op=delete: needs hash only; removes the entry with that short code.
        A missing or stale hash is refused with the whole persona and every entry's short code; take the code from there.
        persona_section is red_lines, working_rules, preferences or profile. Sentence form of text, by section:
        red_lines "Don't <behaviour>; instead <alternative>" (the alternative is required); working_rules "<situation> → <action>";
        preferences "<dimension>: <description>"; profile "<field>: <fact>".
        text is one line within the entry length limit ({itemLength} characters), in the memory language; source is the user's own words behind it, one line.
        A section holds a limited number of entries (checked on add only); a refusal lists them with their short codes. Candidates have no limit.
        Writes are checked for secrets (passwords, keys, tokens); never store one.
        """)]
    public async Task<CallToolResult> WritePersona(
        RequestContext<CallToolRequestParams> request,
        [Required, AllowedValues(ToolValues.WritePersonaOps.Add, ToolValues.WritePersonaOps.Propose, ToolValues.WritePersonaOps.Replace, ToolValues.WritePersonaOps.Delete)]
        [Description("Operation: add, propose, replace or delete.")] string? op = null,
        [AllowedValues(ToolValues.PersonaSections.RedLines, ToolValues.PersonaSections.WorkingRules, ToolValues.PersonaSections.Preferences, ToolValues.PersonaSections.Profile)]
        [Description("The section: red_lines, working_rules, preferences or profile. Used by add and propose only.")] string? persona_section = null,
        [Description("The entry in one line, in the memory language and in its section's sentence form.")] string? text = null,
        [Description("The user's own words behind the entry, one line; kept as a comment under it.")] string? source = null,
        [Description("Short code of the entry to replace or delete, as listed in a refusal or an earlier reply; required by both. Finds an entry in any scope. Not used by add or propose.")] string? hash = null,
        [Description("Scope of the entry: global (the default), project:<name> or domain:<label>. Used by add and propose; see the description above.")] string? scope = null,
        [Description("Trigger of a new domain: one line saying when the domain applies. Used by add only; required when add creates the domain, ignored for an existing one.")] string? trigger = null,
        [Description("true only after a write was refused as a possible secret and you made sure with the user that it holds none; skips the secret check.")] bool? confirm_no_secret = null,
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
