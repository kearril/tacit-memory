using TacitMemory.Core.Localization;
using TacitMemory.Core.Pages;

namespace TacitMemory.Core.Persona;

/// <summary>The three scopes a persona entry can have.</summary>
internal enum PersonaScopeKind
{
    Global,
    Project,
    Domain,
}

/// <summary>
/// Where a persona entry applies: everywhere (<see cref="Global"/>), in one project, or in one domain. The name is the
/// project name or the domain label, and is empty for the global scope. A name as written in a file may break the naming
/// rule; the format check reports that.
/// </summary>
internal sealed record PersonaScope(PersonaScopeKind Kind, string Name)
{
    public static readonly PersonaScope Global = new(PersonaScopeKind.Global, "");

    public bool IsGlobal => Kind == PersonaScopeKind.Global;

    /// <summary>The scope as <c>write_persona</c> spells it: <c>global</c>, <c>project:&lt;name&gt;</c> or <c>domain:&lt;label&gt;</c>.</summary>
    public override string ToString() => Kind switch
    {
        PersonaScopeKind.Project => "project:" + Name,
        PersonaScopeKind.Domain => "domain:" + Name,
        _ => "global",
    };

    /// <summary>The order of the scope blocks in <c>persona.md</c>: domains before projects, each by name.</summary>
    public static int Compare(PersonaScope left, PersonaScope right)
    {
        var byKind = Rank(left.Kind).CompareTo(Rank(right.Kind));
        return byKind != 0 ? byKind : string.CompareOrdinal(left.Name, right.Name);
    }

    /// <summary>
    /// Reads a scope the way <c>write_persona</c> takes it: <c>global</c>, <c>project:&lt;name&gt;</c> or
    /// <c>domain:&lt;label&gt;</c>. The keyword ignores case; the name must follow the project naming rule.
    /// </summary>
    public static bool TryParse(string text, out PersonaScope scope)
    {
        scope = Global;
        if (string.Equals(text, "global", StringComparison.OrdinalIgnoreCase))
            return true;

        var colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
            return false;
        var keyword = text[..colon];
        var name = text[(colon + 1)..];
        if (!IsValidName(name))
            return false;

        if (string.Equals(keyword, "project", StringComparison.OrdinalIgnoreCase))
            scope = new PersonaScope(PersonaScopeKind.Project, name);
        else if (string.Equals(keyword, "domain", StringComparison.OrdinalIgnoreCase))
            scope = new PersonaScope(PersonaScopeKind.Domain, name);
        else
            return false;
        return true;
    }

    /// <summary>Whether a domain label or project name follows the naming rule, which is the same for both.</summary>
    public static bool IsValidName(string name) => PageId.IsProjectName(name);

    /// <summary>The block heading in the memory language: <c>领域：&lt;标签&gt;</c> or <c>Domain: &lt;label&gt;</c>, without its <c>## </c>.</summary>
    public string HeadingIn(MemoryLanguage language) => Word(language) + Separator(language) + Name;

    /// <summary>The structure word that names the scope's kind; null for the global scope.</summary>
    public string? Word(MemoryLanguage language) => Kind switch
    {
        PersonaScopeKind.Project => StructureWords.Project.In(language),
        PersonaScopeKind.Domain => StructureWords.Domain.In(language),
        _ => null,
    };

    /// <summary>The colon after a structure word: the full-width colon in Chinese, a colon and a space in English.</summary>
    internal static string Separator(MemoryLanguage language) => language == MemoryLanguage.Zh ? "：" : ": ";

    /// <summary>The scope a block heading names, as written; null when the heading does not name a domain or a project.</summary>
    internal static PersonaScope? OfHeading(string heading)
    {
        if (StructureWordAfter(heading, StructureWords.Domain) is { } domain)
            return new PersonaScope(PersonaScopeKind.Domain, domain);
        if (StructureWordAfter(heading, StructureWords.Project) is { } project)
            return new PersonaScope(PersonaScopeKind.Project, project);
        return null;
    }

    /// <summary>
    /// The text after a structure word and its colon, trimmed, in either language; null when the line does not start with
    /// the word followed by a colon. Chinese is matched as written, English ignoring case.
    /// </summary>
    internal static string? StructureWordAfter(string line, Localized word)
    {
        foreach (var form in new[] { word.Zh, word.En })
        {
            var comparison = form == word.Zh ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            if (!line.StartsWith(form, comparison))
                continue;
            var rest = line[form.Length..].TrimStart();
            if (rest.StartsWith('：') || rest.StartsWith(':'))
                return rest[1..].Trim();
        }

        return null;
    }

    private static int Rank(PersonaScopeKind kind) => kind switch
    {
        PersonaScopeKind.Domain => 0,
        PersonaScopeKind.Project => 1,
        _ => 2,
    };
}
