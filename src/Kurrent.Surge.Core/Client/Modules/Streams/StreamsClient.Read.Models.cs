using Kurrent.Surge.Validation;
using Kurrent.Variant;

namespace Kurrent.Surge.Client.Streams;

/// <summary>
/// A single element of a read, which can be either a <see cref="Record"/> or a <see cref="Checkpoint"/>.
/// </summary>
public readonly partial record struct ReadMessage : IVariant<Record, Checkpoint> {
    internal static readonly ReadMessage None = default;
}

/// <summary>
/// Options for reading records from the global log.
/// </summary>
[PublicAPI]
[WithValidation]
public sealed partial class ReadRecordsOptions {
    /// <summary>
    /// Starting position for the read. Accepts a bare <see cref="LogPosition"/> (implicit) or a
    /// full <see cref="RecordPosition"/>. When the filter routes the read to a single stream,
    /// the stream half is used natively when it matches the target; otherwise the log half is
    /// resolved to the target stream's boundary record. Reads are INCLUSIVE forwards and
    /// EXCLUSIVE backwards (the server's $all contract).
    /// <para/>
    /// Defaults to <see cref="RecordPosition.Unset"/> — "no position given", which resolves to the
    /// natural edge for the <see cref="Direction"/>: the beginning when reading forwards, the end
    /// when reading backwards.
    /// </summary>
    /// <remarks>
    /// Checkpoint, track, and resume with <c>CheckpointPosition</c>; <see cref="Record.Position"/>
    /// is where the row lives, and through a pointer stream it sits behind the cursor.
    /// </remarks>
    public RecordPosition Start { get; set; } = RecordPosition.Unset;

    /// <summary>
    /// Maximum number of records to read in this operation.
    /// </summary>
    public long Limit { get; set; } = long.MaxValue;

    /// <summary>
    /// Record filtering criteria to apply during read operations. Defaults to
    /// <see cref="RecordFilter.ExcludeSystemRecords"/>; <see cref="RecordFilter.None"/> reads the raw log.
    /// </summary>
    public RecordFilter Filter { get; set; } = RecordFilter.ExcludeSystemRecords;

    /// <summary>
    /// Number of raw log records the server scans per filter page before emitting a
    /// <see cref="FilterCheckpoint"/> and returning results. Only applies when a server-side
    /// filter routes the read through <c>$all</c>. Default matches the SDK-wide scan window.
    /// </summary>
    public int FilterMaxSearchWindow { get; set; } = StreamDefaults.DefaultMaxSearchWindow;
    
    /// <summary>
    /// Direction for reading records through the log.
    /// </summary>
    public ReadDirection Direction { get; set; } = ReadDirection.Forwards;

    /// <summary>
    /// Skip record data deserialization for performance-critical scenarios.
    /// </summary>
    public bool SkipDecoding { get; set; }

    /// <summary>
    /// When true, a record whose schema name has no mapped type fails the read instead of
    /// passing through raw. Default false: a forward-unknown type is data for fallback
    /// handlers, not an error. Has no effect when <see cref="SkipDecoding"/> is set.
    /// </summary>
    public bool ThrowOnUnresolved { get; set; }

    static partial void ConfigureRules(Validator rules) {
        rules.RuleFor(options => options.Limit)
            .GreaterThanOrEqualTo(1L)
            .WithMessage("Limit must be at least 1.");

        // No rule on Start: Unset is the legal default meaning "no position given — resolve to
        // the direction's natural edge" (beginning forwards, end backwards). ResolveStartPosition
        // performs that substitution.
    }
}

/// <summary>
/// Options for reading records from a specific stream. The stream name is supplied
/// separately to the read operation, so it is not part of these options.
/// </summary>
[PublicAPI]
[WithValidation]
public sealed partial class ReadStreamOptions {
    /// <summary>
    /// Starting revision within the stream to begin reading from. Defaults to
    /// <see cref="Revision.Unset"/> — "no revision given", which resolves to the natural edge for
    /// the <see cref="Direction"/>: <see cref="Revision.Min"/> when reading forwards,
    /// <see cref="Revision.Max"/> when reading backwards.
    /// </summary>
    public Revision Start { get; set; } = Revision.Unset;

    /// <summary>Maximum number of records to read in this operation.</summary>
    public long Limit { get; set; } = long.MaxValue;

    /// <summary>Record filtering criteria to apply during read operations.</summary>
    public RecordFilter Filter { get; set; } = RecordFilter.None;

    /// <summary>
    /// Number of raw log records the server scans per filter page before emitting a
    /// <see cref="FilterCheckpoint"/> and returning results. Only applies when a non-stream
    /// <see cref="Filter"/> re-routes the read through <c>$all</c>; a direct stream read ignores it.
    /// Default matches the SDK-wide scan window.
    /// </summary>
    public int FilterMaxSearchWindow { get; set; } = StreamDefaults.DefaultMaxSearchWindow;

    /// <summary>Direction for reading records through the stream.</summary>
    public ReadDirection Direction { get; set; } = ReadDirection.Forwards;

    /// <summary>Skip record data deserialization for performance-critical scenarios.</summary>
    public bool SkipDecoding { get; set; }

    /// <summary>
    /// When true, a record whose schema name has no mapped type fails the read instead of
    /// passing through raw. Default false: a forward-unknown type is data for fallback
    /// handlers, not an error. Has no effect when <see cref="SkipDecoding"/> is set.
    /// </summary>
    public bool ThrowOnUnresolved { get; set; }

    static partial void ConfigureRules(Validator rules) {
        rules.RuleFor(options => options.Limit)
            .GreaterThanOrEqualTo(1L)
            .WithMessage("MaxRecords must be at least 1.");

        // Unset is the legal default meaning "no revision given — resolve to the direction's
        // natural edge" (Min forwards, Max backwards); the range checks apply only to an
        // explicit revision. Reading forwards from Max (the end) reads nothing — an empty
        // success, not an error — so it is NOT rejected here (matches $all's End + Forwards).
        rules.When(options => options.Start != Revision.Unset, () => {
            rules.RuleFor(options => options.Start)
                .Must(start => start >= Revision.Min, "Start revision cannot be less than Min.");

            rules.RuleFor(options => options.Start)
                .Must(start => start <= Revision.Max, "Start revision cannot be greater than Max.");
        });
    }
}
