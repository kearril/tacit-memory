using TacitMemory.Core.Localization;
using TacitMemory.Core.Pages;

namespace TacitMemory.Core.Persona;

/// <summary>The three scopes a persona entry can have.</summary>
internal enum PersonaScopeKind
{
    Global,
    Workstream,
    Domain,
}

/// <summary>
/// Where a persona entry applies: everywhere (<see cref="Global"/>), in one workstream, or in one domain. The name is the
/// workstream name or the domain label, and is empty for the global scope. A name as written in a file may break the naming
/// rule; the format check reports that.
/// </summary>
internal sealed record PersonaScope(PersonaScopeKind Kind, string Name)
{
    public static readonly PersonaScope Global = new(PersonaScopeKind.Global, "");

    public bool IsGlobal => Kind == PersonaScopeKind.Global;

    /// <summary>The scope as <c>write_persona</c> spells it: <c>global</c>, <c>workstream:&lt;name&gt;</c> or <c>domain:&lt;label&gt;</c>.</summary>
    public override string ToString() => Kind switch
    {
        PersonaScopeKind.Workstream => "workstream:" + Name,
        PersonaScopeKind.Domain => "domain:" + Name,
        _ => "global",
    };

    /// <summary>The order of the scope blocks in <c>persona.md</c>: domains before workstreams, each by name.</summary>
    public static int Compare(PersonaScope left, PersonaScope right)
    {
        var byKind = Rank(left.Kind).CompareTo(Rank(right.Kind));
        return byKind != 0 ? byKind : string.CompareOrdinal(left.Name, right.Name);
    }

    /// <summary>
    /// Reads a scope the way <c>write_persona</c> takes it: <c>global</c>, <c>workstream:&lt;name&gt;</c> or
    /// <c>domain:&lt;label&gt;</c>. The keyword ignores case; the name must follow the workstream naming rule.
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

        if (string.Equals(keyword, "workstream", StringComparison.OrdinalIgnoreCase))
            scope = new PersonaScope(PersonaScopeKind.Workstream, name);
        else if (string.Equals(keyword, "domain", StringComparison.OrdinalIgnoreCase))
            scope = new PersonaScope(PersonaScopeKind.Domain, name);
        else
            return false;
        return true;
    }

    /// <summary>Whether a domain label or workstream name follows the naming rule, which is the same for both.</summary>
    public static bool IsValidName(string name) => PageId.IsWorkstreamName(name);

    /// <summary>
    /// The scope the index stored for a unit: the text <see cref="ToString"/> gives, read back without the naming check, so a
    /// name the file holds that breaks the rule still comes back as it was written.
    /// </summary>
    internal static PersonaScope Stored(string text)
    {
        if (text.StartsWith("workstream:", StringComparison.Ordinal))
            return new PersonaScope(PersonaScopeKind.Workstream, text["workstream:".Length..]);
        if (text.StartsWith("domain:", StringComparison.Ordinal))
            return new PersonaScope(PersonaScopeKind.Domain, text["domain:".Length..]);
        return Global;
    }

    /// <summary>The block heading in the memory language: <c>领域：&lt;标签&gt;</c> or <c>Domain: &lt;label&gt;</c>, without its <c>## </c>.</summary>
    public string HeadingIn(MemoryLanguage language) => Word(language) + Separator(language) + Name;

    /// <summary>The structure word that names the scope's kind; null for the global scope.</summary>
    public string? Word(MemoryLanguage language) => Kind switch
    {
        PersonaScopeKind.Workstream => StructureWords.Workstream.In(language),
        PersonaScopeKind.Domain => StructureWords.Domain.In(language),
        _ => null,
    };

    /// <summary>The colon after a structure word: the full-width colon in Chinese, a colon and a space in English.</summary>
    internal static string Separator(MemoryLanguage language) => language == MemoryLanguage.Zh ? "：" : ": ";

    /// <summary>The scope a block heading names, as written; null when the heading does not name a domain or a workstream.</summary>
    internal static PersonaScope? OfHeading(string heading)
    {
        if (StructureWordAfter(heading, StructureWords.Domain) is { } domain)
            return new PersonaScope(PersonaScopeKind.Domain, domain);
        if (StructureWordAfter(heading, StructureWords.Workstream) is { } workstream)
            return new PersonaScope(PersonaScopeKind.Workstream, workstream);
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
        PersonaScopeKind.Workstream => 1,
        _ => 2,
    };
}
