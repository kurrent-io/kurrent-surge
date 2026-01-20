namespace Kurrent.Processors.Locks;

/// <summary>
/// Represents a specialized task completion source that facilitates token-based
/// task completion and cancellation. This class provides mechanisms for asynchronous
/// task signalling, cancellation, and disposal, using an underlying <see cref="CancellationTokenSource"/>.
/// </summary>
public sealed class TokenCompletionSource : TaskCompletionSource<CancellationToken>, IDisposable {
    readonly CancellationTokenSource _cts;
    readonly CancellationToken       _token; // cached to avoid ObjectDisposedException

    public TokenCompletionSource() : base(TaskCreationOptions.RunContinuationsAsynchronously) =>
        _token = (_cts = new()).Token;

    public void Complete() => TrySetResult(_token);

    public void Cancel(Exception? exception = null) {
        CancellationException = exception;
        _cts.Cancel();
    }

    Exception? CancellationException { get; set; }

    void Dispose(bool disposing) {
        if (!disposing) return;

        if (CancellationException is not null)
            TrySetException(CancellationException);
        else {
            _cts.Cancel();
            TrySetResult(_token);
        }

        _cts.Dispose();
    }

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~TokenCompletionSource() => Dispose(false);
}
