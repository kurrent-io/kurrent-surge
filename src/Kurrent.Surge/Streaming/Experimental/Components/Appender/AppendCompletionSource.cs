using System.Collections.Concurrent;
using System.Threading.Tasks.Sources;

namespace Kurrent.Surge.Experimental;

/// <summary>
/// Pooled awaitable completion for <see cref="Appender.AppendAsync"/>. The instance is the
/// <see cref="IValueTaskSource{T}"/> the caller awaits, and <see cref="OnResult"/> is the
/// pre-bound callback the flush lane invokes — one rented object replaces the per-record
/// TaskCompletionSource, its Task, and the completion closures.
/// </summary>
sealed class AppendCompletionSource : IValueTaskSource<AppendResult> {
    // Steady state pools one instance per concurrently awaited append; burst overflow is
    // left to the GC instead of growing the pool without bound.
    const int MaxPooled = 1024;

    static readonly ConcurrentQueue<AppendCompletionSource> Pool = new();

    static int PooledCount;

    ManualResetValueTaskSourceCore<AppendResult> _core;
    CancellationTokenRegistration                _registration;

    int _completed;
    int _releases;

    AppendCompletionSource() {
        _core.RunContinuationsAsynchronously = true;
        OnResult = Complete;
    }

    /// <summary>
    /// The completion callback handed to the flush lane. Allocated once per pooled instance.
    /// </summary>
    public Action<AppendResult> OnResult { get; }

    public short Token => _core.Version;

    public static AppendCompletionSource Rent(CancellationToken ct) {
        if (Pool.TryDequeue(out var source))
            Interlocked.Decrement(ref PooledCount);
        else
            source = new();

        // Two releases guard recycling: the awaiter's GetResult and the lane's callback.
        // The instance must not return to the pool while either side can still touch it —
        // a canceled await completes early, but the lane callback for that record still fires.
        source._completed = 0;
        source._releases  = 2;

        if (ct.CanBeCanceled)
            source._registration = ct.Register(
                static (state, token) => ((AppendCompletionSource)state!).Cancel(token), source);

        return source;
    }

    void Complete(AppendResult result) {
        if (Interlocked.Exchange(ref _completed, 1) == 0) {
            if (result.Status is AppendResultStatus.Failed)
                _core.SetException(result.Error!);
            else
                _core.SetResult(result);
        }

        Release();
    }

    void Cancel(CancellationToken token) {
        // TaskCanceledException, not the base OperationCanceledException — awaiting a
        // canceled TaskCompletionSource.Task threw the derived type, and callers match on it.
        // Not a release owner: the lane callback for this record still fires and releases.
        if (Interlocked.Exchange(ref _completed, 1) == 0)
            _core.SetException(new TaskCanceledException(null, null, token));
    }

    void Release() {
        if (Interlocked.Decrement(ref _releases) != 0)
            return;

        _registration.Dispose();
        _registration = default;
        _core.Reset();

        if (Interlocked.Increment(ref PooledCount) <= MaxPooled)
            Pool.Enqueue(this);
        else
            Interlocked.Decrement(ref PooledCount);
    }

    #region ->> IValueTaskSource <<-

    public AppendResult GetResult(short token) {
        try {
            return _core.GetResult(token);
        }
        finally {
            Release();
        }
    }

    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
        _core.OnCompleted(continuation, state, token, flags);

    #endregion // IValueTaskSource
}
