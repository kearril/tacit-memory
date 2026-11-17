using System.Text.RegularExpressions;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Formats;

/// <summary>
/// The source comment of a persona entry, an activity or a page section: <c>&lt;!-- 2026-10-25 quote「…」 --&gt;</c>, with the
/// day or the quote left out when the comment has none. The quote word is read in every language. A comment that names
/// neither, or names a day followed by anything but a quote, is not a source.
/// </summary>
internal static class SourceComment
{
    private static readonly Regex Line = new(@"^\s*<!--\s*(?<inner>.*?)\s*-->\s*$", RegexOptions.CultureInvariant);

    private static readonly Regex Anywhere = new("<!--.*?-->", RegexOptions.CultureInvariant);

    private static readonly Regex Day = new(@"^(?<day>\d{4}-\d{2}-\d{2})(?:\s+|$)", RegexOptions.CultureInvariant);

    private static readonly Regex Quote = new(
        $@"^{Regex.Escape(StructureWords.Quote)}「(?<quote>.*)」$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Whether <paramref name="line"/> is a comment and nothing else, source or not.</summary>
    public static bool IsComment(string line) => Line.IsMatch(line);

    /// <summary>The source <paramref name="line"/> records, or <c>null</c> when it is not a source comment.</summary>
    public static SourceView? Parse(string line)
    {
        var comment = Line.Match(line);
        if (!comment.Success)
            return null;
        var rest = comment.Groups["inner"].Value;
        var day = Day.Match(rest);
        var date = day.Success ? day.Groups["day"].Value : null;
        rest = (day.Success ? rest[day.Length..] : rest).Trim();
        var quote = Quote.Match(rest);
        if (!quote.Success && (date is null || rest.Length > 0))
            return null;
        return new SourceView(date, quote.Success ? quote.Groups["quote"].Value : null);
    }

    /// <summary>
    /// Every source comment in <paramref name="markdown"/>, on a line of its own or within one, each once, in order.
    /// </summary>
    public static IReadOnlyList<SourceCommentView> In(string markdown) =>
        [.. Anywhere.Matches(markdown)
            .Select(match => (Comment: match.Value, Source: Parse(match.Value)))
            .Where(found => found.Source is not null)
            .DistinctBy(found => found.Comment)
            .Select(found => new SourceCommentView(found.Comment, found.Source!))];

    /// <summary>
    /// Continuation lines parted into the lines of the text and the sources, each in order. A comment that is not a source
    /// is in neither, as the text an agent reads leaves every comment out.
    /// </summary>
    public static (IReadOnlyList<string> Lines, IReadOnlyList<SourceView> Sources) Split(IEnumerable<string> continuation)
    {
        var lines = new List<string>();
        var sources = new List<SourceView>();
        foreach (var line in continuation)
        {
            if (Parse(line) is { } source)
                sources.Add(source);
            else if (!IsComment(line))
                lines.Add(line.Trim());
        }

        return (lines, sources);
    }
}
