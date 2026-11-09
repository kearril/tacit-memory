using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using TacitMemory.Core.Consolidation;

namespace TacitMemory.Core.Pages;

/// <summary>The YAML header of a page. Dates are written by the service; the rest by the agent.</summary>
internal sealed record PageHeader(
    string Title, IReadOnlyList<string> Aliases, DateOnly? First, DateOnly? Last, IReadOnlyList<string> Related);

/// <summary>A <c>##</c> section: lines <see cref="HeadingLine"/> up to, not including, <see cref="EndLine"/>.</summary>
internal sealed record PageSection(string Name, string Body, string Code, int HeadingLine, int EndLine);

/// <summary>
/// A detail page read line by line: YAML header between <c>---</c> lines, then <c>##</c> sections. Anything else
/// in the file (text before the first section, deeper headings, fenced code) is kept as it is; edits replace whole
/// lines only.
/// </summary>
internal sealed partial class PageDocument
{
    private const string Fence = "---";
    private static readonly string DateFormat = "yyyy-MM-dd";

    private PageDocument(IReadOnlyList<string> lines, string newline, bool hasBom, PageHeader header, bool headerBroken,
        int headerEndLine, IReadOnlyList<PageSection> sections, bool endsWithNewline)
    {
        Lines = lines;
        Newline = newline;
        HasBom = hasBom;
        Header = header;
        HeaderBroken = headerBroken;
        HeaderEndLine = headerEndLine;
        Sections = sections;
        EndsWithNewline = endsWithNewline;
    }

    public IReadOnlyList<string> Lines { get; }

    public string Newline { get; }

    public bool HasBom { get; }

    /// <summary>The header; when <see cref="HeaderBroken"/>, a stand-in titled after the file.</summary>
    public PageHeader Header { get; }

    /// <summary>The header is missing or is not a YAML mapping; the page is readable but not writable.</summary>
    public bool HeaderBroken { get; }

    /// <summary>The line after the closing <c>---</c>; 0 when there is no header.</summary>
    public int HeaderEndLine { get; }

    public IReadOnlyList<PageSection> Sections { get; }

    /// <summary>Whether the file ends with a line break; edits keep it that way.</summary>
    public bool EndsWithNewline { get; }

    /// <summary>The whole file as text, with <see cref="Newline"/> as the line break and no BOM.</summary>
    public string Text => JoinLines(Lines);

    /// <summary>
    /// The format problems of this page against the page limits, in line order. Parsing stays lenient and never reports
    /// them; the consolidation asks for them. The file's name and its related pages are checked by the caller.
    /// </summary>
    public IReadOnlyList<FormatIssue> Problems(PageLimits limits)
    {
        var problems = new List<FormatIssue>();
        if (HeaderBroken)
        {
            problems.Add(Fix("YAML header", "yaml header", "The YAML header between the --- lines is missing or cannot be parsed."));
        }
        else
        {
            if (HasHeaderKey("title") && Length(Header.Title) > limits.TitleLength)
                problems.Add(Fix(LocationOf("title"), "title length", $"The title is {Length(Header.Title)} characters; the limit is {limits.TitleLength}."));

            var aliases = LocationOf("aliases");
            if (Header.Aliases.Count < limits.MinAliases)
                problems.Add(Fix(aliases, "alias count", $"The page has {Header.Aliases.Count} aliases; at least {limits.MinAliases} are needed."));
            foreach (var alias in Header.Aliases.Where(alias => Length(alias) > limits.AliasLength))
                problems.Add(Fix(aliases, "alias length", $"Alias \"{alias}\" is {Length(alias)} characters; the limit is {limits.AliasLength}."));
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in Sections)
            if (!names.Add(section.Name))
                problems.Add(Fix($"line {section.HeadingLine + 1}", "duplicate section", $"Section \"{section.Name}\" appears more than once on this page."));
        return problems;
    }

    /// <summary>The location of header key <paramref name="key"/>: its line, or the header itself when the key is missing.</summary>
    public string LocationOf(string key)
    {
        for (var i = 1; i < HeaderEndLine - 1; i++)
            if (BlockKey().Match(Lines[i]) is { Success: true } match && match.Groups[1].Value == key)
                return $"line {i + 1}";
        return "YAML header";
    }

