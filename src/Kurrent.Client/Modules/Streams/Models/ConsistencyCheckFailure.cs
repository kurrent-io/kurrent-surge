using Kurrent.Client.Exceptions;
using Kurrent.Variant;

namespace Kurrent.Client.Streams;

/// <summary>
/// Represents a failure that occurred during a consistency check on a stream operation.
/// </summary>
public readonly partial record struct ConsistencyCheckFailure : IVariant<StreamConsistencyCheckFailure, QueryConsistencyCheckFailure> {
    public static readonly ConsistencyCheckFailure None = new();
}

/// <summary>
/// Represents a failure that occurred during a consistency check using expected and actual stream states on a stream operation.
/// </summary>
public readonly record struct StreamConsistencyCheckFailure() {
    public StreamConsistencyCheckFailure(StreamName stream, ExpectedStreamState expectedState, ActualStreamState actualState) : this() {
        Stream        = stream;
        ExpectedState = expectedState;
        ActualState   = actualState;
    }

    public StreamName          Stream        { get; init; } = StreamName.None;
    public ExpectedStreamState ExpectedState { get; init; } = ExpectedStreamState.NoStream;
    public ActualStreamState   ActualState   { get; init; } = ActualStreamState.NoStream;

    /// <summary>
    /// Converts this consistency check failure into the appropriate exception type based on the expected and actual stream states.
    /// </summary>
    internal T ToException<T>() {
        dynamic ex = ActualState switch {
            _ when ActualState == ActualStreamState.NoStream   => new StreamNotFoundException(Stream),
            _ when ActualState == ActualStreamState.Deleted    => new StreamDeletedException(Stream),
            _ when ActualState == ActualStreamState.Tombstoned => new StreamTombstonedException(Stream),

            _ when ExpectedState == ExpectedStreamState.NoStream
                && ActualState.IsRevision => new StreamAlreadyExistsException(Stream, ActualState.AsRevision),

            _ => new StreamRevisionConflictException(Stream, ExpectedState, ActualState.AsRevision)
        };

        return (T) ex;
    }
}

/// <summary>
/// Represents a failure that occurred during a consistency check using a custom query expression on a stream operation.
/// </summary>
public readonly record struct QueryConsistencyCheckFailure() {
    public string Message { get; init; } = "";
}
