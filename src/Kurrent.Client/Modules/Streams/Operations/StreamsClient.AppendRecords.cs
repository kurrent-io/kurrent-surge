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

        try {
            var serviceRequest = await Mapper
                .ToContract(request, cancellationToken)
                .ConfigureAwait(false);

            var rsp = await ServiceClient
                .AppendRecordsAsync(serviceRequest, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Mapper.ToAppendRecordsResponse(rsp);
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
        var request = new Contracts.AppendRecordsRequest();
        var count   = 0;

        foreach (var record in records) {
            var stamped = ApplyOverrides(record, options);
            ValidateRecord(stamped);
            request.Records.Add(await Mapper.ToContract(stamped, cancellationToken).ConfigureAwait(false));
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
        var request = new Contracts.AppendRecordsRequest();
        var count   = 0;

        await foreach (var record in records.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            var stamped = ApplyOverrides(record, options);
            ValidateRecord(stamped);
            request.Records.Add(await Mapper.ToContract(stamped, cancellationToken).ConfigureAwait(false));
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

        // Mapper.ToContract(check) is a static-like pure mapping — access it
        // through the instance to keep the existing pattern, but the checks
        // are small and not worth a separate code path.
        foreach (var check in checks)
            if (check != ConsistencyCheck.None)
                request.Checks.Add(new Contracts.ConsistencyCheck {
                    StreamState = check.Value switch {
                        StreamStateCheck sc => new() {
                            Stream        = sc.Stream,
                            ExpectedState = sc.ExpectedState.ToWireValue()
                        },
                        _ => throw new ArgumentException(
                            $"Unsupported consistency check type: {check.Value.GetType().Name}")
                    }
                });
    }

    async ValueTask<AppendRecordsResponse> SendAppendRequest(
        Contracts.AppendRecordsRequest request, CancellationToken ct
    ) {
        try {
            var rsp = await ServiceClient
                .AppendRecordsAsync(request, cancellationToken: ct)
                .ConfigureAwait(false);

            return Mapper.ToAppendRecordsResponse(rsp);
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
