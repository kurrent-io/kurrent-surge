using Kurrent.Variant;

namespace Kurrent.Client.Streams;

public  readonly partial record struct ConsistencyCheckError : IVariant<RevisionConsistencyCheckError, GenericConsistencyCheckError> {
    public static readonly ConsistencyCheckError None = new GenericConsistencyCheckError();
}

public readonly record struct RevisionConsistencyCheckError() {
    public StreamName     Stream   { get; init; } = StreamName.None;
    public StreamRevision Revision { get; init; } = StreamRevision.Unset;
}

public readonly record struct GenericConsistencyCheckError() {
    public string Message { get; init; } = "";
}
