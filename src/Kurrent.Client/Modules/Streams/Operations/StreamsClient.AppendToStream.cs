using System.Collections.Immutable;
using Kurrent.Client.Exceptions;

namespace Kurrent.Client.Streams;

public partial class StreamsClient {
    public async ValueTask<AppendToStreamResponse> AppendToStream(
        AppendToStreamRequest request,
        CancellationToken cancellationToken = default
    ) {
        try {
            // Validate all records and stamp target stream
            request = request.EnsureValid() with {
                Checks = [
                    ..request.Checks
                        .Where(c => c.IsStreamStateCheck)
                        .Where(c => c.Value is StreamStateCheck check && check.ExpectedState != ExpectedStreamState.Any)
                ]
            };

            var serviceRequest = await Mapper
                .ToContract(request, cancellationToken)
                .ConfigureAwait(false);

            var rsp = await ServiceClient
                .AppendRecordsAsync(serviceRequest, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return new AppendToStreamResponse(request.Stream, rsp.Revisions[0].Revision, rsp.Position);
        }
        catch (Exception ex) {
            var mex = ex.ToException(
                AppendConsistencyViolationException.From,
                MaxAppendRecordSizeExceededException.From,
                MaxAppendTransactionSizeExceededException.From);

            if (mex is not AppendConsistencyViolationException { Violations: [{ Value: StreamStateViolation violation }] })
                throw mex;

            throw violation.ActualState.Match<Exception>(
                condition => condition switch {
                    ActualStreamCondition.Tombstoned => new StreamTombstonedException(violation.Stream),
                    ActualStreamCondition.NoStream   => new StreamNotFoundException(violation.Stream),
                    _                                => ex
                },
                revision => {
                    var targetStreamCheck = request.Checks
                        .Where(c => c is { IsStreamStateCheck: true, Value: StreamStateCheck check } && check.Stream == request.Stream)
                        .Select(c => c.AsStreamStateCheck)
                        .SingleOrDefault();

                    return targetStreamCheck.ExpectedState == ExpectedStreamState.NoStream
                        ? new StreamAlreadyExistsException(violation.Stream)
                        : new StreamRevisionConflictException(violation.Stream, violation.ExpectedState, revision);
                });
        }
    }

    public ValueTask<AppendToStreamResponse> AppendToStream(
        StreamName stream,
        ExpectedStreamState expectedState,
        IEnumerable<AppendRecord> records,
        CancellationToken cancellationToken = default
    ) =>
        AppendToStream(
            AppendToStreamRequest.For(stream)
                .ExpectingState(expectedState)
                .WithRecords(records)
                .Build(),
            cancellationToken);

    public ValueTask<AppendToStreamResponse> AppendToStream(
        StreamName stream,
        IEnumerable<AppendRecord> records,
        CancellationToken cancellationToken = default
    ) =>
        AppendToStream(
            AppendToStreamRequest.For(stream)
                .WithRecords(records)
                .Build(),
            cancellationToken
        );
}
