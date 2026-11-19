using System.Globalization;
using System.Text;

namespace TacitMemory.Core.Consolidation;

/// <summary>
/// The text of the consolidation report resource: the repository's absolute path with how to run git there, today's date, and
/// the consolidation report. Everything is computed from the repository as it is when the resource is read; the steps that use
/// it are in the tacit-consolidate skill.
/// </summary>
internal static class ConsolidationResource
{
    public static async Task<string> RenderAsync(TacitOptions options, TacitConfig config, ConsolidationServices services, CancellationToken cancellationToken)
    {
        var path = config.MemoryPath;
        var report = await ConsolidationReport.BuildAsync(config, services, options.Today, Sync.MemorySync.TryReadLast(options), cancellationToken);

        var text = new StringBuilder();
        text.Append("# TacitMemory consolidation report\n\n");
        text.Append("Today is ").Append(options.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(".\n");
        text.Append("The memory repository is at ").Append(path).Append(". Edit its files by that absolute path, and run git in that directory (git -C \"")
            .Append(path).Append("\" ...), whatever the current directory is.\n\n");
        text.Append(report.Render());
        return text.ToString();
    }
}
