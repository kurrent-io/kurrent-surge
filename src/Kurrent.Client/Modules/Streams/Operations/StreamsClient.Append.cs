using Kurrent.Client.Exceptions;

namespace Kurrent.Client.Streams;

public partial class StreamsClient {
    public async ValueTask<AppendRecordsSuccess> AppendRecords(
        StreamName                stream,
        ExpectedStreamState       expectedState,
        IEnumerable<AppendRecord> records,
        CancellationToken         cancellationToken = default
    ) {
        var builder = AppendRecordsRequest.ToStream(stream);

        // V2 protocol doesn't support 'Any' — omit the check entirely (no precondition)
        if (expectedState != ExpectedStreamState.Any)
            builder.ExpectingState(expectedState);

        foreach (var record in records)
            builder.WithRecord(record);

        try {
            return await AppendRecords(builder.Build(), cancellationToken);
        }
        catch (AppendConsistencyViolationException ex) when (ex.Violations is [{ Value: StreamStateViolation v }]) {
            // unwrap single-stream consistency violations into specific stream exceptions
            throw v.ActualState.Match<Exception>(
                condition => condition switch {
                    ActualStreamCondition.Tombstoned => new StreamTombstonedException(v.Stream),
                    ActualStreamCondition.Deleted    => new StreamDeletedException(v.Stream),
                    ActualStreamCondition.NoStream   => new StreamNotFoundException(v.Stream),
                    _                                => ex
                },
                _ => ex);
        }
    }

    public async ValueTask<AppendRecordsSuccess> AppendRecords(
        AppendRecordsRequest request,
        CancellationToken cancellationToken = default
    ) {
        try {
            var serviceRequest = await Mapper
                .ToContract(request, cancellationToken)
                .ConfigureAwait(false);

            var response = await ServiceClient
                .AppendRecordsAsync(serviceRequest, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Mapper.ToModel(response);
        }
        catch (Exception ex) {
            throw ex.ToException(
                StreamNotFoundException.From,
                StreamDeletedException.From,
                StreamTombstonedException.From,
                AppendConsistencyViolationException.From,
                MaxAppendRecordSizeExceededException.From,
                MaxAppendTransactionSizeExceededException.From);
        }
    }
}
