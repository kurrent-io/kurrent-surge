using System.Collections.Immutable;
using Kurrent.Client.Exceptions;

namespace Kurrent.Client.Streams;

public partial class StreamsClient {
    // public async ValueTask<AppendToStreamResponse> AppendStream(
    //     AppendStreamRequest request,
    //     CancellationToken cancellationToken = default
    // ) {
    //     try {
    //         // Validate all records and stamp target stream
    //         request = request.EnsureValid() with {
    //             Checks = [
    //                 ..request.Checks
    //                     .Where(c => c != ConsistencyCheck.None)
    //                     .Where(c => c.IsStreamStateCheck)
    //                     .Where(c => c.Value is StreamStateCheck check && check.ExpectedState != ExpectedStreamState.Any)
    //             ],
    //             Records = request.Records.Select(r => r with{ Stream = request.Stream }).ToImmutableArray()
    //         };
    //
    //         var kebas = new AppendRecordsRequest {
    //             Records = request.Records,
    //             Checks  = request.Checks
    //         };
    //
    //         var serviceRequest = await Mapper
    //             .ToContract(request, cancellationToken)
    //             .ConfigureAwait(false);
    //
    //         var rsp = await ServiceClient
    //             .AppendRecordsAsync(serviceRequest, cancellationToken: cancellationToken)
    //             .ConfigureAwait(false);
    //
    //         return new AppendToStreamResponse(request.Stream, rsp.Revisions[0].Revision, rsp.Position);
    //     }
    //     catch (Exception ex) {
    //         try {
    //             var wtf = ex.AsWellKnownException(
    //                 AppendConsistencyViolationException.From,
    //                 MaxAppendRecordSizeExceededException.From,
    //                 MaxAppendTransactionSizeExceededException.From
    //             ); // lol it throws now ffs.
    //         } catch (Exception e) {
    //             Console.WriteLine(e);
    //
    //             throw;
    //         }
    //
    //         var mex = ex.AsWellKnownException(
    //             AppendConsistencyViolationException.From,
    //             MaxAppendRecordSizeExceededException.From,
    //             MaxAppendTransactionSizeExceededException.From);
    //
    //         if (mex is not AppendConsistencyViolationException { Violations: [{ Value: StreamStateViolation violation }] })
    //             throw mex;
    //
    //         throw violation.ActualState.Match<Exception>(
    //             condition => condition switch {
    //                 ActualStreamCondition.Tombstoned => new StreamTombstonedException(violation.Stream),
    //                 ActualStreamCondition.NoStream   => new StreamNotFoundException(violation.Stream),
    //                 _                                => ex
    //             },
    //             revision => {
    //                 var targetStreamCheck = request.Checks
    //                     .Where(c => c is { IsStreamStateCheck: true, Value: StreamStateCheck check } && check.Stream == request.Stream)
    //                     .Select(c => c.AsStreamStateCheck)
    //                     .SingleOrDefault();
    //
    //                 return targetStreamCheck.ExpectedState == ExpectedStreamState.NoStream
    //                     ? new StreamAlreadyExistsException(violation.Stream)
    //                     : new StreamRevisionConflictException(violation.Stream, violation.ExpectedState, revision);
    //             });
    //     }
    // }

    public async ValueTask<AppendStreamResponse> AppendStream(
        AppendStreamRequest request, CancellationToken cancellationToken = default
    ) {
        try {
            request = request.EnsureValid();

            AppendRecordsRequest req = request.ExpectedState != ExpectedStreamState.Any
                ? new() {
                    Records = request.Records,
                    Checks = [new StreamStateCheck(request.Stream, request.ExpectedState)]
                }
                : new() { Records = request.Records, Checks = [] };

            var rsp = await AppendRecords(req, cancellationToken).ConfigureAwait(false);

            return new AppendStreamResponse(rsp.Revisions[0].Stream, rsp.Revisions[0].Revision, rsp.Position);
        }
        catch (AppendConsistencyViolationException vex) {
            // I need to decide if im going to support checks in the append stream.
            // If not then I can just handle a single violation and throw the exception.
            // If I do, not only I need to handle multiple violations, but i need to throw the exception
            // that corresponds to the check that failed, which means I need to correlate the violation to the check,
            // which means I need to inspect all checks and find the one that matches the stream in the violation,
            // which is a bit of work but doable.


            // check target stream violation and throw the exception that corresponds to it.
            var violation = vex.Violations[0].AsStreamStateViolation;
            throw violation.ToStreamException();
        }
    }

    public ValueTask<AppendStreamResponse> AppendStream(
        StreamName stream,
        ExpectedStreamState expectedState,
        IEnumerable<AppendRecord> records,
        CancellationToken cancellationToken = default
    ) =>
        AppendStream(
            AppendStreamRequest.For(stream)
                .ExpectingState(expectedState)
                .WithRecords(records)
                .Build(),
            cancellationToken);

    public ValueTask<AppendStreamResponse> AppendStream(
        StreamName stream,
        IEnumerable<AppendRecord> records,
        CancellationToken cancellationToken = default
    ) =>
        AppendStream(
            AppendStreamRequest.For(stream)
                .WithRecords(records)
                .Build(),
            cancellationToken
        );
}
