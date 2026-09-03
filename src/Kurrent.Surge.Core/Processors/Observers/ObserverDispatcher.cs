using Microsoft.Extensions.Logging;

namespace Kurrent.Surge.Processors;

/// <summary>
/// Dispatches processor lifecycle events to registered observers: synchronously, on the calling
/// thread, in registration order, with per-observer failure isolation.
/// </summary>
sealed partial class ObserverDispatcher(IEnumerable<IProcessorObserver> observers, ILogger logger) {
    readonly IProcessorObserver[] _observers = observers as IProcessorObserver[] ?? observers.ToArray();

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
                LogObserverFailed(ex, observer.GetType().Name, nameof(Processors.RecordSkipped));
            }
        }
    }

    public void RecordReady(in RecordReady evt) {
        foreach (var observer in _observers) {
            try {
                observer.OnRecordReady(in evt);
            }
            catch (Exception ex) {
                LogObserverFailed(ex, observer.GetType().Name, nameof(Processors.RecordReady));
            }
        }
    }

    public void RecordCompleted(in RecordCompleted evt) {
        foreach (var observer in _observers) {
            try {
                observer.OnRecordCompleted(in evt);
            }
            catch (Exception ex) {
                LogObserverFailed(ex, observer.GetType().Name, nameof(Processors.RecordCompleted));
            }
        }
    }

    public void BatchReady(in BatchReady evt) {
        foreach (var observer in _observers) {
            try {
                observer.OnBatchReady(in evt);
            }
            catch (Exception ex) {
                LogObserverFailed(ex, observer.GetType().Name, nameof(Processors.BatchReady));
            }
        }
    }

    public void BatchCompleted(in BatchCompleted evt) {
        foreach (var observer in _observers) {
            try {
                observer.OnBatchCompleted(in evt);
            }
            catch (Exception ex) {
                LogObserverFailed(ex, observer.GetType().Name, nameof(Processors.BatchCompleted));
            }
        }
    }

    public void SourceLagSampled(in SourceLagSampled evt) {
        foreach (var observer in _observers) {
            try {
                observer.OnSourceLagSampled(in evt);
            }
            catch (Exception ex) {
                LogObserverFailed(ex, observer.GetType().Name, nameof(Processors.SourceLagSampled));
            }
        }
    }

    public void CheckpointCommitted(in CheckpointCommitted evt) {
        foreach (var observer in _observers) {
            try {
                observer.OnCheckpointCommitted(in evt);
            }
            catch (Exception ex) {
                LogObserverFailed(ex, observer.GetType().Name, nameof(Processors.CheckpointCommitted));
            }
        }
    }

    public void CheckpointFailed(in CheckpointFailed evt) {
        foreach (var observer in _observers) {
            try {
                observer.OnCheckpointFailed(in evt);
            }
            catch (Exception ex) {
                LogObserverFailed(ex, observer.GetType().Name, nameof(Processors.CheckpointFailed));
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Observer} failed handling {ObserverEvent}")]
    partial void LogObserverFailed(Exception ex, string observer, string observerEvent);
}
