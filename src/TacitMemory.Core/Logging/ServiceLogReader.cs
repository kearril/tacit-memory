using System.Globalization;
using System.Text;

namespace TacitMemory.Core.Logging;

/// <summary>
/// Reads the service log back by the shape <see cref="ServiceLogFiles"/> defines. Every read parses the whole file: a day's
/// file is tens of kilobytes, and parsing again keeps an event's position a plain count, which holds because the files are
/// only ever appended to.
/// </summary>
internal static class ServiceLogReader
{
    /// <summary>The days with a log file, newest first; none when the directory does not exist.</summary>
    public static IReadOnlyList<DateOnly> Days(string tacitHome)
    {
        var directory = ServiceLogFiles.DirectoryOf(tacitHome);
        if (!Directory.Exists(directory))
            return [];
        return [.. Directory.EnumerateFiles(directory)
            .Select(path => ServiceLogFiles.DayOf(Path.GetFileName(path)))
            .OfType<DateOnly>()
            .OrderDescending()];
    }

    /// <summary>The events of one day, oldest first, or null when the day has no file.</summary>
    public static IReadOnlyList<ServiceLogEntry>? Read(string tacitHome, DateOnly day)
    {
        string text;
        try
        {
            // The service holds the file open for writing; Serilog's shared mode lets others read and roll it meanwhile.
            using var stream = new FileStream(
                ServiceLogFiles.PathOf(tacitHome, day), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            text = reader.ReadToEnd();
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        return Parse(text);
    }

    /// <summary>
    /// Splits the text into events. A line that starts with a time and a level begins one; any other line belongs to the event
    /// before it, or, before the first, to an event of its own without a time. A last line without its line break is still
    /// being written and is left for the next read.
    /// </summary>
    private static List<ServiceLogEntry> Parse(string text)
    {
        var entries = new List<ServiceLogEntry>();
        var end = text.LastIndexOf('\n');
        if (end < 0)
            return entries;
        DateTimeOffset? time = null;
        var level = ServiceLogLevel.Information;
        StringBuilder? message = null;

        foreach (var raw in text[..end].Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var start = ServiceLogFiles.EventLine().Match(line);
            if (start.Success)
            {
                Flush();
                time = DateTimeOffset.ParseExact(start.Groups[1].Value, ServiceLogFiles.TimeFormat, CultureInfo.InvariantCulture);
                level = LevelOf(start.Groups[2].Value);
                message = new StringBuilder(start.Groups[3].Value);
            }
            else if (message is null)
            {
                if (line.Length == 0)
                    continue;
                message = new StringBuilder(line);
            }
            else
            {
                message.Append('\n').Append(line);
            }
        }
        Flush();
        return entries;

        void Flush()
        {
            if (message is not null)
                entries.Add(new ServiceLogEntry(time, level, message.ToString().TrimEnd('\n')));
            message = null;
        }
    }

    /// <summary>The level of a code; the service writes from Information up, so the codes below it are read as Information.</summary>
    private static ServiceLogLevel LevelOf(string code) => code switch
    {
        "WRN" => ServiceLogLevel.Warning,
        "ERR" or "FTL" => ServiceLogLevel.Error,
        _ => ServiceLogLevel.Information,
    };
}
