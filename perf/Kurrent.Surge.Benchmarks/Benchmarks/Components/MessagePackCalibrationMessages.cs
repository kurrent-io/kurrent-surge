using MessagePack;
using PolyType;

namespace Benchmarks;

/// <summary>
/// A msgpack-safe mirror of <see cref="WriteMappingPipelineBenchmarks"/>'s two-record calibration
/// payload. Both MessagePack-CSharp and Nerdbank.MessagePack require a compile-time-known,
/// generator-covered type — unlike json/proto, encoding the protobuf-generated
/// <c>AppendRecordsRequest</c> directly would need a hand-written formatter, which no real caller
/// writes either. Same record count and field shape as the wire request; different object identity.
/// </summary>
[MessagePackObject]
public record CalibrationMsgPackRecord {
    [Key(0)] public required string RecordId   { get; init; }
    [Key(1)] public required string Stream     { get; init; }
    [Key(2)] public required string Data       { get; init; }
    [Key(3)] public required string SchemaId   { get; init; }
    [Key(4)] public required string SchemaName { get; init; }
}

[MessagePackObject]
public record CalibrationMsgPackRequest {
    [Key(0)] public required IReadOnlyList<CalibrationMsgPackRecord> Records { get; init; }
}

[GeneratedMessagePackResolver]
public partial class CalibrationMessagePackResolver;

[GenerateShapeFor<CalibrationMsgPackRequest>]
public partial class CalibrationMessagePackWitness;
