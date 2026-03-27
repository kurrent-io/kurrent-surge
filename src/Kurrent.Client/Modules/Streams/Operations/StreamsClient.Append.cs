using System.Collections.Immutable;
using Kurrent.Client.Exceptions;

namespace Kurrent.Client.Streams;

public partial class StreamsClient {
    public async ValueTask<AppendRecordsSuccess> AppendRecords(
        AppendRecordsRequest request,
        CancellationToken cancellationToken = default
    ) {
        try {
            // remove ExpectedStreamState.Any checks
            request = request with {
                Checks = [
                    ..request.Checks
                        .Where(c => c.IsQueryPredicateCheck)
                        .Where(c => c.Value is StreamStateCheck check && check.ExpectedState != ExpectedStreamState.Any)
                ]
            };

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
                // StreamNotFoundException.From,
                // StreamDeletedException.From,
                // StreamTombstonedException.From,
                AppendConsistencyViolationException.From,
                MaxAppendRecordSizeExceededException.From,
                MaxAppendTransactionSizeExceededException.From);
        }
    }

    // we need to rethink the experience. maybe have 2 different request types.
    // one for single-stream, one for multi-stream.

    public async ValueTask<AppendRecordsSuccess> AppendRecords(
        StreamName                stream,
        ExpectedStreamState       expectedState,
        IEnumerable<AppendRecord> records,
        CancellationToken         cancellationToken = default
    ) {
        var builder = AppendRecordsRequest.New();

        if (expectedState != ExpectedStreamState.Any)
            builder.WithCheck(new StreamStateCheck(stream, expectedState));

        foreach (var record in records)
            builder.WithRecord(record with { Stream = stream });

        var request = builder.Build();

        try {
            return await AppendRecords(request, cancellationToken);
        }
        catch (AppendConsistencyViolationException ex)when (ex.Violations is [{ Value: StreamStateViolation v }]) {
            // unwrap single-stream consistency violations into specific stream exceptions
            throw v.ActualState.Match<Exception>(
                condition => condition switch {
                    ActualStreamCondition.Tombstoned => new StreamTombstonedException(v.Stream),
                    // ActualStreamCondition.Deleted    => new StreamDeletedException(v.Stream),
                    ActualStreamCondition.NoStream => new StreamNotFoundException(v.Stream),
                    _                              => ex
                },
                _ => ex);
        }
    }

}
