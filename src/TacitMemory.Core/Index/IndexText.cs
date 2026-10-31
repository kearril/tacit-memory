using System.Globalization;
using System.Text;

namespace TacitMemory.Core.Index;

/// <summary>
/// Text as the full-text index sees it. FTS5's <c>unicode61</c> tokenizer keeps a run of Chinese characters as one
/// token, so runs are cut into overlapping two-character tokens before indexing and before querying; everything
/// else is left to <c>unicode61</c>, which splits at punctuation.
/// </summary>
internal static class IndexText
{
    /// <summary>The text to put in the index: CJK runs become space-separated bigrams.</summary>
    public static string ForIndex(string text)
    {
        var output = new StringBuilder(text.Length * 2);
        foreach (var (run, isCjk) in Runs(text))
            output.Append(' ').Append(isCjk ? string.Join(' ', Bigrams(run)) : run).Append(' ');
        return output.ToString();
    }

    /// <summary>
    /// An FTS5 query matching any of the query's terms, or <c>null</c> when it has none: each CJK bigram is a term,
    /// and each whitespace-separated word of other text is a phrase, so <c>env.ps1</c> must match as written.
    /// </summary>
    public static string? ForQuery(string query)
    {
        var terms = new List<string>();
        foreach (var (run, isCjk) in Runs(query))
        {
            if (isCjk)
                terms.AddRange(run.Length > 1 ? Bigrams(run) : []);
            else
                terms.AddRange(run.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Where(word => word.Any(char.IsLetterOrDigit)));
        }

        return terms.Count == 0 ? null : string.Join(" OR ", terms.Distinct(StringComparer.Ordinal).Select(term => $"\"{term.Replace("\"", "\"\"", StringComparison.Ordinal)}\""));
    }

    /// <summary>The single CJK characters of a query that has nothing else to search for; matched as substrings.</summary>
    public static IReadOnlyList<string> SingleCharacters(string query) =>
        Runs(query).Where(run => run.IsCjk && run.Text.Length == 1).Select(run => run.Text).Distinct(StringComparer.Ordinal).ToList();

    private static IEnumerable<string> Bigrams(string run)
    {
        if (run.Length == 1)
        {
            yield return run;
            yield break;
        }

        for (var i = 0; i + 1 < run.Length; i++)
            yield return run.Substring(i, 2);
    }

    /// <summary>The text split into maximal runs of CJK and of other characters.</summary>
    private static List<(string Text, bool IsCjk)> Runs(string text)
    {
        var runs = new List<(string, bool)>();
        var current = new StringBuilder();
        var currentIsCjk = false;
        foreach (var c in text)
        {
            var isCjk = IsCjk(c);
            if (current.Length > 0 && isCjk != currentIsCjk)
            {
                runs.Add((current.ToString(), currentIsCjk));
                current.Clear();
            }

            currentIsCjk = isCjk;
            current.Append(c);
        }

        if (current.Length > 0)
            runs.Add((current.ToString(), currentIsCjk));
        return runs;
    }

    /// <summary>Han ideographs, kana and Hangul: scripts written without spaces between words.</summary>
    private static bool IsCjk(char c) =>
        CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.OtherLetter
        && c is (>= '\u3040' and <= '\u30FF') or (>= '\u3400' and <= '\u4DBF') or (>= '\u4E00' and <= '\u9FFF')
            or (>= '\uAC00' and <= '\uD7AF') or (>= '\uF900' and <= '\uFAFF');
}
