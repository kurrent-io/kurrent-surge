using Kurrent.Client.Exceptions;

namespace Kurrent.Client.Streams;

public partial class StreamsClient {
    /// <summary>
    /// Appends records to a single stream, guarding on <paramref name="expectedState"/>.
    /// Every record is forced onto <paramref name="stream"/> regardless of its own
    /// <see cref="AppendRecord.Stream"/>. Single-pass — delegates to
    /// <see cref="AppendRecords(IAsyncEnumerable{AppendRecord}, AppendRecordsOptions?, CancellationToken)"/>.
    /// </summary>
    public async ValueTask<AppendStreamResponse> AppendStream(
        StreamName stream,
        IAsyncEnumerable<AppendRecord> records,
        ExpectedStreamState expectedState,
        CancellationToken cancellationToken
    ) {
        // Force every record onto the target stream; only copy when it isn't already there.
        var stamped = records.Select(record => record.Stream == stream ? record : record with { Stream = stream });

        // An expected state is just a single-stream consistency check on the transaction.
        var options = expectedState != ExpectedStreamState.Any
            ? new AppendRecordsOptions { Checks = [new StreamStateCheck(stream, expectedState)] }
            : null;

        try {
            var rsp      = await AppendRecords(stamped, options, cancellationToken).ConfigureAwait(false);
            var revision = rsp.Revisions[0];

            return new AppendStreamResponse(revision.Stream, revision.Revision, rsp.Position);
        }
        catch (AppendConsistencyViolationException vex) {
            // A single-stream append yields at most one violation; surface it as the typed stream exception.
            throw vex.Violations[0].AsStreamStateViolation.ToStreamException();
        }
    }

    /// <inheritdoc cref="AppendStream(StreamName, IAsyncEnumerable{AppendRecord}, ExpectedStreamState, CancellationToken)"/>
    public ValueTask<AppendStreamResponse> AppendStream(
        StreamName stream,
        IAsyncEnumerable<AppendRecord> records,
        CancellationToken cancellationToken
    ) => AppendStream(stream, records, ExpectedStreamState.Any, cancellationToken);

    /// <inheritdoc cref="AppendStream(StreamName, IAsyncEnumerable{AppendRecord}, ExpectedStreamState, CancellationToken)"/>
    public ValueTask<AppendStreamResponse> AppendStream(
        StreamName stream,
        IEnumerable<AppendRecord> records,
        ExpectedStreamState expectedState,
        CancellationToken cancellationToken = default
    ) => AppendStream(stream, records.ToAsyncEnumerable(), expectedState, cancellationToken);

    /// <inheritdoc cref="AppendStream(StreamName, IAsyncEnumerable{AppendRecord}, ExpectedStreamState, CancellationToken)"/>
    public ValueTask<AppendStreamResponse> AppendStream(
        StreamName stream,
        IEnumerable<AppendRecord> records,
        CancellationToken cancellationToken = default
    ) => AppendStream(stream, records.ToAsyncEnumerable(), ExpectedStreamState.Any, cancellationToken);

    /// <summary>
    /// Appends a single record to its own <see cref="AppendRecord.Stream"/>, guarding on
    /// <paramref name="expectedState"/>.
    /// </summary>
    public ValueTask<AppendStreamResponse> AppendStream(
        AppendRecord record,
        ExpectedStreamState expectedState,
        CancellationToken cancellationToken = default
    ) => AppendStream(record.Stream, new[] { record }, expectedState, cancellationToken);

    /// <inheritdoc cref="AppendStream(AppendRecord, ExpectedStreamState, CancellationToken)"/>
    public ValueTask<AppendStreamResponse> AppendStream(
        AppendRecord record,
        CancellationToken cancellationToken = default
    ) => AppendStream(record.Stream, new[] { record }, ExpectedStreamState.Any, cancellationToken);
}
