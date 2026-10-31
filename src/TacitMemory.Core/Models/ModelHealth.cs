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

    public void Succeeded()
    {
        lock (_gate)
            _available = true;
    }

    public void Failed()
    {
        lock (_gate)
        {
            _retryAt = time.GetUtcNow() + Cooldown;
            _available = false;
        }
    }
}
