using System.Diagnostics;
using Kurrent.Surge.Client.Exceptions;

namespace Kurrent.Surge.Client.Streams;

// Maps a StreamStateViolation onto the typed stream exceptions. Those exceptions are gRPC-coupled
// (they parse Google.Rpc.Status) and stay in the client, so the mapper lives here as a C# 14
// extension member on the protobuf-free Abstractions StreamStateViolation.
static class StreamStateViolationMappers {
    extension(StreamStateViolation violation) {
        [StackTraceHidden]
        internal Exception ToStreamException() {
            dynamic ex = violation.ActualState switch {
                _ when violation.ActualState == ActualStreamState.NoStream   => new StreamNotFoundException(violation.Stream),
                _ when violation.ActualState == ActualStreamState.Deleted    => new StreamDeletedException(violation.Stream),
                _ when violation.ActualState == ActualStreamState.Tombstoned => new StreamTombstonedException(violation.Stream),

                _ when violation.ActualState.IsRevision && violation.ExpectedState == ExpectedStreamState.NoStream =>
                    new StreamAlreadyExistsException(violation.Stream, violation.ActualState.AsRevision),

                _ => new StreamRevisionConflictException(violation.Stream, violation.ExpectedState, violation.ActualState.AsRevision)
            };

            return ex;
        }
    }
}