    /// <summary>Whether header key <paramref name="key"/> is present.</summary>
    public bool HasHeaderKey(string key) => LocationOf(key) != "YAML header";

    private static FormatIssue Fix(string location, string kind, string description) =>
        new("", location, kind, description, FormatHandling.FixDirectly);

    private static int Length(string text) => new StringInfo(text).LengthInTextElements;

    /// <summary>Reads and parses <paramref name="id"/> from <paramref name="memoryPath"/>, or <c>null</c> when missing or not UTF-8.</summary>
    public static PageDocument? TryLoad(string memoryPath, PageId id)
    {
        var path = Path.Combine(memoryPath, id.RelativePath);
        if (!File.Exists(path) || Injection.TextFile.Read(path) is not { } read)
            return null;
        return Parse(read.Text, read.HasBom, id.Slug);
    }

    public static PageDocument Parse(string text, bool hasBom, string fallbackTitle)
    {
        var newline = Injection.TextFile.NewlineOf(text);
        var lines = text.ReplaceLineEndings("\n").Split('\n').ToList();
        var endsWithNewline = lines.Count > 0 && lines[^1].Length == 0;
        if (endsWithNewline)
            lines.RemoveAt(lines.Count - 1);

        var headerEnd = 0;
        PageHeader? header = null;
        if (lines.Count > 0 && lines[0].TrimEnd() == Fence)
        {
            var close = lines.FindIndex(1, line => line.TrimEnd() is Fence or "...");
            if (close > 0)
            {
                headerEnd = close + 1;
                header = ReadHeader(string.Join('\n', lines.Skip(1).Take(close - 1)), fallbackTitle);
            }
        }

        var sections = ReadSections(lines, headerEnd);
        return new PageDocument(lines, newline, hasBom, header ?? new PageHeader(fallbackTitle, [], null, null, []),
            header is null, headerEnd, sections, endsWithNewline);
    }

    /// <summary>The text of a new page, in the format the service writes: LF, ending with a line break.</summary>
    public static string Render(PageHeader header, IEnumerable<PageSectionInput> sections)
    {
        var text = new StringBuilder();
        text.Append(RenderHeader(header));
        foreach (var section in sections)
            text.Append('\n').Append("## ").Append(section.Name.Trim()).Append('\n').Append(ShortCode.Normalize(section.Body)).Append('\n');
        return text.ToString();
    }

    public static string RenderHeader(PageHeader header)
    {
        var lines = new List<string> { Fence };
        lines.AddRange(TitleBlock(header.Title));
        lines.AddRange(AliasBlock(header.Aliases));
        if (header.First is { } first && header.Last is { } last)
            lines.AddRange(DatesBlock(first, last));
        if (RelatedBlock(header.Related) is { } related)
            lines.AddRange(related);
        lines.Add(Fence);
        return string.Concat(lines.Select(line => line + "\n"));
    }

    /// <summary>The header line for <c>title</c>.</summary>
    public static IReadOnlyList<string> TitleBlock(string title) => ["title: " + Scalar(title, inFlow: false)];

    /// <summary>The header line for <c>aliases</c>.</summary>
    public static IReadOnlyList<string> AliasBlock(IEnumerable<string> aliases) => ["aliases: " + FlowList(aliases)];

    /// <summary>The header line for <c>dates</c>.</summary>
    public static IReadOnlyList<string> DatesBlock(DateOnly first, DateOnly last) =>
        [$"dates: {{ first: {Day(first)}, last: {Day(last)} }}"];

    /// <summary>The header line for <c>related</c>; null when there is nothing to relate, so the key is dropped.</summary>
    public static IReadOnlyList<string>? RelatedBlock(IReadOnlyList<string> related) =>
        related.Count == 0 ? null : ["related: " + FlowList(related)];

    private static string Day(DateOnly day) => day.ToString(DateFormat, CultureInfo.InvariantCulture);

