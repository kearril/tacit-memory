using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace TacitMemory.Core.Tests;

/// <summary>A logger factory that keeps every event it is given, so a test can read what the service wrote.</summary>
internal sealed class CapturingLoggerFactory : ILoggerFactory
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    /// <summary>Every event written so far, in the order it was written.</summary>
    public IReadOnlyList<LogEntry> Entries => [.. _entries];

    /// <summary>Every message and exception message joined, so a search finds any text that reached the log.</summary>
    public string AllText => string.Join('\n', _entries.Select(entry => entry.Message + "\n" + entry.Exception?.Message));

    public ILogger CreateLogger(string categoryName) => new Writer(categoryName, _entries);

    public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException("Tests add no providers.");

    public void Dispose()
    {
    }

    private sealed class Writer(string category, ConcurrentQueue<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                entries.Enqueue(new LogEntry(logLevel, category, formatter(state, exception), exception));
        }
    }
}

/// <summary>One event a <see cref="CapturingLoggerFactory"/> kept.</summary>
internal sealed record LogEntry(LogLevel Level, string Category, string Message, Exception? Exception);
