using TacitMemory.Core.Localization;
using TacitMemory.Core.Persona;
using TacitMemory.Core.Recent;
using TacitMemory.Core.Workstreams;

namespace TacitMemory.Core.Injection;

/// <summary>
/// What the resident block shows beyond the persona, read from the memory repository as it stands now. Each list holds
/// the lines the block prints, already formatted, in order; an empty list means the section is left out.
/// </summary>
/// <param name="Persona">The parsed <c>persona.md</c>: the global sections are printed, the scope blocks only through their index rows.</param>
/// <param name="SituationalRules">One row per domain: its label, its trigger and its entry count.</param>
/// <param name="Workstreams">One row per established workstream: active ones with their recent line, quiet ones without it.</param>
internal sealed record ResidentContent(
    PersonaDocument Persona,
    IReadOnlyList<string> SituationalRules,
    IReadOnlyList<string> Workstreams)
{
    /// <summary>A workstream is active when it has an activity in the last this many days, counting today.</summary>
    public const int ActiveDays = 28;

    /// <summary>Reads the persona's domains and the established workstreams as they stand on <paramref name="today"/>.</summary>
    public static ResidentContent Read(TacitConfig config, DateOnly today, PersonaDocument persona)
    {
        var language = config.Language;
        var domains = persona.Blocks
            .Where(block => block.Scope.Kind == PersonaScopeKind.Domain)
            .Select(block => DomainRow(block, language))
            .ToList();
        return new ResidentContent(persona, domains, WorkstreamRows(config, today));
    }

    /// <summary>
    /// The workstream rows: the established workstreams, the active ones first, newest activity first, then the quiet ones by
    /// name. An active row shows the background and the recent line; a quiet row shows only the background.
    /// </summary>
    public static IReadOnlyList<string> WorkstreamRows(TacitConfig config, DateOnly today)
    {
        var established = WorkstreamFile.Established(config.MemoryPath);
        if (established.Count == 0)
            return [];

        var latest = LatestActivityDays(config.MemoryPath, today);
        var windowStart = today.AddDays(1 - ActiveDays);
        var active = established
            .Where(entry => latest.TryGetValue(entry.Name, out var day) && day >= windowStart && day <= today)
            .OrderByDescending(entry => latest[entry.Name])
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ToList();
        var quiet = established
            .Where(entry => !active.Contains(entry))
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ToList();

        var language = config.Language;
        return active.Select(entry => ActiveRow(entry, language))
            .Concat(quiet.Select(entry => ResidentMessages.WorkstreamRow.Format(language, entry.Name, entry.Background)))
            .ToList();
    }

    /// <summary>
    /// The newest activity day not after <paramref name="today"/> of each workstream label in the activity log. A day after
    /// today is left out, so a mistyped future date neither hides the real activity nor moves the workstream up the order.
    /// </summary>
    private static Dictionary<string, DateOnly> LatestActivityDays(string memoryPath, DateOnly today)
    {
        var latest = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        foreach (var (_, file) in RecentFile.MonthFiles(memoryPath))
        {
            if (TextFile.Read(file) is not { } read)
                continue;
            foreach (var activity in RecentFile.Activities(read.Text))
            {
                if (activity.Workstream is not { } name || activity.Day > today)
                    continue;
                if (!latest.TryGetValue(name, out var day) || activity.Day > day)
                    latest[name] = activity.Day;
            }
        }

        return latest;
    }

    private static string ActiveRow(WorkstreamEntry entry, MemoryLanguage language) =>
        entry.Recent is null
            ? ResidentMessages.WorkstreamRow.Format(language, entry.Name, entry.Background)
            : ResidentMessages.WorkstreamRowWithRecent.Format(language, entry.Name, entry.Background, entry.Recent);

    /// <summary>A domain's index row: its label, its trigger and its entry count, across its four sections.</summary>
    private static string DomainRow(PersonaBlock block, MemoryLanguage language)
    {
        var entries = ResidentMessages.Entries(language, block.Entries.Count);
        return string.IsNullOrWhiteSpace(block.Trigger)
            ? ResidentMessages.DomainRowWithoutTrigger.Format(language, block.Scope.Name, entries)
            : ResidentMessages.DomainRow.Format(language, block.Scope.Name, block.Trigger, entries);
    }
}
