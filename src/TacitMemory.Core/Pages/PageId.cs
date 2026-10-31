using System.Text;
using System.Text.RegularExpressions;

namespace TacitMemory.Core.Pages;

/// <summary>
/// A page ID, <c>&lt;project&gt;/&lt;slug&gt;</c>: the page lives at <c>pages/&lt;project&gt;/&lt;slug&gt;.md</c>.
/// The project is lowercased; <c>_topics</c> is the only project starting with an underscore.
/// </summary>
internal sealed partial record PageId(string Project, string Slug)
{
    public const string Topics = "_topics";

    public string RelativePath => $"pages/{Project}/{Slug}.md";

    public override string ToString() => $"{Project}/{Slug}";

    /// <summary>Parses <paramref name="raw"/>, or explains in English what is wrong with it.</summary>
    public static bool TryParse(string raw, out PageId id, out string problem)
    {
        id = null!;
        var parts = raw.Trim().Split('/');
        if (parts.Length != 2)
        {
            problem = $"Page ID \"{raw}\" must be <project>/<slug>, for example my-app/db-upgrade or {Topics}/pgroonga.";
            return false;
        }

        var project = parts[0].ToLowerInvariant();
        var slug = parts[1];
        if (project != Topics && !ProjectPattern().IsMatch(project))
        {
            problem = $"Project \"{parts[0]}\" may only use a-z, 0-9, '.', '_' and '-', must not start with '.' or '_' (except {Topics}), for example {Suggest(parts[0])}.";
            return false;
        }

        if (!SlugPattern().IsMatch(slug))
        {
            problem = $"Slug \"{slug}\" must be lowercase words of a-z and 0-9 joined by single hyphens, for example {Suggest(slug)}.";
            return false;
        }

        id = new PageId(project, slug);
        problem = "";
        return true;
    }

    /// <summary>A compliant spelling of <paramref name="text"/>: lowercase ASCII words joined by hyphens.</summary>
    private static string Suggest(string text)
    {
        var words = new StringBuilder();
        foreach (var c in text.ToLowerInvariant())
            words.Append(c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : ' ');
        var joined = string.Join('-', words.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return joined.Length == 0 ? "db-upgrade" : joined;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]*$")]
    private static partial Regex ProjectPattern();

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}
