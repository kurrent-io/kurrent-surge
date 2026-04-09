using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Kurrent.Client.Streams;

/// <summary>
/// Represents a request to append records to a single stream.
/// All records are assigned to the specified stream regardless of their individual Stream property.
/// </summary>
[PublicAPI]
public record AppendStreamRequest() {
    /// <summary>
    /// Creates a builder for appending records to the specified stream.
    /// </summary>
    public static AppendStreamRequestBuilder For(StreamName stream) => new(stream);

    // [method: SetsRequiredMembers]
    // public AppendStreamRequest(
    //     StreamName stream,
    //     AppendRecord[] records,
    //     ConsistencyCheck[] checks
    // ) : this() {
    //     Stream  = stream;
    //     Records = [..records];
    //     Checks  = [..checks];
    // }

    [method: SetsRequiredMembers]
    public AppendStreamRequest(
        StreamName stream,
        ImmutableArray<AppendRecord> records,
        ExpectedStreamState expectedState
    ) : this() {
        Stream        = stream;
        Records       = records;
        ExpectedState = expectedState;
    }

    /// <summary>
    /// The target stream for all records.
    /// </summary>
    public required StreamName Stream { get; init; }

    /// <summary>
    /// The records to append. All will be assigned to <see cref="Stream"/>.
    /// </summary>
    public required ImmutableArray<AppendRecord> Records { get; init; }

    /// <summary>
    /// The expected state of the stream before appending.
    /// </summary>
    public required ExpectedStreamState ExpectedState { get; init; }

    // /// <summary>
    // /// Consistency checks to enforce before appending.
    // /// </summary>
    // public required ImmutableArray<ConsistencyCheck> Checks { get; init; }

    /// <summary>
    /// Validates all records meet their invariants and stamps the target stream on each,
    /// while also filtering out irrelevant stream-state checks.
    /// Returns a new request with the validated, stream-stamped records.
    /// </summary>
    public AppendStreamRequest EnsureValid() {
        var builder        = Records.ToBuilder();
        var recordsChanged = false;

        for(var i = 0; i < builder.Count; i++) {
            var record = builder[i].EnsureValid();

            if (record.Stream == Stream)
                continue;

            builder[i]     = record with { Stream = Stream };
            recordsChanged = true;
        }

        // var checksBuilder = Checks.ToBuilder();
        // var checksChanged = false;
        //
        // for(var i = 0; i < checksBuilder.Count; i++) {
        //     var check = checksBuilder[i];
        //
        //     if (check.Value is not StreamStateCheck ssc || ssc.ExpectedState == ExpectedStreamState.Any)
        //         continue;
        //
        //     builder.RemoveAt(i);
        //     checksChanged = true;
        // }
        //
        // if (recordsChanged && checksChanged) {
        //     return this with {
        //         Records = builder.MoveToImmutable(),
        //         Checks  = checksBuilder.MoveToImmutable()
        //     };
        // }

        if (recordsChanged)
            return this with { Records = builder.MoveToImmutable() };

        // if (checksChanged)
        //     return this with { Checks = checksBuilder.MoveToImmutable() };

        return this;
    }
}

/// <summary>
/// The result of appending records to a single stream.
/// </summary>
[PublicAPI]
// [method: SetsRequiredMembers]
public record AppendStreamResponse(StreamName Stream, Revision Revision, LogPosition Position) {
    // public required StreamName  Stream   { get; init; } = Stream;
    // public required Revision    Revision { get; init; } = Revision;
    // public required LogPosition Position { get; init; } = Position;
}


/// <summary>
/// The result of appending records to a single stream.
/// </summary>
[PublicAPI]
// [method: SetsRequiredMembers]
public record AppendStreamResponse4(LogPosition Position, Revision Revision) {
    // public required Revision    Revision { get; init; } = Revision;
    // public required LogPosition Position { get; init; } = Position;
}

public record AppendStreamResponse2(StreamPosition StreamPosition, LogPosition LogPosition) {
    public static AppendStreamResponse2 Unset { get; } = new(StreamPosition.Unset, LogPosition.Unset);
}

public record AppendStreamResponse3(RecordPosition Position) {
    public static AppendStreamResponse3 Unset { get; } = new(RecordPosition.Unset);
}

