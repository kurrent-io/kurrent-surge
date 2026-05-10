using Kurrent.Validation;

namespace Kurrent.Client.Streams;

/// <summary>
/// Represents a request to read records from the log.
/// </summary>
[PublicAPI]
public record ReadRecordsRequest : SelfValidating<ReadRecordsRequest, ReadRecordsRequest.Validator> {
    /// <summary>
    /// Fluent builder for constructing <see cref="ReadRecordsRequest"/> instances.
    /// </summary>
    public static Builder New => new();

    /// <summary>
    /// Starting position for the read operation in the global log.
    /// </summary>
    /// <remarks>
    /// When <see cref="Direction"/> is <see cref="ReadDirection.Forwards"/>,
    /// <see cref="LogPosition.Latest"/> is not allowed.
    /// </remarks>
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
    
    public class Builder(ReadRecordsRequest request) : FluentBuilder<Builder, ReadRecordsRequest>(request) {
        public Builder() : this(new()) { }
       
        /// <inheritdoc />
        protected override ReadRecordsRequest OnBuild(ReadRecordsRequest request) {
            request.EnsureValid();
            return request;
        }

        /// <summary>
        /// Sets the starting position for the read operation in the global log.
        /// </summary>
        public Builder FromPosition(LogPosition position) =>
            With(r => r with { Start = position });

        /// <summary>
        /// Starts reading from the latest position in the global log.
        /// </summary>
        /// <remarks>
        /// Use <see cref="Backwards"/> (or <see cref="FromLatestBackwards"/>)
        /// because <see cref="ReadDirection.Forwards"/> from <see cref="LogPosition.Latest"/>
        /// is rejected by request validation.
        /// </remarks>
        public Builder FromLatest() =>
            FromPosition(LogPosition.Latest);

        /// <summary>
        /// Starts reading from the earliest position in the global log.
        /// </summary>
        public Builder FromEarliest() =>
            FromPosition(LogPosition.Earliest);

        /// <summary>
        /// Starts reading backwards from the latest position in the global log.
        /// </summary>
        public Builder FromLatestBackwards() =>
            FromLatest().Backwards();

        /// <summary>
        /// Starts reading forwards from the earliest position in the global log.
        /// </summary>
        public Builder FromEarliestForwards() =>
            FromEarliest().Forwards();
        
        /// <summary>
        /// Sets the direction for reading records through the log.
        /// </summary>
        public Builder Direction(ReadDirection direction) =>
            With(x => x with { Direction = direction });
        
        /// <summary>
        /// Read forwards (toward newer records).
        /// </summary>
        public Builder Forwards() =>
            Direction(ReadDirection.Forwards);

        /// <summary>
        /// Read backwards (toward older records).
        /// </summary>
        public Builder Backwards() =>
            Direction(ReadDirection.Backwards);

        /// <summary>
        /// Limit the number of records returned.
        /// </summary>
        public Builder Limit(long maxRecords) =>
            With(x => x with { Limit = maxRecords });

        /// <summary>
        /// Sets record filtering criteria to apply during read operations.
        /// </summary>
        public Builder Filter(IRecordFilter filter) =>
            With(x => x with { Filter = filter });

        /// <summary>
        /// Sets record filtering criteria from a filter expression.
        /// </summary>
        public Builder Filter(string expression) =>
            Filter(RecordFilter.ByExpression(expression));

        /// <summary>
        /// Sets whether to skip record data deserialization for performance-critical scenarios.
        /// </summary>
        public Builder SkipDecoding(bool skip = true) =>
            With(x => x with { SkipDecoding = skip });
    }
    
    public class Validator : Validator<Validator, ReadRecordsRequest> {
        public Validator() {
            RuleFor(options => options.Limit)
                .GreaterThanOrEqualTo(1L)
                .WithMessage("Limit must be at least 1.");

            RuleFor(options => options.Start)
                .Must(start => start != LogPosition.Unset,
                    "Start position cannot be Unset when reading records from the log.");

            When(options => options.Direction == ReadDirection.Forwards, () => {
                RuleFor(options => options.Start)
                    .Must(start => start != LogPosition.Latest, 
                        "Start position cannot be Latest when reading forwards.");
            });
        }
    }
}


