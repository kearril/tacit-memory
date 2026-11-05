namespace TacitMemory.Core.Maintenance;

/// <summary>
/// The lock that orders the service's work on the memory repository and the index. Three kinds of work take it:
/// <list type="bullet">
/// <item><description>a read (a memory tool's read, the server instructions, a prompt) shares it with other reads and with writes;</description></item>
/// <item><description>a write or background task runs one at a time and shares it with reads;</description></item>
/// <item><description>a maintenance operation holds it alone.</description></item>
/// </list>
/// While a maintenance waits, no new read or write is let in, so a maintenance cannot be starved. Waits have no timeout;
/// a wait that is cancelled leaves the queue holding nothing.
/// </summary>
internal sealed class MaintenanceGate : IDisposable
{
    private readonly Lock _gate = new();
    private readonly List<Waiter> _reads = [];
    private readonly List<Waiter> _writes = [];
    private readonly List<Waiter> _maintenances = [];

    /// <summary>Reads and writes holding the gate.</summary>
    private int _shared;

    /// <summary>A write or background task holds the write slot.</summary>
    private bool _writing;

    /// <summary>A maintenance operation holds the gate alone.</summary>
    private bool _maintaining;

    private bool _disposed;

    /// <summary>Waits until no maintenance holds or waits for the gate, then holds it for a read.</summary>
    public Task<Hold> ReadAsync(CancellationToken cancellationToken) => AcquireAsync(Kind.Read, cancellationToken);

    /// <summary>Waits until no other write, background task or maintenance holds the gate, then holds it for a write.</summary>
    public Task<Hold> WriteAsync(CancellationToken cancellationToken) => AcquireAsync(Kind.Write, cancellationToken);

    /// <summary>Waits until every holder has let go, then holds the gate alone.</summary>
    public Task<Hold> MaintenanceAsync(CancellationToken cancellationToken) => AcquireAsync(Kind.Maintenance, cancellationToken);

    /// <summary>Fails every waiting caller and every later one. A holder that has the gate keeps it until it lets go.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var waiter in _reads.Concat(_writes).Concat(_maintenances))
                waiter.Source.TrySetException(new ObjectDisposedException(nameof(MaintenanceGate)));
            _reads.Clear();
            _writes.Clear();
            _maintenances.Clear();
        }
    }

    private async Task<Hold> AcquireAsync(Kind kind, CancellationToken cancellationToken)
    {
        var waiter = new Waiter(kind);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            QueueOf(kind).Add(waiter);
            Grant();
        }

        using var registration = cancellationToken.Register(() => Abandon(waiter, cancellationToken));
        return await waiter.Source.Task;
    }

    /// <summary>Takes a waiter out of its queue when its caller gave up. A waiter that was already granted keeps its hold.</summary>
    private void Abandon(Waiter waiter, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (QueueOf(waiter.Kind).Remove(waiter))
                Grant();
        }

        waiter.Source.TrySetCanceled(cancellationToken);
    }

    /// <summary>Grants the gate to every waiter that may now have it. Runs under the lock.</summary>
    private void Grant()
    {
        if (_disposed || _maintaining)
            return;

        if (_maintenances.Count > 0)
        {
            // A maintenance waiting keeps new reads and writes out; it starts when the holders already inside have let go.
            if (_shared > 0)
                return;
            _maintaining = true;
            Complete(_maintenances);
            return;
        }

        foreach (var read in _reads)
        {
            _shared++;
            read.Source.TrySetResult(new Hold(() => Release(Kind.Read)));
        }
        _reads.Clear();

        if (!_writing && _writes.Count > 0)
        {
            _writing = true;
            _shared++;
            Complete(_writes);
        }
    }

    /// <summary>Grants the first waiter of a queue, which holds the gate for its kind of work.</summary>
    private void Complete(List<Waiter> queue)
    {
        var first = queue[0];
        queue.RemoveAt(0);
        first.Source.TrySetResult(new Hold(() => Release(first.Kind)));
    }

    private void Release(Kind kind)
    {
        lock (_gate)
        {
            switch (kind)
            {
                case Kind.Read:
                    _shared--;
                    break;
                case Kind.Write:
                    _writing = false;
                    _shared--;
                    break;
                case Kind.Maintenance:
                    _maintaining = false;
                    break;
            }

            Grant();
        }
    }

    private List<Waiter> QueueOf(Kind kind) => kind switch
    {
        Kind.Read => _reads,
        Kind.Write => _writes,
        _ => _maintenances,
    };

    private enum Kind
    {
        Read,
        Write,
        Maintenance,
    }

    /// <summary>One caller waiting for the gate. Compared by identity, so a queue removes exactly the waiter that gave up.</summary>
    private sealed class Waiter(Kind kind)
    {
        public Kind Kind { get; } = kind;

        public TaskCompletionSource<Hold> Source { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

/// <summary>What a caller holds while it has the gate. Disposing it lets the gate go; disposing it twice does nothing more.</summary>
internal sealed class Hold(Action release) : IDisposable
{
    private Action? _release = release;

    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
