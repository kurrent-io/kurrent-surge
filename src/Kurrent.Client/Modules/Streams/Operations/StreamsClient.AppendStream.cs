using Kurrent.Client.Exceptions;

namespace Kurrent.Client.Streams;

public partial class StreamsClient {
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

            var response = new AppendStreamResponse(rsp.Revisions[0].Stream, rsp.Revisions[0].Revision, rsp.Position);

            return response;
        }
        catch (AppendConsistencyViolationException vex) {
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

    public ValueTask<AppendStreamResponse> AppendStream(
        AppendRecord record,
        ExpectedStreamState expectedState,
        CancellationToken cancellationToken = default
    ) =>
        AppendStream(
            AppendStreamRequest.For(record.Stream)
                .ExpectingState(expectedState)
                .WithRecord(record)
                .Build(),
            cancellationToken
        );

    public ValueTask<AppendStreamResponse> AppendStream(
        AppendRecord record,
        CancellationToken cancellationToken = default
    ) =>
        AppendStream(
            AppendStreamRequest.For(record.Stream)
                .WithRecord(record)
                .Build(),
            cancellationToken
        );
}
