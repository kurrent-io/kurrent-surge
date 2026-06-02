using Kurrent.Client.Exceptions;
using Kurrent.Validation;
using Resources;

using Contracts = KurrentDB.Protocol.V2.Streams;

namespace Kurrent.Client.Streams;

public partial class StreamsClient {
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
                    string.Format(ErrorMessages.AppendRecordSizeExceeded, record.RecordId, recordSize, maxRecordSize)) {
                    RecordId         = record.RecordId,
                    RecordActualSize = recordSize,
                    RecordMaxSize    = maxRecordSize
                };

            transactionSize += recordSize;
            if (transactionSize > maxTransactionSize)
                throw new MaxAppendTransactionSizeExceededException(
                    string.Format(ErrorMessages.AppendTransactionSizeExceeded, transactionSize, maxTransactionSize)) {
                    TransactionActualSize = transactionSize,
                    TransactionMaxSize    = maxTransactionSize
                };

            request.Records.Add(serialized);
        }

        if (request.Records.Count == 0)
            throw new ValidationException(
                ErrorCode.From(nameof(AppendRecords)),
                ErrorMessages.AppendRecordsEmpty,
                ValidationProblem.Create(ErrorCode.From("AppendRecords"), ErrorMessages.AppendRecordsEmpty));

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
                    ErrorMessages.AppendRecordStreamRequired,
                    ValidationProblem.Create(
                        ErrorCode.FromType<AppendRecord>(),
                        ErrorMessages.AppendRecordStreamRequired));

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
    ) => AppendRecords(records, null, cancellationToken);
    
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
    ) => AppendRecords(records, null, cancellationToken);
}
