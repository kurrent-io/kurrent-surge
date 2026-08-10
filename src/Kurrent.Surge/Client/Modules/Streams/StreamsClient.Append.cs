using Kurrent.Surge.Client.Exceptions;
using Kurrent.Validation;

using Contracts = KurrentDB.Protocol.V2.Streams;

namespace Kurrent.Surge.Client.Streams;

public partial class StreamsClient {
    #region ->> AppendRecords <<-

    /// <summary>
    /// Appends records from an async enumerable source with optional consistency checks and a
    /// batch-level metadata overlay. Single-pass: each record is validated, serialized, and
    /// size-checked as it is yielded — nothing is materialized into an intermediate collection.
    /// </summary>
    public async ValueTask<AppendRecordsResponse> AppendRecords(
        IAsyncEnumerable<AppendRecord> records,
        AppendRecordsOptions? options,
        CancellationToken cancellationToken
    ) {
        var maxRecordSize      = options?.MaxRecordSize      ?? AppendRecordsOptions.DefaultMaxRecordSize;
        var maxTransactionSize = options?.MaxTransactionSize ?? AppendRecordsOptions.DefaultMaxTransactionSize;

        var request         = new Contracts.AppendRecordsRequest();
        var transactionSize = 0;

        await foreach (var record in records.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            EnsureValid(record);

            // Metadata.WithMany mutates the record's metadata, so there is no record copy.
            // This also mutates the caller's metadata — intentional.
            if (options?.Metadata is { Count: > 0 } meta)
                record.Metadata.WithMany(meta);

            var serialized = await WritePipeline
                .Execute(record, cancellationToken)
                .ConfigureAwait(false);

            // The serialized protobuf size is the unit the server enforces, so we measure the
            // same thing here to fail fast on a transaction that could never be accepted.
            var recordSize = serialized.CalculateSize();
            if (recordSize > maxRecordSize)
                throw new MaxAppendRecordSizeExceededException(
                    $"Record '{record.RecordId}' size {recordSize} bytes exceeds the maximum append record size of {maxRecordSize} bytes.") {
                    RecordId         = record.RecordId.ToGuid(),
                    RecordActualSize = recordSize,
                    RecordMaxSize    = maxRecordSize
                };

            transactionSize += recordSize;
            if (transactionSize > maxTransactionSize)
                throw new MaxAppendTransactionSizeExceededException(
                    $"Append transaction size {transactionSize} bytes exceeds the maximum append transaction size of {maxTransactionSize} bytes.") {
                    TransactionActualSize = transactionSize,
                    TransactionMaxSize    = maxTransactionSize
                };

            request.Records.Add(serialized);
        }

        if (request.Records.Count == 0)
            throw new ValidationException(
                ErrorCode.From(nameof(AppendRecords)),
                "At least one record is required.",
                ValidationProblem.Create(ErrorCode.From("AppendRecords"), "At least one record is required."));

        AddChecks(request, options);

        try {
            var rsp = await ServiceClient
                .AppendRecordsAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return rsp.ToResponse();
        }
        catch (Exception ex) {
            throw ex.AsWellKnownException(
                AppendConsistencyViolationException.Match,
                MaxAppendRecordSizeExceededException.Match,
                MaxAppendTransactionSizeExceededException.Match);
        }

        static void EnsureValid(AppendRecord record) {
            if (record.Stream == StreamName.Unset)
                throw new ValidationException(
                    ErrorCode.FromType<AppendRecord>(),
                    "A record must have a destination stream.",
                    ValidationProblem.Create(
                        ErrorCode.FromType<AppendRecord>(),
                        "A record must have a destination stream."));

            if (record.Stream.IsAllStream)
                throw new ValidationException(
                    ErrorCode.FromType<AppendRecord>(),
                    "A record cannot be appended to the '$all' stream.",
                    ValidationProblem.Create(
                        ErrorCode.FromType<AppendRecord>(),
                        "A record cannot be appended to the '$all' stream."));

            record.EnsureValid();
        }

        static void AddChecks(Contracts.AppendRecordsRequest request, AppendRecordsOptions? options) {
            if (options?.Checks is not { Count: > 0 } checks)
                return;

            foreach (var check in checks.Where(c => c != ConsistencyCheck.None))
                request.Checks.Add(check.ToContract());
        }
    }

    /// <summary>
    /// Appends records from an async enumerable source, configuring the batch options inline
    /// from a fresh <see cref="AppendRecordsOptions"/>.
    /// </summary>
    public ValueTask<AppendRecordsResponse> AppendRecords(
        IAsyncEnumerable<AppendRecord> records,
        Func<AppendRecordsOptions, AppendRecordsOptions> configure,
        CancellationToken cancellationToken
    ) => AppendRecords(records, configure(new()), cancellationToken);

    #endregion // AppendRecords

    #region ->> AppendStream <<-

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

            return new AppendStreamResponse(RecordPosition.From(rsp.Position, revision.Stream, revision.Revision));
        }
        catch (AppendConsistencyViolationException vex) {
            // A single-stream append yields at most one violation; surface it as the typed stream exception.
            throw vex.Violations[0].AsStreamStateViolation.ToStreamException();
        }
    }

    #endregion // AppendStream
}
