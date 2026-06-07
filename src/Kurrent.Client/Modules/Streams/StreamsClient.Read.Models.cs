using Kurrent.Validation;

namespace Kurrent.Client.Streams;

/// <summary>
/// Options for reading records from the global log.
/// </summary>
[PublicAPI]
public record ReadRecordsOptions : SelfValidating<ReadRecordsOptions, ReadRecordsOptions.Validator> {
    /// <summary>
    /// Starting position for the read. Accepts a bare <see cref="LogPosition"/> (implicit) or a
    /// full <see cref="RecordPosition"/>. When the filter routes the read to a single stream,
    /// the stream half is used natively when it matches the target; otherwise the log half is
    /// resolved to the target stream's boundary record. Reads are INCLUSIVE forwards and
    /// EXCLUSIVE backwards (the server's $all contract).
    /// </summary>
    public RecordPosition Start { get; init; } = RecordPosition.Latest;

    /// <summary>
    /// Maximum number of records to read in this operation.
    /// </summary>
    public long Limit { get; init; } = long.MaxValue;

    /// <summary>
    /// Record filtering criteria to apply during read operations.
    /// </summary>
    public RecordFilter Filter { get; init; } = RecordFilter.None;

    /// <summary>
    /// Number of raw log records the server scans per filter page before emitting a
    /// <see cref="FilterCheckpoint"/> and returning results. Only applies when a server-side
    /// filter routes the read through <c>$all</c>. Default matches the SDK-wide scan window.
    /// </summary>
    public int FilterMaxSearchWindow { get; init; } = (int)StreamsClientMapping.DefaultMaxSearchWindow;
    
    /// <summary>
    /// Direction for reading records through the log.
    /// </summary>
    public ReadDirection Direction { get; init; } = ReadDirection.Forwards;

    /// <summary>
    /// Skip record data deserialization for performance-critical scenarios.
    /// </summary>
    public bool SkipDecoding { get; init; }

    /// <summary>
    /// When true, a record whose schema name has no mapped type fails the read instead of
    /// passing through raw. Default false: a forward-unknown type is data for fallback
    /// handlers, not an error. Has no effect when <see cref="SkipDecoding"/> is set.
    /// </summary>
    public bool ThrowOnUnresolved { get; init; }

    /// <summary>
    /// Resolve link records (<c>$&gt;</c>) to their target events. Index streams (<c>$ce-*</c>,
    /// <c>$idx-*</c>) contain links; without resolution, consumers see opaque link records
    /// instead of the underlying events. Default <c>true</c>. Set <c>false</c> for low-level
    /// inspection of index/projection streams.
    /// </summary>
    public bool ResolveLinks { get; init; } = true;
    

    public class Validator : Validator<Validator, ReadRecordsOptions> {
        public Validator() {
            RuleFor(options => options.Limit)
                .GreaterThanOrEqualTo(1L)
                .WithMessage("Limit must be at least 1.");

            RuleFor(options => options.Start)
                .Must(start => !start.IsUnset,
                    "Start position cannot be Unset when reading records.");
        }
    }
}

/// <summary>
/// Options for reading records from a specific stream. The stream name is supplied
/// separately to the read operation, so it is not part of these options.
/// </summary>
[PublicAPI]
public record ReadStreamOptions : SelfValidating<ReadStreamOptions, ReadStreamOptions.Validator> {
    /// <summary>
    /// Starting revision within the stream to begin reading from.
    /// </summary>
    public Revision Start { get; init; } = Revision.Min;

    /// <summary>Maximum number of records to read in this operation.</summary>
    public long Limit { get; init; } = long.MaxValue;

    /// <summary>Record filtering criteria to apply during read operations.</summary>
    public RecordFilter Filter { get; init; } = RecordFilter.None;

    /// <summary>Direction for reading records through the stream.</summary>
    public ReadDirection Direction { get; init; } = ReadDirection.Forwards;

    /// <summary>Skip record data deserialization for performance-critical scenarios.</summary>
    public bool SkipDecoding { get; init; }

    /// <summary>
    /// When true, a record whose schema name has no mapped type fails the read instead of
    /// passing through raw. Default false: a forward-unknown type is data for fallback
    /// handlers, not an error. Has no effect when <see cref="SkipDecoding"/> is set.
    /// </summary>
    public bool ThrowOnUnresolved { get; init; }

    /// <summary>
    /// Resolve link records (<c>$&gt;</c>) to their target events. Default <c>true</c>.
    /// Set <c>false</c> when inspecting an index/projection stream and you want the raw
    /// link records instead of their resolved targets.
    /// </summary>
    public bool ResolveLinks { get; init; } = true;

    public class Validator : Validator<Validator, ReadStreamOptions> {
        public Validator() {
            RuleFor(options => options.Limit)
                .GreaterThanOrEqualTo(1L)
                .WithMessage("MaxRecords must be at least 1.");

            RuleFor(options => options.Start)
                .Must(start => start != Revision.Unset,
                    "Start revision cannot be Unset when reading from a specific stream. Specify a valid revision.");

            RuleFor(options => options.Start)
                .Must(start => start >= Revision.Min, "Start revision cannot be less than Min.");

            RuleFor(options => options.Start)
                .Must(start => start <= Revision.Max, "Start revision cannot be greater than Max.");

            When(options => options.Direction == ReadDirection.Forwards, () => {
                RuleFor(options => options.Start)
                    .Must(start => start != Revision.Max, "Start revision cannot be Max when reading forwards.");
            });
        }
    }
}
