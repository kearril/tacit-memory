namespace TacitMemory.Core.Logging;

/// <summary>
/// Where the service log lives and how each event is written there: <c>TACIT_HOME/logs/tacit-YYYYMMDD.log</c>, one file a
/// local day, each event starting with a line of <c>time level message</c>. The CLI writes the files to this shape and the
/// service reads them back by it, so this is the one place the shape is defined.
/// </summary>
public static class ServiceLogFiles
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
}
