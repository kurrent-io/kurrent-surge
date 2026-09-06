using Contracts = KurrentDB.Protocol.V2.Streams;

namespace Kurrent.Surge.Experimental;

/// <summary>
/// One channel element for both message kinds — a record to flush or a flush signal.
/// A struct so the append hot path enqueues without a per-record heap allocation.
/// <see cref="FlushCompletion"/> discriminates: set means flush signal, null means record.
/// </summary>
readonly struct PendingRecord {
    // Exactly one of Callback/AsyncCallback is set, matching the Append overload used.
    // Kept as two fields so the hot Action path pays no wrapper allocation.
    public Contracts.AppendRecord?        Serialized      { get; private init; }
    public int                            SizeBytes       { get; private init; }
    public Action<AppendResult>?          Callback        { get; private init; }
    public Func<AppendResult, ValueTask>? AsyncCallback   { get; private init; }
    public TaskCompletionSource?          FlushCompletion { get; private init; }

    // SizeBytes arrives precomputed from the caller's thread — the single-reader flush
    // loop must never walk the protobuf message for sizing.
    public static PendingRecord ForRecord(
        Contracts.AppendRecord serialized, int sizeBytes,
        Action<AppendResult>? callback, Func<AppendResult, ValueTask>? asyncCallback
    ) => new() {
        Serialized    = serialized,
        SizeBytes     = sizeBytes,
        Callback      = callback,
        AsyncCallback = asyncCallback
    };

    public static PendingRecord ForFlush(TaskCompletionSource completion) =>
        new() { FlushCompletion = completion };
}
