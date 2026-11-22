using System.Globalization;
using TacitMemory.Core.Index;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Pages;
using TacitMemory.Core.Persona;
using TacitMemory.Core.Recent;
using TacitMemory.Core.Workstreams;

namespace TacitMemory.Core.Retrieval;

/// <summary>
/// The workstream brief: what <c>recall</c> returns for a workstream alone. It opens with the workstream's background and recent
/// line when it is established, then gathers the workstream's conventions (its workstream-scope
/// persona entries), its open items (the leftovers of its work pages), a page index grouped by type, its last three
/// activities and the latest weekly summary of a week it had activity in. The text is capped at <c>limits.brief</c>; only the
/// page index is shortened, oldest entry first, and the omission is said. <see cref="Compose"/> counts a return for each page it
/// lists; <see cref="Gather"/> returns the same parts whole and counts nothing. Neither changes a file.
/// </summary>
internal sealed class WorkstreamBrief(TacitConfig config, MemoryIndex index)
{
    /// <summary>How many activities the brief lists, newest first.</summary>
    private const int ListedActivities = 3;

    /// <summary>The group headings of the page index, in the order the brief lists them.</summary>
    private const string DecisionsHeading = "Decisions";
    private const string IncidentsHeading = "Incidents";
    private const string WorkHeading = "Work";
    private const string EntitiesHeading = "Entities";
    private const string UntypedHeading = "Untyped";

    /// <summary>The text of the brief <paramref name="parts"/> make, and the pages it lists.</summary>
    public (string Text, IReadOnlyList<PageId> Listed) Compose(Parts parts)
    {
        var limit = config.Limits.Brief;
        var shown = new HashSet<Entry>(parts.Entries);
        var omitted = 0;
        var text = Render(parts, shown, omitted, limit);
        // Only the page index is shortened: the oldest entries go first, until the brief fits or none is left.
        foreach (var oldest in parts.Entries.AsEnumerable().Reverse())
        {
            if (text.Length <= limit)
                break;
            shown.Remove(oldest);
            omitted++;
            text = Render(parts, shown, omitted, limit);
        }

        text = CutAtLimit(text, limit);
        var listed = shown.Select(entry => entry.Id).Concat(parts.Overturned).Concat(parts.OpenItems.Select(item => item.Id)).Distinct().ToList();
        return (text, listed);
    }

    /// <summary>
    /// Everything the brief lists for <paramref name="workstream"/>, whole and in the brief's order; <c>null</c> when the workstream is not established and has no pages and no activities.
    /// </summary>
    public Parts? Gather(string workstream, DateOnly? from, DateOnly? to)
    {
        var pages = PageEntries(workstream, from, to);
        var activities = ActivitiesOf(workstream, from, to);
        // An established workstream has its background to say even when it has no pages or activities yet.
        var established = WorkstreamFile.Established(config.MemoryPath).FirstOrDefault(entry => entry.Name == workstream);
        if (pages.Count == 0 && activities.Count == 0 && established is null)
            return null;

        // Overturned decisions are identifiers only: they are never listed as entries, so truncation does not touch them.
        var superseded = index.SupersededPages();
        var entries = pages.Where(entry => !IsOverturned(entry, superseded)).ToList();
        var overturned = pages.Where(entry => IsOverturned(entry, superseded)).Select(entry => entry.Id).ToList();
        var recent = activities.AsEnumerable().Reverse().OrderByDescending(activity => activity.Day).Take(ListedActivities).ToList();
        return new Parts(
            workstream, established?.Background, established?.Recent, Conventions(workstream), OpenItems(pages), entries, overturned, recent,
            LatestWeekSummary(activities));
    }

    /// <summary>A page of the workstream as the brief sees it: its identifier, its document, its last changed date, its use count and the last day it was returned (yyyy-MM-dd).</summary>
    public sealed record Entry(PageId Id, PageDocument Document, DateOnly? Changed, long Uses, string? LastUse);

    /// <summary>An open item: a work page and the non-blank lines of its leftovers.</summary>
    public sealed record OpenItem(PageId Id, IReadOnlyList<string> Lines);

    /// <summary>Everything the brief lists, gathered before any of it is shortened.</summary>
    public sealed record Parts(
        string Workstream,
        string? Background,
        string? Recent,
        PersonaBlock? Conventions,
        IReadOnlyList<OpenItem> OpenItems,
        IReadOnlyList<Entry> Entries,
        IReadOnlyList<PageId> Overturned,
        IReadOnlyList<RecentFile.Activity> Activities,
        Summary? Summary);

