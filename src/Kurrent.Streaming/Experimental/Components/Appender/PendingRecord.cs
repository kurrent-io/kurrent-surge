using Contracts = KurrentDB.Protocol.V2.Streams;

namespace Kurrent.Streaming.Experimental.Components.Appender;

// IVariant can't be used here — the source generator emits public members but the
// proto types (Contracts.AppendRecord) are internal. A sealed interface hierarchy
// gives the same exhaustive pattern matching without accessibility conflicts.

interface IPendingRecord;

sealed record SerializedRecord : IPendingRecord {
    public required Contracts.AppendRecord Serialized { get; init; }
    public required Action<AppendResult>   Callback   { get; init; }
}

sealed record FlushSignal : IPendingRecord {
    public required TaskCompletionSource Completion { get; init; }
}
