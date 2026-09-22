namespace Kurrent.Surge.Processors.Hosting;

/// <summary>
/// Holds the first failure a processor stopped with, for the host lifetime to rethrow.
/// </summary>
sealed class ProcessorFailureRecorder : IProcessorObserver {
    Exception? _failure;

    public Exception? Failure => Volatile.Read(ref _failure);

    public void OnStateChanged(in ProcessorStateChanged evt) {
        if (evt is { ToStatus: DroneStatus.Stopped, Error: { } error })
            Interlocked.CompareExchange(ref _failure, error, null);
    }
}
