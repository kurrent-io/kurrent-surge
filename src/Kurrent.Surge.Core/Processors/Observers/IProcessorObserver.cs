using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Processors;

/// <summary>
/// Receives processor lifecycle notifications. All hooks have default no-op implementations —
/// implement only the events of interest.
/// </summary>
/// <remarks>
/// Hooks are invoked synchronously on the processing thread, in registration order. For any given
/// record or batch, the <c>Ready</c> hook always precedes the matching <c>Completed</c> hook with
/// no interleaving from other records — single-slot correlation across the pair is safe.
/// <para>
/// <c>Ready</c> events carry the live record instances the handler will receive; mutating
/// <see cref="Record.Metadata"/> there is the supported enrichment and propagation point.
/// </para>
/// <para>
/// An exception thrown by a hook is logged as a warning and never disturbs the pipeline or the
/// remaining observers. Observers that transform or replace records do not belong here — wrap the
/// processor in a decorator instead.
/// </para>
/// </remarks>
[PublicAPI]
public interface IProcessorObserver {
    /// <summary>
    /// Called when the drone transitions between lifecycle statuses.
    /// </summary>
    void OnStateChanged(in ProcessorStateChanged evt) { }

    /// <summary>
    /// Called when a record is bypassed without being dispatched to the handler.
    /// </summary>
    void OnRecordSkipped(in RecordSkipped evt) { }

    /// <summary>
    /// Called when a record is decoded and about to be dispatched to the handler.
    /// </summary>
    void OnRecordReady(in RecordReady evt) { }

    /// <summary>
    /// Called exactly once after record processing ends, regardless of outcome.
    /// </summary>
    void OnRecordCompleted(in RecordCompleted evt) { }

    /// <summary>
    /// Called when a batch is decoded and about to be dispatched to the handler.
    /// </summary>
    void OnBatchReady(in BatchReady evt) { }

    /// <summary>
    /// Called exactly once after batch processing ends, regardless of outcome.
    /// </summary>
    void OnBatchCompleted(in BatchCompleted evt) { }

    /// <summary>
    /// Called when the source-lag probe samples how far the drone trails the log head.
    /// </summary>
    void OnSourceLagSampled(in SourceLagSampled evt) { }

    /// <summary>
    /// Called when a checkpoint has been committed.
    /// </summary>
    void OnCheckpointCommitted(in CheckpointCommitted evt) { }

    /// <summary>
    /// Called when a checkpoint failed to commit.
    /// </summary>
    void OnCheckpointFailed(in CheckpointFailed evt) { }
}
