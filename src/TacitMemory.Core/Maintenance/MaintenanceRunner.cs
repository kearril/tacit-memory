using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Logging;
using TacitMemory.Core.Upkeep;

namespace TacitMemory.Core.Maintenance;

/// <summary>
/// Runs a maintenance operation with the memory to itself. First it waits for the background work already started (vector
/// fills, consolidation judgments and automatic backups) to finish, so the operation never races them. While it runs, memory
/// tool calls, server instructions, the consolidation report and background syncs wait, then run. The operation must not call
/// the read or write paths, which would wait for it. Its start, its end and its failure are logged.
/// </summary>
internal sealed class MaintenanceRunner(MaintenanceGate gate, DerivedUpkeep upkeep, ServiceLog log, Func<Task[]> background)
{
    public async Task<T> RunAsync<T>(string name, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        // An automatic backup takes the write slot, which this operation holds alone, so it is waited for before the gate.
        // The refreshes scheduled before it run first, so none lands in the middle of the operation.
        await upkeep.SettledAsync();
        using var hold = await gate.MaintenanceAsync(cancellationToken);
        await Task.WhenAll(background().Select(Settled));
        log.Record(LogLevel.Information, LogMessages.MaintenanceStarted, name);
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await operation(cancellationToken);
            log.Record(LogLevel.Information, LogMessages.MaintenanceFinished, name, (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return result;
        }
        catch (Exception e)
        {
            log.Record(LogLevel.Warning, LogMessages.MaintenanceFailed, name, (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds, e.Message);
            throw;
        }
    }

    /// <summary>A task that completes once <paramref name="task"/> has, whether it finished or failed: its failure is reported where it happened.</summary>
    private static Task Settled(Task task) => task.ContinueWith(_ => { }, TaskScheduler.Default);
}
