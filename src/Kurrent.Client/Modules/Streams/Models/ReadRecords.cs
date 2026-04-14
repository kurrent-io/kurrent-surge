using FluentValidation;
using Kurrent.Validation;

namespace Kurrent.Client.Streams;

// /// <summary>
// /// Static entry point for building global log read requests.
// /// </summary>
// [PublicAPI]
// public static class ReadRecords {
//     /// <summary>
//     /// Start reading from the earliest position in the log (forwards).
//     /// </summary>
//     public static ReadRecordsRequestBuilder FromStart() =>
//         new ReadRecordsRequestBuilder()
//             .From(LogPosition.Earliest)
//             .Forward();
//
//     /// <summary>
//     /// Start reading from the end of the log (backwards).
//     /// </summary>
//     public static ReadRecordsRequestBuilder FromEnd() =>
//         new ReadRecordsRequestBuilder()
//             .From(LogPosition.Latest)
//             .Backwards();
//
//     /// <summary>
//     /// Start reading from a specific log position.
//     /// </summary>
//     public static ReadRecordsRequestBuilder From(LogPosition position) =>
//         new ReadRecordsRequestBuilder()
//             .From(position);
// }

/// <summary>
/// Fluent builder for constructing <see cref="ReadRecordsRequest"/> instances.
/// </summary>
[PublicAPI]
public class ReadRecordsRequestBuilder()
    : FluentBuilder<ReadRecordsRequestBuilder, ReadRecordsRequest>(new ReadRecordsRequest()) {
    /// <inheritdoc />
    protected override ReadRecordsRequest OnBuild(ReadRecordsRequest request) {
        request.EnsureValid();
        return request;
    }

    /// <summary>
    /// Sets the starting log position.
    /// </summary>
    public ReadRecordsRequestBuilder FromPosition(LogPosition position) =>
        With(request => request with { Start = position });

    /// <summary>
    /// Read forwards (toward newer records).
    /// </summary>
    public ReadRecordsRequestBuilder Forward() =>
        With(request => request with { Direction = ReadDirection.Forwards });

    /// <summary>
    /// Read backwards (toward older records).
    /// </summary>
    public ReadRecordsRequestBuilder Backwards() =>
        With(request => request with { Direction = ReadDirection.Backwards });

    /// <summary>
    /// Limit the number of records returned.
    /// </summary>
    public ReadRecordsRequestBuilder WithMaxRecords(long maxRecords) =>
        With(request => request with { Limit = maxRecords });

    /// <summary>
    /// Apply a record filter.
    /// </summary>
    public ReadRecordsRequestBuilder WithFilter(IRecordFilter filter) =>
        With(request => request with { Filter = filter });

    /// <summary>
    /// Apply a filter expression. Sugar for <c>RecordFilter.ByExpression(...)</c>.
    /// </summary>
    public ReadRecordsRequestBuilder WithFilterExpression(string expression) =>
        With(request => request with { Filter = RecordFilter.ByExpression(expression) });

    /// <summary>
    /// Skip record data deserialization.
    /// </summary>
    public ReadRecordsRequestBuilder SkipDecoding() =>
        With(request => request with { SkipDecoding = true });
}

[PublicAPI]
public record ReadRecordsRequest : SelfValidating<ReadRecordsRequest, ReadRecordsRequestValidator> {
    public static ReadRecordsRequestBuilder New => new();

    /// <summary>
    /// Starting position for the read operation in the global log.
    /// </summary>
    public LogPosition Start { get; init; } = LogPosition.Latest;

    /// <summary>Maximum number of records to read in this operation.</summary>
    public long Limit { get; init; } = long.MaxValue;

    /// <summary>Record filtering criteria to apply during read operations.</summary>
    public IRecordFilter Filter { get; init; } = RecordFilter.None;

    /// <summary>Direction for reading records through the log.</summary>
    public ReadDirection Direction { get; init; } = ReadDirection.Forwards;

    /// <summary>Skip record data deserialization for performance-critical scenarios.</summary>
    public bool SkipDecoding { get; init; }
}

public class ReadRecordsRequestValidator : FluentValidator<ReadRecordsRequestValidator, ReadRecordsRequest> {
    public ReadRecordsRequestValidator() {
        RuleFor(options => options.Limit)
            .GreaterThanOrEqualTo(1)
            .WithMessage("MaxRecords must be at least 1.")
            .WithSeverity(Severity.Error);
    }
}

