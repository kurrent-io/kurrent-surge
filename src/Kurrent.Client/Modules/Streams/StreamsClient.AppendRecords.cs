using Kurrent.Client.Exceptions;
using Kurrent.Validation;
using Resources;

using Contracts = KurrentDB.Protocol.V2.Streams;

namespace Kurrent.Client.Streams;

public partial class StreamsClient {
    /// <summary>
    /// Appends records from a materialized request. All records are validated and serialized
    /// before sending. Use for small atomic batches where the full request is inspectable.
    /// </summary>
    public async ValueTask<AppendRecordsResponse> AppendRecords(
        AppendRecordsRequest request,
        CancellationToken cancellationToken = default
    ) {
        request.EnsureValid();

        var ctx = new MappingContext(SerializerProvider, MetadataDecoder, RegistryPolicy, cancellationToken);

        try {
            var serviceRequest = await request
                .ToContract(ctx)
                .ConfigureAwait(false);

            var rsp = await ServiceClient
                .AppendRecordsAsync(serviceRequest, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return rsp.ToResponse();
        }
        catch (Exception ex) {
            throw ex.AsWellKnownException(
                AppendConsistencyViolationException.From,
                MaxAppendRecordSizeExceededException.From,
                MaxAppendTransactionSizeExceededException.From);
        }
    }

    /// <summary>
    /// Appends records from an enumerable source in a single pass — each record is validated,
    /// stamped with batch overrides, and serialized as it is iterated. The records are never
    /// materialized into an intermediate collection.
    /// </summary>
    public ValueTask<AppendRecordsResponse> AppendRecords(
        IEnumerable<AppendRecord> records,
        CancellationToken cancellationToken = default
    ) => AppendRecords(records, options: null, cancellationToken);

    /// <summary>
    /// Appends records from an enumerable source with optional consistency checks and
    /// batch-level overrides. Single-pass: each record is validated, stamped, and serialized
    /// as it is iterated.
    /// </summary>
    public async ValueTask<AppendRecordsResponse> AppendRecords(
        IEnumerable<AppendRecord> records,
        AppendRecordsOptions? options,
        CancellationToken cancellationToken = default
    ) {
        var ctx     = new MappingContext(SerializerProvider, MetadataDecoder, RegistryPolicy, cancellationToken);
        var request = new Contracts.AppendRecordsRequest();
        var count   = 0;

        foreach (var record in records) {
            var stamped = ApplyOverrides(record, options);
            ValidateRecord(stamped);
            request.Records.Add(await stamped.ToContract(ctx).ConfigureAwait(false));
            count++;
        }

        if (count == 0)
            throw new ValidationException(
                ErrorCode.FromType<AppendRecordsRequest>(),
                ErrorMessages.AppendRecordsRequestEmpty);

        AppendChecks(request, options);

        return await SendAppendRequest(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Appends records from an async enumerable source with optional consistency checks and
    /// batch-level overrides. Single-pass: each record is validated, stamped, and serialized
    /// as it is yielded.
    /// </summary>
    public ValueTask<AppendRecordsResponse> AppendRecords(
        IAsyncEnumerable<AppendRecord> records,
        CancellationToken cancellationToken = default
    ) => AppendRecords(records, options: null, cancellationToken);

    /// <summary>
    /// Appends records from an async enumerable source with optional consistency checks and
    /// batch-level overrides. Single-pass: each record is validated, stamped, and serialized
    /// as it is yielded.
    /// </summary>
    public async ValueTask<AppendRecordsResponse> AppendRecords(
        IAsyncEnumerable<AppendRecord> records,
        AppendRecordsOptions? options,
        CancellationToken cancellationToken = default
    ) {
        var ctx     = new MappingContext(SerializerProvider, MetadataDecoder, RegistryPolicy, cancellationToken);
        var request = new Contracts.AppendRecordsRequest();
        var count   = 0;

        await foreach (var record in records.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            var stamped = ApplyOverrides(record, options);
            ValidateRecord(stamped);
            request.Records.Add(await stamped.ToContract(ctx).ConfigureAwait(false));
            count++;
        }

        if (count == 0)
            throw new ValidationException(
                ErrorCode.FromType<AppendRecordsRequest>(),
                ErrorMessages.AppendRecordsRequestEmpty);

        AppendChecks(request, options);

        return await SendAppendRequest(request, cancellationToken).ConfigureAwait(false);
    }

    #region ->> AppendRecords Internals <<-

    static AppendRecord ApplyOverrides(AppendRecord record, AppendRecordsOptions? options) {
        if (options is null)
            return record;

        if (options.DataFormat is { } fmt)
            record = record with { DataFormat = fmt };

        if (options.Metadata is { } meta)
            record = record with { Metadata = new Metadata(record.Metadata).WithMany(meta) };

        return record;
    }

    static void ValidateRecord(AppendRecord record) {
        if (record.Stream == StreamName.Unset)
            throw new ValidationException(
                ErrorCode.FromType<AppendRecordsRequest>(),
                ErrorMessages.AppendRecordStreamRequired,
                ValidationProblem.Create(
                    ErrorCode.FromType<AppendRecordsRequest>(),
                    ErrorMessages.AppendRecordStreamRequired));

        record.EnsureValid();
    }

    static void AppendChecks(Contracts.AppendRecordsRequest request, AppendRecordsOptions? options) {
        if (options?.Checks is not { Length: > 0 } checks)
            return;

        foreach (var check in checks)
            if (check != ConsistencyCheck.None)
                request.Checks.Add(check.ToContract());
    }

    async ValueTask<AppendRecordsResponse> SendAppendRequest(
        Contracts.AppendRecordsRequest request, CancellationToken ct
    ) {
        try {
            var rsp = await ServiceClient
                .AppendRecordsAsync(request, cancellationToken: ct)
                .ConfigureAwait(false);

            return rsp.ToResponse();
        }
        catch (Exception ex) {
            throw ex.AsWellKnownException(
                AppendConsistencyViolationException.From,
                MaxAppendRecordSizeExceededException.From,
                MaxAppendTransactionSizeExceededException.From);
        }
    }

    #endregion
}
