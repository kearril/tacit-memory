using Microsoft.Extensions.Logging;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Logging;
using TacitMemory.Core.Maintenance;

namespace TacitMemory.Core.Sync;

/// <summary>
/// The syncs nobody waits for: the one after a write, which pushes it, and the timed one every <c>sync.pullMinutes</c>,
/// which brings the other machine's commits and pushes what an earlier sync could not. Each runs in the background as a
/// write-slot task of the <see cref="MaintenanceGate"/>, one at a time; a sync asked for while another waits to start is
/// the same sync, so a burst of writes leads to one. Belongs to one runtime: a reload makes a new one.
/// </summary>
internal sealed class SyncSchedule : IDisposable
{
    private readonly MemorySync _sync;
    private readonly MaintenanceGate _gate;
    private readonly ServiceLog _log;
    private readonly ITimer? _timer;
    private readonly TimeSpan _period;
    private readonly Lock _lock = new();
    private Task _completion = Task.CompletedTask;
    private bool _waiting;

    public SyncSchedule(MemorySync sync, MaintenanceGate gate, ServiceLog log, TimeProvider time, SyncConfig config)
    {
        _sync = sync;
        _gate = gate;
        _log = log;
        _period = TimeSpan.FromMinutes(config.PullMinutes);
        // A one-shot timer armed again on each tick, so ticks never pile up behind a sync that is slow to finish.
        if (sync.Enabled && config.PullMinutes > 0)
            _timer = time.CreateTimer(_ => OnTick(), null, _period, Timeout.InfiniteTimeSpan);
    }

    private void OnTick()
    {
        Schedule(SyncTrigger.Timer);
        try
        {
            _timer?.Change(_period, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // The runtime closed meanwhile.
        }
    }

    /// <summary>Completes when the syncs scheduled so far have run.</summary>
    public Task Completion
    {
        get
        {
            lock (_lock)
                return _completion;
        }
    }

    /// <summary>Starts a sync in the background and returns at once; does nothing while the sync is off.</summary>
    public void Schedule(SyncTrigger trigger)
    {
        if (!_sync.Enabled)
            return;
        lock (_lock)
        {
            if (_waiting)
                return;
            _waiting = true;
            _completion = _completion.ContinueWith(_ => RunAsync(trigger), TaskScheduler.Default).Unwrap();
        }
    }

    /// <summary>Nothing may escape: nobody awaits this run except tests, and a failure must not disturb anything.</summary>
    private async Task RunAsync(SyncTrigger trigger)
    {
        try
        {
            using var hold = await _gate.WriteAsync(CancellationToken.None);
            lock (_lock)
                _waiting = false;
            await _sync.RunAsync(trigger, CancellationToken.None);
        }
        catch (ObjectDisposedException)
        {
            // The service is shutting down; the sync waits for the next start.
        }
        catch (Exception e)
        {
            _log.Record(LogLevel.Warning, LogMessages.SyncNotDone, MemorySync.TriggerName(trigger), e.Message);
        }
        finally
        {
            lock (_lock)
                _waiting = false;
        }
    }

    public void Dispose() => _timer?.Dispose();
}
