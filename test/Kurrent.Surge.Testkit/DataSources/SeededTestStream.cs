using System.Collections.Immutable;
using Kurrent.Surge.Client.Streams;

namespace Testing.ArcadeRacer;

public record SeededTestStream(TestStream TestStream, Revision Revision, LogPosition Position) {
    public StreamName Stream => TestStream.Stream;

    public ImmutableArray<AppendRecord> Records => TestStream.Records;

    public long LastTimestamp => TestStream.LastTimestamp;

    // public SimulatedRace SimulateMoreEvents(int? numberOfEvents = null) =>
    //     Race.SimulateMoreEvents(numberOfEvents);

    // public SimulatedRace WithExpectedRevision(long expectedRevision) =>
    //     Race.WithExpectedRevision(expectedRevision);

    public override string ToString() => $"{Stream} v{Revision.Value} @ {Position.Value}";
}
