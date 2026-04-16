using FluentValidation;
using Kurrent.Validation;

namespace Kurrent.Client.Streams;

[PublicAPI]
public record ReadRecordsRequest : SelfValidating<ReadRecordsRequest, ReadRecordsRequest.Validator> {
    public static Builder New => new();

    /// <summary>
    /// Starting position for the read operation in the global log.
    /// </summary>
    public LogPosition Start { get; init; } = LogPosition.Latest;

    /// <summary>
    /// Maximum number of records to read in this operation.
    /// </summary>
    public long Limit { get; init; } = long.MaxValue;

    /// <summary>
    /// Record filtering criteria to apply during read operations.
    /// </summary>
    public IRecordFilter Filter { get; init; } = RecordFilter.None;

    /// <summary>
    /// Direction for reading records through the log.
    /// </summary>
    public ReadDirection Direction { get; init; } = ReadDirection.Forwards;

    /// <summary>
    /// Skip record data deserialization for performance-critical scenarios.
    /// </summary>
    public bool SkipDecoding { get; init; }
    
    public class Builder() : FluentBuilder<Builder, ReadRecordsRequest>(new()) {
        /// <inheritdoc />
        protected override ReadRecordsRequest OnBuild(ReadRecordsRequest request) {
            request.EnsureValid();
            return request;
        }

        /// <summary>
        /// Sets the starting log position.
        /// </summary>
        public Builder FromPosition(LogPosition position) =>
            With(r => r with { Start = position });

        /// <summary>
        /// Read forwards (toward newer records).
        /// </summary>
        public Builder Forward() =>
            With(request => request with { Direction = ReadDirection.Forwards });

        /// <summary>
        /// Read backwards (toward older records).
        /// </summary>
        public Builder Backwards() =>
            With(request => request with { Direction = ReadDirection.Backwards });

        /// <summary>
        /// Limit the number of records returned.
        /// </summary>
        public Builder WithMaxRecords(long maxRecords) =>
            With(request => request with { Limit = maxRecords });

        /// <summary>
        /// Apply a record filter.
        /// </summary>
        public Builder WithFilter(IRecordFilter filter) =>
            With(request => request with { Filter = filter });

        /// <summary>
        /// Apply a filter expression. Sugar for <c>RecordFilter.ByExpression(...)</c>.
        /// </summary>
        public Builder WithFilterExpression(string expression) =>
            With(request => request with { Filter = RecordFilter.ByExpression(expression) });

        /// <summary>
        /// Skip record data deserialization.
        /// </summary>
        public Builder SkipDecoding() =>
            With(request => request with { SkipDecoding = true });
    }
    
    public class Validator : FluentValidator<Validator, ReadRecordsRequest> {
        public Validator() {
            RuleFor(options => options.Limit)
                .GreaterThanOrEqualTo(1)
                .WithMessage("MaxRecords must be at least 1.")
                .WithSeverity(Severity.Error);
        }
    }
}


