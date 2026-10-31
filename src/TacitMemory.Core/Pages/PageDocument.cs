using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace TacitMemory.Core.Pages;

/// <summary>The front matter of a page. Dates are written by the service; the rest by the agent.</summary>
internal sealed record PageHeader(
    string Title, IReadOnlyList<string> Aliases, DateOnly? First, DateOnly? Last, IReadOnlyList<string> Related);

/// <summary>A <c>##</c> section: lines <see cref="HeadingLine"/> up to, not including, <see cref="EndLine"/>.</summary>
internal sealed record PageSection(string Name, string Body, string Code, int HeadingLine, int EndLine);

/// <summary>
/// A detail page read line by line: YAML front matter between <c>---</c> lines, then <c>##</c> sections. Anything else
/// in the file (text before the first section, deeper headings, fenced code) is kept as it is; edits replace whole
/// lines only.
/// </summary>
internal sealed partial class PageDocument
{
    private const string Fence = "---";
    private static readonly string DateFormat = "yyyy-MM-dd";

    private PageDocument(IReadOnlyList<string> lines, string newline, bool hasBom, PageHeader header, bool headerBroken,
        int headerEndLine, IReadOnlyList<PageSection> sections)
    {
        Lines = lines;
        Newline = newline;
        HasBom = hasBom;
        Header = header;
        HeaderBroken = headerBroken;
        HeaderEndLine = headerEndLine;
        Sections = sections;
    }

    public IReadOnlyList<string> Lines { get; }

    public string Newline { get; }

    public bool HasBom { get; }

    /// <summary>The header; when <see cref="HeaderBroken"/>, a stand-in titled after the file.</summary>
    public PageHeader Header { get; }

    /// <summary>The front matter is missing or is not a YAML mapping; the page is readable but not writable.</summary>
    public bool HeaderBroken { get; }

    /// <summary>The line after the closing <c>---</c>; 0 when there is no front matter.</summary>
    public int HeaderEndLine { get; }

    public IReadOnlyList<PageSection> Sections { get; }

    public static PageDocument Parse(string text, bool hasBom, string fallbackTitle)
    {
        var newline = Injection.TextFile.NewlineOf(text);
        var lines = text.ReplaceLineEndings("\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
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
            header is null, headerEnd, sections);
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
        var text = new StringBuilder();
        text.Append(Fence).Append('\n');
        text.Append("title: ").Append(Scalar(header.Title, inFlow: false)).Append('\n');
        text.Append("aliases: ").Append(FlowList(header.Aliases)).Append('\n');
        if (header.First is { } first && header.Last is { } last)
            text.Append("dates: { first: ").Append(first.ToString(DateFormat, CultureInfo.InvariantCulture))
                .Append(", last: ").Append(last.ToString(DateFormat, CultureInfo.InvariantCulture)).Append(" }\n");
        if (header.Related.Count > 0)
            text.Append("related: ").Append(FlowList(header.Related)).Append('\n');
        text.Append(Fence).Append('\n');
        return text.ToString();
    }

    /// <summary>Whether <paramref name="body"/> has a <c>#</c> or <c>##</c> heading outside code fences, which would split the page.</summary>
    public static bool BreaksStructure(string body)
    {
        var lines = body.ReplaceLineEndings("\n").Split('\n');
        return OutsideFences(lines, 0).Any(i => TopHeading().IsMatch(lines[i]));
    }

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
        catch (YamlException)
        {
            return null;
        }

        var title = Text(root, "title");
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

    private static string? Text(YamlMappingNode mapping, string key) => (Child(mapping, key) as YamlScalarNode)?.Value;

    private static List<string> List(YamlMappingNode mapping, string key) => Child(mapping, key) switch
    {
        YamlSequenceNode sequence => sequence.Children.OfType<YamlScalarNode>()
            .Select(item => item.Value ?? "").Where(item => item.Length > 0).ToList(),
        YamlScalarNode { Value: { Length: > 0 } single } => [single],
        _ => [],
    };

    private static DateOnly? Date(YamlMappingNode mapping, string key) =>
        DateOnly.TryParseExact(Text(mapping, key), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
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

    [GeneratedRegex(@"^ {0,3}(`{3,}|~{3,})")]
    private static partial Regex FenceLine();

    /// <summary>
    /// Text YAML reads back as the same plain string: no indicator first, no ": " or " #", no edge spaces, no quotes.
    /// </summary>
    [GeneratedRegex(@"^(?![-?:,\[\]{}#&*!|>'""%@`\s])(?!.*(: |\s#|:$))[^""'\r\n\t]*(?<!\s)$")]
    private static partial Regex PlainScalar();
}
