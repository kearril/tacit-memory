using System.Text;
using TacitMemory.Core.Injection;

namespace TacitMemory.Core.Recent;

/// <summary>
/// The text of the <c>remember</c> prompt: the steps for writing the session into memory, followed by today's
/// activities with their short codes and the current mainline with its version code, as the service reads them now.
/// </summary>
internal static class RememberText
{
    public static string Render(TacitOptions options, TacitConfig config, string? focus)
    {
        var today = options.Today;
        var recent = TextFile.Read(Path.Combine(config.MemoryPath, RecentFile.RelativePath(today)))?.Text ?? "";
        var mainline = TextFile.Read(Path.Combine(config.MemoryPath, MainlineSection.RelativePath))?.Text ?? "";
        var items = MainlineSection.Items(mainline);

        var text = new StringBuilder();
        text.Append("Remember this session in the TacitMemory memory now. Write without asking the user first.\n");
        if (!string.IsNullOrWhiteSpace(focus))
            text.Append("Focus: ").Append(focus.Trim()).Append('\n');
        text.Append('\n');
        text.Append("Steps:\n");
        text.Append("1. Review this session for what is worth recording, by the \"Worth recording\" rules.\n");
        text.Append("2. Before writing a detail page, search with recall. Update the page that covers the topic with write_page (upsert_section, using the hash recall shows); create a page only when no existing page fits.\n");
        text.Append("3. Write one activity with write_recent (add_activity): what the user did and worked out, in one line, with the project when it belongs to one. If this session already wrote the same matter, rewrite that activity with replaces (its short code is listed below or was in an earlier reply) instead of adding another.\n");
        text.Append("4. Consider rewriting the mainline with write_recent (update_mainline), passing the version code below, only when a direction that will last weeks has appeared, or a mainline direction has clearly ended. Otherwise leave it.\n");
        text.Append("5. When done, list what you wrote, one line each, with its page ID or short code. Do not ask for confirmation before writing.\n");
        text.Append('\n');

        text.Append("Today's activities (").Append(today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)).Append("):\n");
        var entries = RecentFile.EntriesOn(recent, today);
        if (entries.Count == 0)
            text.Append("(none)\n");
        foreach (var entry in entries)
            text.Append("- [").Append(entry.Code).Append("] ").Append(entry.Body).Append('\n');
        text.Append('\n');

        text.Append("Mainline, version [").Append(MainlineSection.VersionOf(items)).Append("]:\n");
        if (items.Count == 0)
            text.Append("(empty)\n");
        foreach (var item in items)
            text.Append("- ").Append(item).Append('\n');

        return text.ToString();
    }
}
