using Microsoft.Extensions.Logging;

namespace Kurrent.Surge.Streaming;

/// <summary>
/// Dispatches processor lifecycle events to registered observers: synchronously, on the calling
/// thread, in registration order, with per-observer failure isolation.
/// </summary>
sealed partial class ObserverDispatcher {
    public ObserverDispatcher(IEnumerable<IProcessorObserver> observers, ILogger logger) {
        // Materialized once — dispatch is a hot-path indexed loop with no enumerator allocation.
        _observers = observers as IProcessorObserver[] ?? observers.ToArray();
        _logger    = logger;
    }

    readonly IProcessorObserver[] _observers;
    readonly ILogger              _logger;

    public void StateChanged(in ProcessorStateChanged evt) {
        foreach (var observer in _observers) {
            try {
                observer.OnStateChanged(in evt);
            }
            catch (Exception ex) {
                LogObserverFailed(ex, observer.GetType().Name, nameof(ProcessorStateChanged));
            }
        }
    }

    public void RecordSkipped(in RecordSkipped evt) {
        foreach (var observer in _observers) {
            try {
                observer.OnRecordSkipped(in evt);
            }
            catch (Exception ex) {
                LogObserverFailed(ex, observer.GetType().Name, nameof(Streaming.RecordSkipped));
            }
        }
    }

    public void RecordReady(in RecordReady evt) {
        foreach (var observer in _observers) {
            try {
                observer.OnRecordReady(in evt);
            }
            catch (Exception ex) {
                LogObserverFailed(ex, observer.GetType().Name, nameof(Streaming.RecordReady));
            }
        }
    }

    public void RecordCompleted(in RecordCompleted evt) {
        foreach (var observer in _observers) {
            try {
                observer.OnRecordCompleted(in evt);
            }
            catch (Exception ex) {
                LogObserverFailed(ex, observer.GetType().Name, nameof(Streaming.RecordCompleted));
            }
        }
    }

    public void BatchReady(in BatchReady evt) {
        foreach (var observer in _observers) {
            try {
                observer.OnBatchReady(in evt);
            }
            catch (Exception ex) {
                LogObserverFailed(ex, observer.GetType().Name, nameof(Streaming.BatchReady));
            }
        }
    }

    public void BatchCompleted(in BatchCompleted evt) {
        foreach (var observer in _observers) {
            try {
                observer.OnBatchCompleted(in evt);
            }
            catch (Exception ex) {
                LogObserverFailed(ex, observer.GetType().Name, nameof(Streaming.BatchCompleted));
            }
        }
    }

    public void SourceLagSampled(in SourceLagSampled evt) {
        foreach (var observer in _observers) {
            try {
                observer.OnSourceLagSampled(in evt);
            }
            catch (Exception ex) {
                LogObserverFailed(ex, observer.GetType().Name, nameof(Streaming.SourceLagSampled));
            }
        }
    }

    public void CheckpointCommitted(in CheckpointCommitted evt) {
        foreach (var observer in _observers) {
            try {
                observer.OnCheckpointCommitted(in evt);
            }
            catch (Exception ex) {
                LogObserverFailed(ex, observer.GetType().Name, nameof(Streaming.CheckpointCommitted));
            }
        }
    }

    public void CheckpointFailed(in CheckpointFailed evt) {
        foreach (var observer in _observers) {
            try {
                observer.OnCheckpointFailed(in evt);
            }
            catch (Exception ex) {
                LogObserverFailed(ex, observer.GetType().Name, nameof(Streaming.CheckpointFailed));
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Observer} failed handling {ObserverEvent}")]
    partial void LogObserverFailed(Exception ex, string observer, string observerEvent);
}
