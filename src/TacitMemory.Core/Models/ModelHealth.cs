namespace TacitMemory.Core.Models;

/// <summary>
/// Whether an auxiliary model is cooling down. A request that finally fails starts <see cref="Cooldown"/>; while it
/// runs the model is not called and is listed as unavailable. The first request after it is tried as usual, and one
/// success clears it. Failures inside a request that is still retrying never reach here.
/// </summary>
internal sealed class ModelHealth(TimeProvider time)
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

    public void Succeeded()
    {
        lock (_gate)
            _coolingUntil = null;
    }

    /// <summary>A request finally failed: cool down from now.</summary>
    public void Failed()
    {
        lock (_gate)
            _coolingUntil = time.GetUtcNow() + Cooldown;
    }

    /// <summary>The model can never be used, as a decision model without a key: it is never called, and stays cooling.</summary>
    public void Disable()
    {
        lock (_gate)
            _coolingUntil = DateTimeOffset.MaxValue;
    }
}
