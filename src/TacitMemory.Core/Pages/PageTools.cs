using Microsoft.Extensions.Logging;
using TacitMemory.Core.Logging;
using System.Globalization;
using System.Text;
using TacitMemory.Core.Decisions;
using TacitMemory.Core.Git;
using TacitMemory.Core.Index;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Tools;

namespace TacitMemory.Core.Pages;

/// <summary>
/// The detail page side of the memory tools: reading a page and the <c>write_page</c> operations. Callers serialize
/// writes; this class assumes it runs one write at a time.
/// </summary>
internal sealed class PageTools(
    TacitOptions options, TacitConfig config, ServiceLog log, DuplicatePageCheck duplicates)
{
    /// <summary>The parameters each op takes; any other parameter given with an op is refused rather than ignored.</summary>
    private static readonly Dictionary<string, string[]> OpParameters = new()
    {
        [ToolValues.WritePageOps.Create] = ["title", "aliases", "sections", "related", "confirm_no_secret"],
        [ToolValues.WritePageOps.UpsertSection] = ["section", "body", "hash", "confirm_no_secret"],
        [ToolValues.WritePageOps.DeleteSection] = ["section", "hash"],
        [ToolValues.WritePageOps.UpdateHeader] = ["title", "aliases", "add_aliases", "related", "confirm_no_secret"],
    };

    private static readonly string Ops = string.Join(", ", OpParameters.Keys);

    /// <summary>Every parameter <c>write_page</c> takes, listed when the op is missing or unknown.</summary>
    private static readonly string AllParameters = string.Join(", ", new[] { "op", "page" }.Concat(OpParameters.Values.SelectMany(taken => taken).Distinct()));

    private readonly GitCli _git = new(config.MemoryPath, config.Language);

    /// <summary>Reads a whole page. The index follows the file first, so the section dates are current, and each section counts as returned.</summary>
    public ToolResult Read(RecallRequest request, MemoryIndex index)
    {
        if (string.IsNullOrWhiteSpace(request.Page))
            return ToolText.Refuse("Missing parameter page.", "Call recall again with page set to a page ID such as my-app/db-upgrade.");
        if (!PageId.TryParse(request.Page, out var id, out var problem))
            return ToolText.Refuse(problem, "Call recall again with a valid page ID.");

        var (document, refusal) = Load(id);
        if (refusal is not null)
            return refusal;
        index.SyncPage(id);
        index.RecordPageRead(id);
        return ToolText.Ok(Format(id, document!, index.SectionChanges(id)));
    }

    public async Task<ToolResult> WriteAsync(WritePageRequest request, CancellationToken cancellationToken)
    {
        var problems = ToolText.UnknownParameters(request.UnknownParameters);
        string nextStep;
        if (!OpParameters.TryGetValue(request.Op ?? "", out var taken))
        {
            problems.Add(string.IsNullOrWhiteSpace(request.Op) ? "Missing parameter op." : $"Unknown op {ToolText.Quote(request.Op)}.");
            nextStep = $"Call write_page again with op set to one of: {Ops}; its parameters are: {AllParameters}.";
        }
        else
        {
            problems.AddRange(MisplacedParameters(request, taken).Select(name => $"Parameter {name} does not apply to op {request.Op}."));
            nextStep = $"Call write_page again with only the parameters for op {request.Op}: page, {string.Join(", ", taken)}.";
        }
        if (problems.Count > 0)
            return ToolText.Refuse(problems, nextStep);

        if (string.IsNullOrWhiteSpace(request.Page))
            return ToolText.Refuse("Missing parameter page.", "Call write_page again with page set to a page ID such as my-app/db-upgrade.");
        if (!PageId.TryParse(request.Page, out var id, out var problem))
            return ToolText.Refuse(problem, "Call write_page again with a valid page ID.");

        return request.Op switch
        {
            ToolValues.WritePageOps.Create => await CreateAsync(id, request, cancellationToken),
            ToolValues.WritePageOps.UpsertSection => await UpsertSectionAsync(id, request, cancellationToken),
            ToolValues.WritePageOps.DeleteSection => await DeleteSectionAsync(id, request, cancellationToken),
            ToolValues.WritePageOps.UpdateHeader => await UpdateHeaderAsync(id, request, cancellationToken),
            _ => throw new InvalidOperationException($"Op {request.Op} passed the op check without a handler."),
        };
    }

    private async Task<ToolResult> CreateAsync(PageId id, WritePageRequest request, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var title = request.Title?.Trim() ?? "";
        CheckTitle(title, problems);
        var aliases = CheckAliases(request.Aliases ?? [], problems);
        var sections = CheckSections(request.Sections ?? [], problems);
        var related = CheckRelated(id, request.Related ?? [], problems);
        if (problems.Count > 0)
            return ToolText.Refuse(problems, "Fix these and call write_page op=create again.");

        if (File.Exists(PathOf(id)))
        {
            var (existing, refusal) = Load(id);
            return refusal ?? ToolText.Refuse(
                $"Page {id} already exists.",
                "Update it instead: write_page op=upsert_section for a section, op=update_header for its title, aliases or related pages.",
                SectionList(existing!));
        }

        if (CheckSecrets(request, ToolValues.WritePageOps.Create, [("title", title), .. aliases.Select(alias => ("alias", alias)), .. SectionTexts(sections)]) is { } secret)
            return secret;

        var today = options.Today;
        var header = new PageHeader(title, aliases, today, today, related);
        var check = await duplicates.RunAsync(id, header, sections, cancellationToken);
        if (check.Duplicate is { } duplicate)
        {
            var (existing, refusal) = Load(duplicate);
            return refusal ?? ToolText.Refuse(
                $"Page {duplicate} already records the same matter.",
                $"Update {duplicate} instead (write_page op=upsert_section or op=update_header); read it first with recall page={duplicate}.",
                $"{duplicate}: {existing!.Header.Title}\n{SectionList(existing)}");
        }

        var text = PageDocument.Render(header, sections);
        TextFile.Write(PathOf(id), text, hasBom: false);

        var commitProblem = await CommitAsync($"pages: create {id}", id, cancellationToken);
        var written = PageDocument.Parse(text, hasBom: false, id.Slug);
        var reply = $"Created page {id}.\nSections:\n{SectionList(written)}";
        if (check.Note is { } note)
            reply += $"\n\n{note}";
        return commitProblem is null ? ToolText.Ok(reply) with { Written = true } : new ToolResult($"{reply}\n\n{commitProblem}", IsError: true, Written: true);
    }

    private async Task<ToolResult> UpsertSectionAsync(PageId id, WritePageRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Section))
            return ToolText.Refuse("Missing parameter section.", "Call write_page op=upsert_section again with section set to the section's name.");
        var name = request.Section.Trim();

        var (document, refusal) = Load(id);
        if (refusal is not null)
            return refusal;
        if (document!.HeaderBroken)
            return BrokenHeader(id);
        if (RefuseCodeAsName(document!, name, "op=upsert_section") is { } codeAsName)
            return codeAsName;

        var body = request.Body ?? "";
        var problems = new List<string>();
        CheckSections([new PageSectionInput(name, body)], problems);
        if (problems.Count > 0)
            return ToolText.Refuse(problems, "Fix these and call write_page op=upsert_section again.");

        if (CheckSecrets(request, ToolValues.WritePageOps.UpsertSection, SectionTexts([new PageSectionInput(name, body)])) is { } secret)
            return secret;

        var existing = document.Sections.FirstOrDefault(section => section.Name == name);
        if (existing is not null && RefuseStaleSection(existing, request.Hash, "replace", "op=upsert_section") is { } stale)
            return stale;

        var changed = document.WithSection(existing, name, body);
        var written = changed.Sections.First(section => section.Name == name);
        var reply = existing is null ? $"Added section {name} [{written.Code}]." : $"Replaced section {name} [{written.Code}].";
        return await SaveAsync(id, document, changed, $"pages: upsert {id} {name}", reply, cancellationToken);
    }

    private async Task<ToolResult> DeleteSectionAsync(PageId id, WritePageRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Section))
            return ToolText.Refuse("Missing parameter section.", "Call write_page op=delete_section again with section set to the section's name.");
        var name = request.Section.Trim();

        var (document, refusal) = Load(id);
        if (refusal is not null)
            return refusal;
        if (document!.HeaderBroken)
            return BrokenHeader(id);
        if (RefuseCodeAsName(document!, name, "op=delete_section") is { } codeAsName)
            return codeAsName;

        var existing = document.Sections.FirstOrDefault(section => section.Name == name);
        if (existing is null)
            return ToolText.Refuse(
                $"Section {ToolText.Quote(name)} does not exist on page {id}.",
                "Call write_page op=delete_section again with the name of a section listed below.",
                SectionList(document));
        if (RefuseStaleSection(existing, request.Hash, "delete", "op=delete_section") is { } stale)
            return stale;

        var changed = document.WithoutSection(existing);
        return await SaveAsync(id, document, changed, $"pages: delete {id} {name}", $"Deleted section {name}.", cancellationToken);
    }

    private async Task<ToolResult> UpdateHeaderAsync(PageId id, WritePageRequest request, CancellationToken cancellationToken)
    {
        if (request.Aliases is not null)
            return ToolText.Refuse(
                "update_header does not replace aliases; pass add_aliases to add some.",
                "Call write_page op=update_header again with add_aliases instead of aliases.");
        if (request.Title is null && request.AddAliases is null && request.Related is null)
            return ToolText.Refuse(
                "Give at least one of title, add_aliases or related.",
                "Call write_page op=update_header again with the header field to change.");

        var problems = new List<string>();
        var title = request.Title?.Trim();
        if (title is not null)
            CheckTitle(title, problems);
        var added = request.AddAliases is null ? [] : CheckAddedAliases(request.AddAliases, problems);
        var related = request.Related is null ? null : CheckRelated(id, request.Related, problems);
        if (problems.Count > 0)
            return ToolText.Refuse(problems, "Fix these and call write_page op=update_header again.");

        var written = new List<(string, string)>();
        if (title is not null)
            written.Add(("title", title));
        written.AddRange(added.Select(alias => ("alias", alias)));
        if (CheckSecrets(request, ToolValues.WritePageOps.UpdateHeader, written) is { } secret)
            return secret;

        var (document, refusal) = Load(id);
        if (refusal is not null)
            return refusal;
        if (document!.HeaderBroken)
            return BrokenHeader(id);

        var header = document.Header;
        var changes = new List<(string Key, IReadOnlyList<string>? Block)>();
        if (title is not null && title != header.Title)
            changes.Add(("title", PageDocument.TitleBlock(title)));
        var newAliases = added.Where(alias => !header.Aliases.Contains(alias)).ToList();
        if (newAliases.Count > 0)
            changes.Add(("aliases", PageDocument.AliasBlock(header.Aliases.Concat(newAliases).ToList())));
        if (related is not null && !related.SequenceEqual(header.Related))
            changes.Add(("related", PageDocument.RelatedBlock(related)));

        var changed = document.WithHeaderBlocks(changes);
        return await SaveAsync(id, document, changed, $"pages: header {id}", $"Updated the header of page {id}.", cancellationToken);
    }

    /// <summary>The credential check's refusal, or <c>null</c> when the texts may be written or the agent confirmed.</summary>
    private static ToolResult? CheckSecrets(WritePageRequest request, string op, IEnumerable<(string Where, string Text)> texts)
    {
        if (request.ConfirmNoSecret == true)
            return null;
        var written = texts.Select(text => new WrittenText(text.Where, text.Text)).ToList();
        return written.Count == 0 ? null : CredentialCheck.Run(written, "write_page", op);
    }

    private static IEnumerable<(string Where, string Text)> SectionTexts(IEnumerable<PageSectionInput> sections) =>
        sections.SelectMany(section => new[] { ("section name", section.Name), ($"section {section.Name}", section.Body) });

    /// <summary>The refusal when section <paramref name="section"/> exists and the hash does not prove the agent read its current text.</summary>
    private static ToolResult? RefuseStaleSection(PageSection section, string? hash, string verb, string op)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return ToolText.Refuse(
                $"Section {section.Name} exists; give its hash to {verb} it.",
                $"Call write_page {op} again with hash set to the short code in brackets from the latest recall.",
                SectionBlock(section));
        if (!string.Equals(hash.Trim(), section.Code, StringComparison.OrdinalIgnoreCase))
            return ToolText.Refuse(
                $"The hash for section {section.Name} does not match: it changed since it was read.",
                "Recall the page, then call write_page again with the section's current short code.",
                SectionBlock(section));
        return null;
    }

    /// <summary>
    /// The refusal when <paramref name="name"/> is the short code of a section of the page and no section has that name:
    /// the agent gave a code where a section name belongs. Null otherwise, so a name that matches a section is handled by name.
    /// </summary>
    private static ToolResult? RefuseCodeAsName(PageDocument document, string name, string op)
    {
        if (document.Sections.Any(section => section.Name == name))
            return null;
        var coded = document.Sections.FirstOrDefault(candidate => string.Equals(candidate.Code, name, StringComparison.OrdinalIgnoreCase));
        if (coded is null)
            return null;
        return ToolText.Refuse(
            $"Parameter section {ToolText.Quote(name)} is the short code of section {coded.Name}, not a section name.",
            $"Call write_page {op} again with section set to the name {coded.Name} and hash set to the short code {coded.Code}.",
            SectionBlock(coded));
    }

    private static string SectionBlock(PageSection section) => $"## {section.Name} [{section.Code}]\n{section.Body}";

    private static ToolResult BrokenHeader(PageId id) => ToolText.Refuse(
        $"The header of page {id} cannot be read as a YAML header, so the page is left alone.",
        "Ask the user to fix the YAML header at the top of the page, then try again.");

    /// <summary>The page with its dates set for a write today: last becomes today, and first stays or becomes today when unset.</summary>
    private PageDocument WithToday(PageDocument document)
    {
        var today = options.Today;
        var header = document.Header;
        if (header.Last == today && header.First is not null)
            return document;
        return document.WithHeaderBlocks([("dates", PageDocument.DatesBlock(header.First ?? today, today))]);
    }

    /// <summary>Writes the changed page and commits it. An unchanged page is neither written nor committed.</summary>
    private async Task<ToolResult> SaveAsync(PageId id, PageDocument before, PageDocument changed, string message, string reply, CancellationToken cancellationToken)
    {
        if (changed.Text == before.Text)
            return ToolText.Ok("Nothing changed: the page already has this content.");

        var after = WithToday(changed);
        // Checked before the write: a file that already had user edits gets that noted in its commit subject.
        var dirty = await _git.IsDirtyAsync(id.RelativePath, cancellationToken);
        TextFile.Write(PathOf(id), after.Text, after.HasBom);
        var commitProblem = await CommitAsync(dirty ? $"{message} (with user edits)" : message, id, cancellationToken);
        return commitProblem is null ? ToolText.Ok(reply) with { Written = true } : new ToolResult($"{reply}\n\n{commitProblem}", IsError: true, Written: true);
    }

    /// <summary>The parameters that were given but the op does not take.</summary>
    private static List<string> MisplacedParameters(WritePageRequest request, string[] taken)
    {
        var given = new (string Name, bool IsGiven)[]
        {
            ("title", request.Title is not null),
            ("aliases", request.Aliases is not null),
            ("sections", request.Sections is not null),
            ("related", request.Related is not null),
            ("section", request.Section is not null),
            ("body", request.Body is not null),
            ("hash", request.Hash is not null),
            ("add_aliases", request.AddAliases is not null),
            ("confirm_no_secret", request.ConfirmNoSecret is not null),
        };
        return given.Where(parameter => parameter.IsGiven && !taken.Contains(parameter.Name)).Select(parameter => parameter.Name).ToList();
    }

    private void CheckAliasLines(IEnumerable<string> aliases, List<string> problems)
    {
        foreach (var alias in aliases.Where(alias => alias.Contains('\n') || Length(alias) > config.Limits.Page.AliasLength))
            problems.Add($"Alias {ToolText.Quote(alias)} must be one line of at most {config.Limits.Page.AliasLength} characters.");
    }

    private List<string> CheckAddedAliases(IReadOnlyList<string> raw, List<string> problems)
    {
        var aliases = raw.Select(alias => alias.Trim()).Where(alias => alias.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        CheckAliasLines(aliases, problems);
        return aliases;
    }

    private void CheckTitle(string title, List<string> problems)
    {
        if (title.Length == 0)
            problems.Add("Missing parameter title.");
        else if (title.Contains('\n'))
            problems.Add("The title must be a single line.");
        else if (Length(title) > config.Limits.Page.TitleLength)
            problems.Add($"The title has {Length(title)} characters; the limit is {config.Limits.Page.TitleLength}.");
    }

    private List<string> CheckAliases(IReadOnlyList<string> raw, List<string> problems)
    {
        var aliases = raw.Select(alias => alias.Trim()).Where(alias => alias.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var limits = config.Limits.Page;
        if (aliases.Count < limits.MinAliases)
            problems.Add($"Give at least {limits.MinAliases} distinct aliases (spoken phrasings, Chinese and English names, nicknames); got {aliases.Count}.");
        CheckAliasLines(aliases, problems);
        return aliases;
    }

    private static List<PageSectionInput> CheckSections(IReadOnlyList<PageSectionInput> raw, List<string> problems)
    {
        if (raw.Count == 0)
            problems.Add("Give at least one section.");
        var sections = raw.Select(section => new PageSectionInput((section.Name ?? "").Trim(), section.Body ?? "")).ToList();
        foreach (var section in sections)
        {
            if (section.Name.Length == 0 || section.Name.Contains('\n') || section.Name.StartsWith('#'))
                problems.Add($"Section name {ToolText.Quote(section.Name)} must be one line of text, without leading '#'.");
            if (ShortCode.Normalize(section.Body).Length == 0)
                problems.Add($"Section {ToolText.Quote(section.Name)} has an empty body.");
            else if (PageDocument.BreaksStructure(section.Body))
                problems.Add($"The body of section {ToolText.Quote(section.Name)} has a '#' or '##' heading; use '###' or deeper inside a section.");
        }

        foreach (var name in sections.GroupBy(section => section.Name, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key))
            problems.Add($"Section name {ToolText.Quote(name)} is used more than once; section names must be unique within a page.");
        return sections;
    }

    private List<string> CheckRelated(PageId self, IReadOnlyList<string> raw, List<string> problems)
    {
        var related = new List<string>();
        foreach (var item in raw.Select(item => item.Trim()).Where(item => item.Length > 0))
        {
            if (!PageId.TryParse(item, out var id, out var problem))
                problems.Add($"related: {problem}");
            else if (id == self)
                problems.Add($"related: {id} is this page itself.");
            else if (!File.Exists(PathOf(id)))
                problems.Add($"related: page {id} does not exist.");
            else if (!related.Contains(id.ToString()))
                related.Add(id.ToString());
        }

        return related;
    }

    /// <summary>The page, or the refusal to give when it is missing or not UTF-8.</summary>
    private (PageDocument? Document, ToolResult? Refusal) Load(PageId id)
    {
        var path = PathOf(id);
        if (!File.Exists(path))
            return (null, ToolText.Refuse($"Page {id} does not exist.", "Check the page ID, or create the page with write_page op=create."));
        if (TextFile.Read(path) is not { } read)
            return (null, ToolText.Refuse($"Page {id} is not UTF-8 encoded, so it is left alone.", "Ask the user to save the file as UTF-8."));
        return (PageDocument.Parse(read.Text, read.HasBom, id.Slug), null);
    }

    /// <summary>Commits the page; on failure, tells the user and returns the note for the agent. The file stays written.</summary>
    private async Task<string?> CommitAsync(string message, PageId id, CancellationToken cancellationToken)
    {
        try
        {
            await _git.CommitAsync(message, [id.RelativePath], cancellationToken);
            return null;
        }
        catch (TacitException e) when (e.Error == TacitError.GitFailed)
        {
            log.Notify(LogLevel.Warning, e.Message);
            return $"Not committed: the page is saved, but git failed ({e.Message}). Nothing to redo: it is committed with the next write to this page.";
        }
    }

    /// <summary>A page as the agent reads it: header facts, then every section with its short code.</summary>
    public static string Format(PageId id, PageDocument document, IReadOnlyDictionary<string, DateOnly> changed)
    {
        var header = document.Header;
        var text = new StringBuilder();
        text.Append($"# {id}: {header.Title}\n");
        if (document.HeaderBroken)
            text.Append("Note: this page's header cannot be read as a YAML header; the user must fix it before the page can be written.\n");
        if (header.Aliases.Count > 0)
            text.Append($"Aliases: {string.Join("; ", header.Aliases)}\n");
        if (header.First is { } first && header.Last is { } last)
            text.Append(CultureInfo.InvariantCulture, $"Dates: {first:yyyy-MM-dd} to {last:yyyy-MM-dd}\n");
        if (header.Related.Count > 0)
            text.Append($"Related: {string.Join(", ", header.Related)}\n");
        text.Append("Short codes in brackets identify sections for write_page.\n");
        foreach (var section in document.Sections)
            text.Append($"\n## {section.Name} [{section.Code}]{SectionDates.Note(changed, section.Name)}\n{section.Body}\n");
        return text.ToString();
    }

    private static string SectionList(PageDocument document) =>
        string.Join('\n', document.Sections.Select(section => $"- {section.Name} [{section.Code}]"));

    private string PathOf(PageId id) => Path.Combine(config.MemoryPath, "pages", id.Project, id.Slug + ".md");

    private static int Length(string text) => new StringInfo(text).LengthInTextElements;
}
