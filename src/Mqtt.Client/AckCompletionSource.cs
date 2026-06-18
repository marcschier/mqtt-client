// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace Mqtt.Client;

/// <summary>
/// Pooled <see cref="IValueTaskSource{TResult}"/> used to await broker acknowledgements
/// (PUBACK/PUBCOMP/SUBACK/UNSUBACK) without allocating a <see cref="TaskCompletionSource{T}"/> and
/// its backing <see cref="Task"/> per in-flight operation. Reuses instances via a lock-free pool.
/// Completion is guarded so exactly one of {ack, cancellation, fault} resolves the source; the
/// instance returns to the pool once (and only once) the awaiter observes the result.
/// </summary>
internal sealed class AckCompletionSource : IValueTaskSource<object?>
{
    private static readonly ConcurrentQueue<AckCompletionSource> Pool = new();

    // Mutable struct: must be a field and never copied.
    private ManualResetValueTaskSourceCore<object?> _core;
    private int _completed;
    private CancellationTokenRegistration _ctReg;

    private AckCompletionSource()
    {
        // Run continuations asynchronously so completing on the read loop thread never inlines a
        // caller's continuation onto (and thereby stalls) the read loop.
        _core.RunContinuationsAsynchronously = true;
    }

    /// <summary>Rents an instance from the pool (or allocates one) and arms optional cancellation.</summary>
    public static AckCompletionSource Rent(CancellationToken cancellationToken)
    {
        if (!Pool.TryDequeue(out var source))
        {
            source = new AckCompletionSource();
        }
        if (cancellationToken.CanBeCanceled)
        {
            source._ctReg = cancellationToken.Register(
                static s => ((AckCompletionSource)s!).TrySetException(
                    new OperationCanceledException()),
                source);
        }
        return source;
    }

    /// <summary>The <see cref="ValueTask{TResult}"/> the caller awaits for the ack (or fault).</summary>
    public ValueTask<object?> ValueTask => new(this, _core.Version);

    /// <summary>Completes with a result. Returns false if already completed.</summary>
    public bool TrySetResult(object? result)
    {
        if (Interlocked.CompareExchange(ref _completed, 1, 0) != 0)
        {
            return false;
        }
        _core.SetResult(result);
        return true;
    }

    /// <summary>Completes with an exception. Returns false if already completed.</summary>
    public bool TrySetException(Exception exception)
    {
        if (Interlocked.CompareExchange(ref _completed, 1, 0) != 0)
        {
            return false;
        }
        _core.SetException(exception);
        return true;
    }

    public object? GetResult(short token)
    {
        try
        {
            return _core.GetResult(token);
        }
        finally
        {
            // Dispose the registration first; if a cancellation callback is mid-flight this blocks
            // until it finishes, closing the window where a recycled instance could be mutated.
            _ctReg.Dispose();
            _ctReg = default;
            _core.Reset();
            Volatile.Write(ref _completed, 0);
            Pool.Enqueue(this);
        }
    }

    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

    public void OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags)
        => _core.OnCompleted(continuation, state, token, flags);
}
