using System.Globalization;
using System.Text;
using TacitMemory.Core.Git;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Tools;

namespace TacitMemory.Core.Pages;

/// <summary>
/// The detail page side of the memory tools: reading a page and the <c>write_page</c> operations. Callers serialize
/// writes; this class assumes it runs one write at a time.
/// </summary>
internal sealed class PageTools(TacitOptions options, TacitConfig config, Action<string> notice)
{
    private const string Ops = "create";

    private readonly GitCli _git = new(config.MemoryPath, config.Language);

    public ToolResult Read(RecallRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Page))
            return ToolText.Refuse("Missing parameter page.", "Call recall again with page set to a page ID such as my-app/db-upgrade.");
        if (!PageId.TryParse(request.Page, out var id, out var problem))
            return ToolText.Refuse(problem, "Call recall again with a valid page ID.");

        var (document, refusal) = Load(id);
        return refusal ?? ToolText.Ok(Format(id, document!));
    }

    public async Task<ToolResult> WriteAsync(WritePageRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Op))
            return ToolText.Refuse("Missing parameter op.", $"Call write_page again with op set to one of: {Ops}.");
        if (string.IsNullOrWhiteSpace(request.Page))
            return ToolText.Refuse("Missing parameter page.", "Call write_page again with page set to a page ID such as my-app/db-upgrade.");
        if (!PageId.TryParse(request.Page, out var id, out var problem))
            return ToolText.Refuse(problem, "Call write_page again with a valid page ID.");

        return request.Op switch
        {
            "create" => await CreateAsync(id, request, cancellationToken),
            _ => ToolText.Refuse($"Unknown op {ToolText.Quote(request.Op)}.", $"Call write_page again with op set to one of: {Ops}."),
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

        var today = options.Today;
        var header = new PageHeader(title, aliases, today, today, related);
        var text = PageDocument.Render(header, sections);
        TextFile.Write(PathOf(id), text, hasBom: false);

        var commitProblem = await CommitAsync($"pages: create {id}", id, cancellationToken);
        var written = PageDocument.Parse(text, hasBom: false, id.Slug);
        var reply = $"Created page {id}.\nSections:\n{SectionList(written)}";
        return commitProblem is null ? ToolText.Ok(reply) : new ToolResult($"{reply}\n\n{commitProblem}", IsError: true);
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
        foreach (var alias in aliases.Where(alias => alias.Contains('\n') || Length(alias) > limits.AliasLength))
            problems.Add($"Alias {ToolText.Quote(alias)} must be one line of at most {limits.AliasLength} characters.");
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
            notice(e.Message);
            return $"Not committed: the page is saved, but git failed ({e.Message}). Nothing to redo: it is committed with the next write to this page.";
        }
    }

    /// <summary>A page as the agent reads it: header facts, then every section with its short code.</summary>
    public static string Format(PageId id, PageDocument document)
    {
        var header = document.Header;
        var text = new StringBuilder();
        text.Append($"# {id}: {header.Title}\n");
        if (document.HeaderBroken)
            text.Append("Note: this page's header cannot be read as YAML front matter; the user must fix it before the page can be written.\n");
        if (header.Aliases.Count > 0)
            text.Append($"Aliases: {string.Join("; ", header.Aliases)}\n");
        if (header.First is { } first && header.Last is { } last)
            text.Append(CultureInfo.InvariantCulture, $"Dates: {first:yyyy-MM-dd} to {last:yyyy-MM-dd}\n");
        if (header.Related.Count > 0)
            text.Append($"Related: {string.Join(", ", header.Related)}\n");
        text.Append("Short codes in brackets identify sections for write_page.\n");
        foreach (var section in document.Sections)
            text.Append($"\n## {section.Name} [{section.Code}]\n{section.Body}\n");
        return text.ToString();
    }

    private static string SectionList(PageDocument document) =>
        string.Join('\n', document.Sections.Select(section => $"- {section.Name} [{section.Code}]"));

    private string PathOf(PageId id) => Path.Combine(config.MemoryPath, "pages", id.Project, id.Slug + ".md");

    private static int Length(string text) => new StringInfo(text).LengthInTextElements;
}
