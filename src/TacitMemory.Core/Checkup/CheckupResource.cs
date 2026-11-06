using System.Globalization;
using System.Text;

namespace TacitMemory.Core.Checkup;

/// <summary>
/// The text of the checkup report resource: the repository's absolute path with how to run git there, today's date, and
/// the checkup report. Everything is computed from the repository as it is when the resource is read; the steps that use
/// it are in the tacit-checkup skill.
/// </summary>
internal static class CheckupResource
{
    public static async Task<string> RenderAsync(TacitOptions options, TacitConfig config, CheckupServices services, CancellationToken cancellationToken)
    {
        var path = config.MemoryPath;
        var report = await CheckupReport.BuildAsync(config, services, options.Today, cancellationToken);

        var text = new StringBuilder();
        text.Append("# TacitMemory checkup report\n\n");
        text.Append("Today is ").Append(options.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(".\n");
        text.Append("The memory repository is at ").Append(path).Append(". Edit its files by that absolute path, and run git in that directory (git -C \"")
            .Append(path).Append("\" ...), whatever the current directory is.\n\n");
        text.Append(report.Render());
        return text.ToString();
    }
}
