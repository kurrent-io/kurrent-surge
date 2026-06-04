using System.Diagnostics;
using System.Runtime.InteropServices;
using Kurrent.Client.Exceptions;
using Kurrent.Variant;

namespace Kurrent.Client.Streams;

/// <summary>
/// A consistency check that was violated during an append operation.
/// Can be either a <see cref="StreamStateViolation"/> (stream was not at the expected state)
/// or a <see cref="QueryPredicateViolation"/> (a query predicate evaluated to false).
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly partial record struct ConsistencyViolation : IVariant<StreamStateViolation, QueryPredicateViolation> {
    public static readonly ConsistencyViolation None;

    /// <summary>
    /// Index of the check in the original consistency_checks list.
    /// </summary>
    public int CheckIndex { get; init; }
}

/// <summary>
/// A stream state check was violated: the stream's actual state did not match the expected state.
/// The actual state may be a specific revision, or a lifecycle state (NoStream, Deleted, Tombstoned).
/// </summary>
public readonly record struct StreamStateViolation() {
    public static readonly StreamStateViolation None = new(StreamName.Unset, ExpectedStreamState.NoStream, ActualStreamState.NoStream);

    public StreamStateViolation(StreamName stream, ExpectedStreamState expectedState, ActualStreamState actualState) : this() {
        Stream        = stream;
        ExpectedState = expectedState;
        ActualState   = actualState;
    }

    public StreamName          Stream        { get; init; } = StreamName.Unset;
    public ExpectedStreamState ExpectedState { get; init; } = ExpectedStreamState.NoStream;
    public ActualStreamState   ActualState   { get; init; } = ActualStreamState.NoStream;

    /// <summary>
    /// Converts this stream state violation into the appropriate exception type based on the expected and actual states.
    /// </summary>
    [StackTraceHidden]
    internal Exception ToStreamException() {
        dynamic ex = ActualState switch {
            _ when ActualState == ActualStreamState.NoStream   => new StreamNotFoundException(Stream),
            _ when ActualState == ActualStreamState.Deleted    => new StreamDeletedException(Stream),
            _ when ActualState == ActualStreamState.Tombstoned => new StreamTombstonedException(Stream),

            _ when ActualState.IsRevision && ExpectedState == ExpectedStreamState.NoStream =>
                new StreamAlreadyExistsException(Stream, ActualState.AsRevision),

            _ => new StreamRevisionConflictException(Stream, ExpectedState, ActualState.AsRevision)
        };

        return ex;
    }
}

/// <summary>
/// A query predicate was violated: the server-side expression evaluated to false.
/// </summary>
public readonly record struct QueryPredicateViolation() {
    /// <summary>
    /// An optional message describing why the predicate was not satisfied.
    /// Intended for debugging; clients should not rely on its content or format.
    /// </summary>
    public string Message { get; init; } = "";
}
