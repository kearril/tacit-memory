using Microsoft.Extensions.Logging;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Logging;

namespace TacitMemory.Core.Models;

/// <summary>
/// Whether an auxiliary model is cooling down. A request that finally fails starts <see cref="Cooldown"/>; while it
/// runs the model is not called and is listed as unavailable. The first request after it is tried as usual, and one
/// success clears it. Failures inside a request that is still retrying never reach here.
/// </summary>
internal sealed class ModelHealth(TimeProvider time, string role, ServiceLog log)
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);

    private readonly Lock _gate = new();
    private DateTimeOffset? _coolingUntil;

    /// <summary>Whether the model is cooling down now: not called, and named in the status.</summary>
    public bool CoolingDown
    {
        get
        {
            lock (_gate)
                return _coolingUntil is { } until && time.GetUtcNow() < until;
        }
    }

    /// <summary>How long the cooldown has left; <c>null</c> when the model is not cooling down.</summary>
    public TimeSpan? CooldownRemaining
    {
        get
        {
            lock (_gate)
            {
                if (_coolingUntil is not { } until)
                    return null;
                var left = until - time.GetUtcNow();
                return left > TimeSpan.Zero ? left : null;
            }
        }
    }

    public void Succeeded()
    {
        lock (_gate)
            _coolingUntil = null;
    }

    /// <summary>A request finally failed: cool down from now, and log it.</summary>
    public void Failed()
    {
        lock (_gate)
            _coolingUntil = time.GetUtcNow() + Cooldown;
        log.Record(LogLevel.Warning, LogMessages.ModelFailed, role, (int)Cooldown.TotalSeconds);
    }
}