    /// <summary>Whether <paramref name="body"/> has a <c>#</c> or <c>##</c> heading outside code fences, which would split the page.</summary>
    public static bool BreaksStructure(string body)
    {
        var lines = body.ReplaceLineEndings("\n").Split('\n');
        return OutsideFences(lines, 0).Any(i => TopHeading().IsMatch(lines[i]));
    }

    /// <summary>The page with section <paramref name="existing"/> replaced, or section <paramref name="name"/> appended when it is null.</summary>
    public PageDocument WithSection(PageSection? existing, string name, string body)
    {
        var section = new List<string> { "## " + name };
        section.AddRange(ShortCode.Normalize(body).Split('\n'));
        var result = Lines.ToList();
        if (existing is null)
        {
            if (result.Count > 0 && result[^1].Length > 0)
                result.Add("");
            result.AddRange(section);
        }
        else
        {
            // The blank line that separates a section from the next one stays where it is.
            var end = existing.EndLine;
            if (end - 1 > existing.HeadingLine && Lines[end - 1].Length == 0)
                end--;
            result.RemoveRange(existing.HeadingLine, end - existing.HeadingLine);
            result.InsertRange(existing.HeadingLine, section);
        }
        return Compose(result);
    }

    /// <summary>The page without <paramref name="section"/>; the blank line before it goes too when it was the last section.</summary>
    public PageDocument WithoutSection(PageSection section)
    {
        var result = Lines.ToList();
        result.RemoveRange(section.HeadingLine, section.EndLine - section.HeadingLine);
        if (section.EndLine == Lines.Count && section.HeadingLine > 0 && Lines[section.HeadingLine - 1].Length == 0)
            result.RemoveAt(section.HeadingLine - 1);
        return Compose(result);
    }

    /// <summary>
    /// The page with header keys replaced. A block replaces its key's lines in place (null drops the key); a key the
    /// file lacks is appended before the closing fence. Other header lines stay exactly as they are.
    /// </summary>
    public PageDocument WithHeaderBlocks(IReadOnlyList<(string Key, IReadOnlyList<string>? Block)> changes)
    {
        if (changes.Count == 0)
            return this;
        var replacements = changes.ToDictionary(change => change.Key, change => change.Block, StringComparer.Ordinal);
        var replaced = new HashSet<string>(StringComparer.Ordinal);
        var close = HeaderEndLine - 1;
        var result = new List<string> { Lines[0] };
        var skipping = false;
        for (var i = 1; i < close; i++)
        {
            var line = Lines[i];
            var key = BlockKey().Match(line);
            if (!key.Success)
            {
                if (line.StartsWith('#'))
                    skipping = false;
                // Indented continuation lines of a replaced block go with it; blank lines and top-level comments stay.
                if (!skipping || line.Trim().Length == 0)
                    result.Add(line);
                continue;
            }

            var name = key.Groups[1].Value;
            skipping = replacements.TryGetValue(name, out var block);
            if (!skipping)
            {
                result.Add(line);
                continue;
            }
            replaced.Add(name);
            if (block is not null)
                result.AddRange(block);
        }
        foreach (var change in changes)
            if (change.Block is not null && !replaced.Contains(change.Key))
                result.AddRange(change.Block);
        result.Add(Lines[close]);
        result.AddRange(Lines.Skip(HeaderEndLine));
        return Compose(result);
    }

    private PageDocument Compose(IReadOnlyList<string> lines) => Parse(JoinLines(lines), HasBom, Header.Title);

    private string JoinLines(IReadOnlyList<string> lines) =>
        string.Join(Newline, lines) + (EndsWithNewline && lines.Count > 0 ? Newline : "");

