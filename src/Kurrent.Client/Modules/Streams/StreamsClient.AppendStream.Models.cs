namespace Kurrent.Client.Streams;

/// <summary>
/// The result of appending records to a single stream.
/// </summary>
// [PublicAPI]
// [method: SetsRequiredMembers]
public record AppendStreamResponse(StreamName Stream, Revision Revision, LogPosition Position) {
    // public required StreamName  Stream   { get; init; } = Stream;
    // public required Revision    Revision { get; init; } = Revision;
    // public required LogPosition Position { get; init; } = Position;
}

/// <summary>
/// The result of appending records to a single stream.
/// </summary>
// [PublicAPI]
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

