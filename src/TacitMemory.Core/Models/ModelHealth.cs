namespace TacitMemory.Core.Models;

/// <summary>
/// Whether an auxiliary model answered last time. After a failure the model is not called again for
/// <see cref="Cooldown"/>, so a model that is down costs one slow failure instead of one per request.
/// </summary>
internal sealed class ModelHealth(TimeProvider time)
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);

    private readonly Lock _gate = new();
    private DateTimeOffset _retryAt = DateTimeOffset.MinValue;
    private bool? _available;

    /// <summary><c>null</c> until the model has been called once.</summary>
    public bool? Available
    {
        get
        {
            lock (_gate)
                return _available;
        }
    }

    /// <summary>Whether to call the model now; <c>false</c> while cooling down after a failure.</summary>
    public bool ShouldTry
    {
        get
        {
            lock (_gate)
                return _available != false || time.GetUtcNow() >= _retryAt;
        }
    }

    /// <summary>Raised when availability changes; the server instructions mention unavailable models.</summary>
    public event Action? Changed;

    public void Succeeded() => Set(true);

    public void Failed()
    {
        lock (_gate)
            _retryAt = time.GetUtcNow() + Cooldown;
        Set(false);
    }

    private void Set(bool available)
    {
        bool changed;
        lock (_gate)
        {
            changed = _available != available;
            _available = available;
        }

        if (changed)
            Changed?.Invoke();
    }
}