    private static PageHeader? ReadHeader(string yaml, string fallbackTitle)
    {
        YamlMappingNode root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode mapping)
                return null;
            root = mapping;
        }
        catch (Exception exception) when (exception is YamlException or InvalidOperationException)
        {
            // The scanner reports some broken flow sequences as InvalidOperationException rather than YamlException.
            return null;
        }

        var title = ScalarOf(root, "title");
        var dates = Child(root, "dates") as YamlMappingNode;
        return new PageHeader(
            string.IsNullOrWhiteSpace(title) ? fallbackTitle : title,
            List(root, "aliases"),
            dates is null ? null : Date(dates, "first"),
            dates is null ? null : Date(dates, "last"),
            List(root, "related"));
    }

    /// <summary>Indices of the lines from <paramref name="start"/> that are neither a code fence nor inside one.</summary>
    private static IEnumerable<int> OutsideFences(IReadOnlyList<string> lines, int start)
    {
        string? fence = null;
        for (var i = start; i < lines.Count; i++)
        {
            var match = FenceLine().Match(lines[i]);
            if (match.Success)
            {
                var marker = match.Groups[1].Value;
                if (fence is null)
                    fence = marker;
                else if (marker[0] == fence[0] && marker.Length >= fence.Length)
                    fence = null;
                continue;
            }

            if (fence is null)
                yield return i;
        }
    }

    private static List<PageSection> ReadSections(IReadOnlyList<string> lines, int start)
    {
        var headings = new List<(int Line, string Name)>();
        foreach (var i in OutsideFences(lines, start))
        {
            var heading = SectionHeading().Match(lines[i]);
            if (heading.Success)
                headings.Add((i, heading.Groups[1].Value.Trim()));
        }

        var sections = new List<PageSection>();
        for (var k = 0; k < headings.Count; k++)
        {
            var end = k + 1 < headings.Count ? headings[k + 1].Line : lines.Count;
            var body = ShortCode.Normalize(string.Join('\n', lines.Skip(headings[k].Line + 1).Take(end - headings[k].Line - 1)));
            sections.Add(new PageSection(headings[k].Name, body, "", headings[k].Line, end));
        }

        var codes = ShortCode.For(sections.Select(section => ShortCode.SectionText(section.Name, section.Body)));
        return sections.Select((section, k) => section with { Code = codes[k] }).ToList();
    }

    private static YamlNode? Child(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var node) ? node : null;

    private static string? ScalarOf(YamlMappingNode mapping, string key) => (Child(mapping, key) as YamlScalarNode)?.Value;

    private static List<string> List(YamlMappingNode mapping, string key) => Child(mapping, key) switch
    {
        YamlSequenceNode sequence => sequence.Children.OfType<YamlScalarNode>()
            .Select(item => item.Value ?? "").Where(item => item.Length > 0).ToList(),
        YamlScalarNode { Value: { Length: > 0 } single } => [single],
        _ => [],
    };

    private static DateOnly? Date(YamlMappingNode mapping, string key) =>
        DateOnly.TryParseExact(ScalarOf(mapping, key), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static string FlowList(IEnumerable<string> items) => "[" + string.Join(", ", items.Select(item => Scalar(item, inFlow: true))) + "]";

    /// <summary>A YAML scalar: plain when that reads back unchanged, otherwise double-quoted with JSON escapes.</summary>
    private static string Scalar(string value, bool inFlow) =>
        PlainScalar().IsMatch(value) && (!inFlow || value.IndexOfAny([',', '[', ']', '{', '}']) < 0)
            ? value
            : System.Text.Json.JsonSerializer.Serialize(value, JsonOptions);

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [GeneratedRegex(@"^## (.+)$")]
    private static partial Regex SectionHeading();

    [GeneratedRegex(@"^#{1,2}(\s|$)")]
    private static partial Regex TopHeading();

    [GeneratedRegex(@"^([A-Za-z_][\w-]*):(\s|$)")]
    private static partial Regex BlockKey();

    [GeneratedRegex(@"^ {0,3}(`{3,}|~{3,})")]
    private static partial Regex FenceLine();

    /// <summary>
    /// Text YAML reads back as the same plain string: no indicator first, no ": " or " #", no edge spaces, no quotes.
    /// </summary>
    [GeneratedRegex(@"^(?![-?:,\[\]{}#&*!|>'""%@`\s])(?!.*(: |\s#|:$))[^""'\r\n\t]*(?<!\s)$")]
    private static partial Regex PlainScalar();
}
