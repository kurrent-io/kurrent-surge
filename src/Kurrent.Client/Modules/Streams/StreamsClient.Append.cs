using Kurrent.Client.Exceptions;
using Kurrent.Validation;

using Contracts = KurrentDB.Protocol.V2.Streams;

namespace Kurrent.Client.Streams;

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

        var ctx             = CreateMappingContext(cancellationToken);
        var request         = new Contracts.AppendRecordsRequest();
        var transactionSize = 0;

        await foreach (var record in records.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            EnsureValid(record);
            
            // Metadata.WithMany mutates the record's metadata, so there is no record copy.
            // This also mutates the caller's metadata — intentional.
            if (options?.Metadata is { Count: > 0 } meta)
                record.Metadata.WithMany(meta);

            var serialized = await record
                .ToContract(ctx)
                .ConfigureAwait(false);

            // The serialized protobuf size is the unit the server enforces, so we measure the
            // same thing here to fail fast on a transaction that could never be accepted.
            var recordSize = serialized.CalculateSize();
            if (recordSize > maxRecordSize)
                throw new MaxAppendRecordSizeExceededException(
                    string.Format("Record '{0}' size {1} bytes exceeds the maximum append record size of {2} bytes.", record.RecordId, recordSize, maxRecordSize)) {
                    RecordId         = record.RecordId,
                    RecordActualSize = recordSize,
                    RecordMaxSize    = maxRecordSize
                };

            transactionSize += recordSize;
            if (transactionSize > maxTransactionSize)
                throw new MaxAppendTransactionSizeExceededException(
                    string.Format("Append transaction size {0} bytes exceeds the maximum append transaction size of {1} bytes.", transactionSize, maxTransactionSize)) {
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
                AppendConsistencyViolationException.From,
                MaxAppendRecordSizeExceededException.From,
                MaxAppendTransactionSizeExceededException.From);
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
    /// Appends records from an async enumerable source with optional consistency checks and
    /// batch-level overrides. Single-pass: each record is validated, stamped, and serialized
    /// as it is yielded.
    /// </summary>
    public ValueTask<AppendRecordsResponse> AppendRecords(
        IAsyncEnumerable<AppendRecord> records,
        CancellationToken cancellationToken
    ) => AppendRecords(records, (AppendRecordsOptions?)null, cancellationToken);
    
    /// <summary>
    /// Appends records from an enumerable source with optional consistency checks and
    /// batch-level overrides. Single-pass: each record is validated, stamped, and serialized
    /// as it is iterated.
    /// </summary>
    public ValueTask<AppendRecordsResponse> AppendRecords(
        IEnumerable<AppendRecord> records,
        AppendRecordsOptions? options,
        CancellationToken cancellationToken
    ) => AppendRecords(records.ToAsyncEnumerable(), options, cancellationToken);
    
    /// <summary>
    /// Appends records from an enumerable source in a single pass — each record is validated,
    /// stamped with batch overrides, and serialized as it is iterated. The records are never
    /// materialized into an intermediate collection.
    /// </summary>
    public ValueTask<AppendRecordsResponse> AppendRecords(
        IEnumerable<AppendRecord> records,
        CancellationToken cancellationToken
    ) => AppendRecords(records, (AppendRecordsOptions?)null, cancellationToken);

    /// <summary>
    /// Appends records from an async enumerable source, configuring the batch options inline
    /// from a fresh <see cref="AppendRecordsOptions"/>.
    /// </summary>
    public ValueTask<AppendRecordsResponse> AppendRecords(
        IAsyncEnumerable<AppendRecord> records,
        Func<AppendRecordsOptions, AppendRecordsOptions> configure,
        CancellationToken cancellationToken
    ) => AppendRecords(records, configure(new()), cancellationToken);

    /// <summary>
    /// Appends records from an enumerable source, configuring the batch options inline
    /// from a fresh <see cref="AppendRecordsOptions"/>.
    /// </summary>
    public ValueTask<AppendRecordsResponse> AppendRecords(
        IEnumerable<AppendRecord> records,
        Func<AppendRecordsOptions, AppendRecordsOptions> configure,
        CancellationToken cancellationToken
    ) => AppendRecords(records.ToAsyncEnumerable(), configure(new()), cancellationToken);

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

    #endregion // AppendStream
}
