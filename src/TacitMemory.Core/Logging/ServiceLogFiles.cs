using System.Globalization;
using System.Text.RegularExpressions;

namespace TacitMemory.Core.Logging;

/// <summary>
/// Where the service log lives and how each event is written there: <c>TACIT_HOME/logs/tacit-YYYYMMDD.log</c>, one file a
/// local day, each event starting with a line of <c>time level message</c>. The CLI writes the files to this shape and the
/// service reads them back by it, so this is the one place the shape is defined.
/// </summary>
public static partial class ServiceLogFiles
{
    /// <summary>
    /// The Serilog output template of an event: the time to the millisecond with its offset, the level as three capitals
    /// (<c>INF</c>, <c>WRN</c>, <c>ERR</c>), then the message, and the exception on the lines after it when there is one.
    /// </summary>
    public const string OutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3} {Message:lj}{NewLine}{Exception}";

    /// <summary>How many daily files are kept; older ones are deleted as a new day starts.</summary>
    public const int RetainedDays = 14;

    /// <summary>The directory of the log files.</summary>
    public static string DirectoryOf(string tacitHome) => Path.Combine(tacitHome, "logs");

    /// <summary>The path Serilog rolls from: it puts the day's date before the extension.</summary>
    public static string RollingPathOf(string tacitHome) => Path.Combine(DirectoryOf(tacitHome), "tacit-.log");

    /// <summary>The file of one day: the name Serilog gives it.</summary>
    internal static string PathOf(string tacitHome, DateOnly day) =>
        Path.Combine(DirectoryOf(tacitHome), $"tacit-{day.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");

    /// <summary>The day a file name is the log of, or null when it is not a log file's name.</summary>
    internal static DateOnly? DayOf(string fileName)
    {
        var match = FileName().Match(fileName);
        return match.Success && DateOnly.TryParseExact(match.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;
    }

    /// <summary>
    /// The start of a line that begins an event, as <see cref="OutputTemplate"/> writes it: the time, the level code and the
    /// message after one space each.
    /// </summary>
    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) (VRB|DBG|INF|WRN|ERR|FTL) (.*)$")]
    internal static partial Regex EventLine();

    /// <summary>The format of the time at the start of an event line.</summary>
    internal const string TimeFormat = "yyyy-MM-dd HH:mm:ss.fff zzz";

    [GeneratedRegex(@"^tacit-(\d{8})\.log$")]
    private static partial Regex FileName();
}
