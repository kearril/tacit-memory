using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace TacitMemory.Core.Tests;

/// <summary>A logger factory that keeps every event it is given, so a test can read what the service wrote.</summary>
internal sealed class CapturingLoggerFactory : ILoggerFactory
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private readonly Lock _gate = new();
    private TaskCompletionSource _written = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Every event written so far, in the order it was written.</summary>
    public IReadOnlyList<LogEntry> Entries => [.. _entries];

    /// <summary>Every message and exception message joined, so a search finds any text that reached the log.</summary>
    public string AllText => string.Join('\n', _entries.Select(entry => entry.Message + "\n" + entry.Exception?.Message));

    /// <summary>
    /// The first event that matches, waiting for it when the service has not written it yet: the event of an API call is
    /// written once its answer has gone out, so a client can hold the answer before the event exists. Gives up after ten
    /// seconds.
    /// </summary>
    public async Task<LogEntry> WaitForAsync(Func<LogEntry, bool> match)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            Task written;
            lock (_gate)
                written = _written.Task;
            if (_entries.FirstOrDefault(match) is { } entry)
                return entry;
            await written.WaitAsync(timeout.Token);
        }
    }

    public ILogger CreateLogger(string categoryName) => new Writer(categoryName, Add);

    public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException("Tests add no providers.");

    public void Dispose()
    {
    }

    private void Add(LogEntry entry)
    {
        _entries.Enqueue(entry);
        TaskCompletionSource written;
        lock (_gate)
        {
            written = _written;
            _written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        written.SetResult();
    }

    private sealed class Writer(string category, Action<LogEntry> add) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                add(new LogEntry(logLevel, category, formatter(state, exception), exception));
        }
    }
}

/// <summary>One event a <see cref="CapturingLoggerFactory"/> kept.</summary>
internal sealed record LogEntry(LogLevel Level, string Category, string Message, Exception? Exception);
