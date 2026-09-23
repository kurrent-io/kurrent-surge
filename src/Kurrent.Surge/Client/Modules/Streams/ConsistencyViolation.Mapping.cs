using System.Diagnostics;
using Kurrent.Surge.Client.Exceptions;

namespace Kurrent.Surge.Client.Streams;

// Maps a StreamStateViolation onto the typed stream exceptions, as a C# 14 extension member on
// the Core StreamStateViolation.
static class ConsistencyViolationMapping {
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
