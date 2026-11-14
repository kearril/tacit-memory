namespace TacitMemory.Core;

/// <summary>The days the service log has a file for, and today by the service's local date, which may have none yet.</summary>
/// <param name="Today">Today on the machine the service runs on: the day the log is being written to.</param>
/// <param name="Dates">The days with a log file, newest first.</param>
public sealed record ServiceLogDays(DateOnly Today, IReadOnlyList<DateOnly> Dates);

/// <summary>The events of one day's service log, oldest first.</summary>
/// <param name="Date">The day read.</param>
/// <param name="Today">Today on the machine the service runs on; a later day than <paramref name="Date"/> means that day's file is finished.</param>
/// <param name="Entries">The events read.</param>
/// <param name="Next">How many events the file holds so far.</param>
public sealed record ServiceLogDay(DateOnly Date, DateOnly Today, IReadOnlyList<ServiceLogEntry> Entries, int Next);

/// <summary>One event of the service log.</summary>
/// <param name="Time">When it was written, with the offset of the machine; null for lines before the file's first event.</param>
/// <param name="Level">How serious it is.</param>
/// <param name="Message">The message, with the lines that follow it (a list of reasons, an exception) joined by line breaks.</param>
public sealed record ServiceLogEntry(DateTimeOffset? Time, ServiceLogLevel Level, string Message);

/// <summary>The three levels the service log is read at.</summary>
public enum ServiceLogLevel
{
    Information,
    Warning,
    Error,
}