    /// <summary>
    /// The workstream's pages within the date range, newest first: by the latest date among their sections, then by how often
    /// they were returned, then by identifier.
    /// </summary>
    private List<Entry> PageEntries(string workstream, DateOnly? from, DateOnly? to)
    {
        var folder = MemoryLayout.Full(config.MemoryPath, MemoryLayout.WorkstreamPages(workstream));
        if (!Directory.Exists(folder))
            return [];

        var uses = index.PageUsages().ToDictionary(row => row.Page, StringComparer.Ordinal);
        var entries = new List<Entry>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.md", SearchOption.TopDirectoryOnly))
        {
            if (!PageId.TryParse($"{workstream}/{Path.GetFileNameWithoutExtension(file)}", out var id, out _))
                continue;
            if (PageDocument.TryLoad(config.MemoryPath, id) is not { } document || !InRange(document.Header, from, to))
                continue;

            index.SyncPage(id);
            var changes = index.SectionChanges(id);
            var changed = changes.Count > 0 ? changes.Values.Max() : document.Header.Last;
            var use = uses.GetValueOrDefault(id.ToString());
            entries.Add(new Entry(id, document, changed, use?.Count ?? 0, use?.Last));
        }

        return entries
            .OrderByDescending(entry => entry.Changed ?? DateOnly.MinValue)
            .ThenByDescending(entry => entry.Uses)
            .ThenBy(entry => entry.Id.ToString(), StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>A page is in range when its dates overlap the range, as the search filter compares them.</summary>
    private static bool InRange(PageHeader header, DateOnly? from, DateOnly? to) =>
        (from is null || (header.Last is { } last && last >= from))
        && (to is null || (header.First is { } first && first <= to));

    /// <summary>The workstream's activities in the date range, in file order.</summary>
    private List<RecentFile.Activity> ActivitiesOf(string workstream, DateOnly? from, DateOnly? to)
    {
        return RecentFile.AllActivities(config.MemoryPath)
            .Where(activity =>
                activity.Workstream == workstream
                && (from is null || activity.Day >= from)
                && (to is null || activity.Day <= to))
            .ToList();
    }

    /// <summary>The workstream-scope block of the persona; <c>null</c> when it has none.</summary>
    private PersonaBlock? Conventions(string workstream) =>
        PersonaDocument.Read(MemoryLayout.Full(config.MemoryPath, MemoryLayout.Persona))?.BlockOf(new PersonaScope(PersonaScopeKind.Workstream, workstream));

    /// <summary>The conventions as the brief lists them: each entry with its non-comment continuation lines.</summary>
    private static List<string> ConventionLines(PersonaBlock? block) =>
        block is null
            ? []
            : block.Entries
                .SelectMany(item => new[] { "- " + item.Text }.Concat(item.ContinuationWithoutComments.Select(line => "  " + line.Trim())))
                .ToList();

    /// <summary>The non-blank lines of the "leftovers" section of each work page, in the page order.</summary>
    private static IReadOnlyList<OpenItem> OpenItems(IEnumerable<Entry> entries) =>
        entries
            .Where(entry => entry.Document.Header.Type == ToolValues.PageTypes.Work && entry.Document.Leftovers is not null)
            .Select(entry => new OpenItem(entry.Id, Lines(entry.Document.Leftovers!.Body)))
            .Where(item => item.Lines.Count > 0)
            .ToList();

    /// <summary>Whether a decision page is overturned: its <c>Overturned</c> section is not blank or another page supersedes it.</summary>
    private static bool IsOverturned(Entry entry, HashSet<string> superseded) =>
        entry.Document.Header.Type == ToolValues.PageTypes.Decision
        && (entry.Document.HasOverturnedSection || superseded.Contains(entry.Id.ToString()));

    /// <summary>The group a page is listed under; a page with no valid type is untyped.</summary>
    private static string GroupOf(PageDocument document) => document.Header.Type switch
    {
        ToolValues.PageTypes.Decision => DecisionsHeading,
        ToolValues.PageTypes.Incident => IncidentsHeading,
        ToolValues.PageTypes.Work => WorkHeading,
        ToolValues.PageTypes.Entity => EntitiesHeading,
        _ => UntypedHeading,
    };

    /// <summary>The latest week summary of a week in which the workstream had activity; null when none has one.</summary>
    private Summary? LatestWeekSummary(IReadOnlyList<RecentFile.Activity> activities)
    {
        var weeks = activities.Select(activity => SummaryLines.WeekOf(activity.Day).Label).ToHashSet(StringComparer.Ordinal);
        Summary? latest = null;
        foreach (var (_, file) in RecentFile.MonthFiles(config.MemoryPath))
        {
            if (TextFile.Read(file) is not { } read)
                continue;
            foreach (var summary in SummaryLines.All(read.Text))
            {
                if (summary.Kind == SummaryKind.Week && weeks.Contains(summary.Label) && (latest is null || summary.First > latest.First))
                    latest = summary;
            }
        }

        return latest;
    }

    /// <summary>The brief as text. The page index keeps only the entries in <paramref name="shown"/>; <paramref name="omitted"/> says how many were left out.</summary>
    private static string Render(Parts parts, HashSet<Entry> shown, int omitted, int limit)
    {
        var header = $"Workstream brief: {parts.Workstream}";
        if (parts.Background is not null)
            header += "\nBackground: " + parts.Background;
        if (parts.Recent is not null)
            header += "\nRecent: " + parts.Recent;
        var blocks = new List<string> { header };
        var conventions = ConventionLines(parts.Conventions);
        if (conventions.Count > 0)
            blocks.Add(Block("Conventions:", conventions));

        if (parts.OpenItems.Count > 0)
        {
            var open = parts.OpenItems.SelectMany(item => new[] { "- " + item.Id }.Concat(item.Lines.Select(line => "  " + line)));
            blocks.Add(Block("Open items:", open.ToList()));
        }

        var directory = DirectoryLines(parts, shown);
        if (omitted > 0)
            directory.Add($"{omitted} page entries omitted (oldest first; limits.brief is {limit} characters).");
        if (directory.Count > 0)
            blocks.Add(string.Join('\n', directory));

        if (parts.Activities.Count > 0)
            blocks.Add(Block("Recent activities:", parts.Activities.Select(activity =>
                $"- {activity.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} {activity.MachineAfterDate}[{activity.Code}] {activity.Body}").ToList()));

        if (parts.Summary is not null)
            blocks.Add(Block("Latest weekly summary:", ["- " + parts.Summary.Text]));

        return string.Join("\n\n", blocks);
    }

    /// <summary>The page index: one group per type that has a page shown, decisions first, with the overturned line under them.</summary>
    private static List<string> DirectoryLines(Parts parts, HashSet<Entry> shown)
    {
        var lines = new List<string>();
        var groups = new[] { DecisionsHeading, IncidentsHeading, WorkHeading, EntitiesHeading, UntypedHeading };
        foreach (var group in groups)
        {
            var members = parts.Entries.Where(entry => shown.Contains(entry) && GroupOf(entry.Document) == group).ToList();
            var overturned = group == DecisionsHeading ? parts.Overturned : [];
            if (members.Count == 0 && overturned.Count == 0)
                continue;

            lines.Add(group + ":");
            lines.AddRange(members.Select(Describe));
            if (overturned.Count > 0)
                lines.Add("Overturned: " + string.Join(", ", overturned.Select(id => id.ToString())));
        }

        return lines;
    }

    /// <summary>One page's line: identifier, title, aliases and last changed date.</summary>
    private static string Describe(Entry entry)
    {
        var header = entry.Document.Header;
        var aliases = header.Aliases.Count > 0 ? $" (aliases: {string.Join(", ", header.Aliases)})" : "";
        var changed = entry.Changed is { } date ? $" (last changed {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)})" : "";
        return $"- {entry.Id}: {header.Title}{aliases}{changed}";
    }

    private static string Block(string heading, IReadOnlyList<string> lines) => heading + "\n" + string.Join('\n', lines);

    /// <summary>The non-blank lines of a section body, without their line breaks.</summary>
    private static List<string> Lines(string body) =>
        body.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Trim().Length > 0).ToList();

    /// <summary>
    /// Cuts the text to <paramref name="limit"/> characters when it is still longer, keeping its start and ending with a note.
    /// Used only once the page index has nothing left to shorten.
    /// </summary>
    private static string CutAtLimit(string text, int limit)
    {
        if (text.Length <= limit)
            return text;

        var note = $"Brief cut at limits.brief ({limit} characters).";
        var keep = Math.Max(0, limit - note.Length - 1);
        if (keep == 0)
            return note[..Math.Min(note.Length, limit)];
        if (char.IsHighSurrogate(text[keep - 1]))
            keep--;
        return text[..keep] + "\n" + note;
    }
}
