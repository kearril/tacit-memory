using TacitMemory.Core.Localization;
using TacitMemory.Core.Persona;
using TacitMemory.Core.Recent;

namespace TacitMemory.Core.Injection;

/// <summary>
/// What the resident block shows beyond the persona, read from the memory repository as it stands now. Each list holds
/// the lines the block prints, already formatted, in order; an empty list means the section is left out.
/// </summary>
/// <param name="Persona">The parsed <c>persona.md</c>: the global sections are printed, the scope blocks only through their index rows.</param>
/// <param name="SituationalRules">One row per domain, up to the domain limit: its label, its trigger and its entry count.</param>
/// <param name="Mainline">The mainline items.</param>
/// <param name="RecentWeeks">The latest completed weeks that hold anything, newest first.</param>
/// <param name="Projects">The project names on one line; empty when there are none.</param>
internal sealed record ResidentContent(
    PersonaDocument Persona,
    IReadOnlyList<string> SituationalRules,
    IReadOnlyList<string> Mainline,
    IReadOnlyList<string> RecentWeeks,
    IReadOnlyList<string> Projects)
{
    /// <summary>Reads the persona's domains, the mainline, the recent weeks and the project list.</summary>
    public static ResidentContent Read(TacitConfig config, DateOnly today, PersonaDocument persona)
    {
        var language = config.Language;
        var recent = RecentSummary.Read(config.MemoryPath, today, language);
        var domains = persona.Blocks
            .Where(block => block.Scope.Kind == PersonaScopeKind.Domain)
            .Take(config.Limits.Domains)
            .Select(block => DomainRow(block, language))
            .ToList();
        var mainline = MainlineSection.ReadItems(config.MemoryPath).Select(item => "- " + item).ToList();
        var projects = ProjectNames(config.MemoryPath, recent.ProjectLabels);

        return new ResidentContent(
            persona,
            domains,
            mainline,
            recent.RecentWeeks,
            projects.Count == 0 ? [] : [string.Join(", ", projects)]);
    }

    /// <summary>
    /// The project names: the directories under <c>pages/</c> that do not start with an underscore or a dot, together with
    /// every project label in the activity log; sorted by name, each once.
    /// </summary>
    public static IReadOnlyList<string> ProjectNames(string memoryPath, IEnumerable<string> activityProjects)
    {
        var pages = Path.Combine(memoryPath, "pages");
        IEnumerable<string> directories = Directory.Exists(pages)
            ? Directory.EnumerateDirectories(pages).Select(Path.GetFileName).OfType<string>()
            : Enumerable.Empty<string>();
        return directories
            .Where(name => !name.StartsWith('_') && !name.StartsWith('.'))
            .Concat(activityProjects)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>A domain's index row: its label, its trigger and its entry count, across its four sections.</summary>
    private static string DomainRow(PersonaBlock block, MemoryLanguage language)
    {
        var entries = ResidentMessages.Entries(language, block.Entries.Count);
        return string.IsNullOrWhiteSpace(block.Trigger)
            ? ResidentMessages.DomainRowWithoutTrigger.Format(language, block.Scope.Name, entries)
            : ResidentMessages.DomainRow.Format(language, block.Scope.Name, block.Trigger, entries);
    }
}
