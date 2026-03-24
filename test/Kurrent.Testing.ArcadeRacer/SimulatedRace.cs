using System.Collections;
using System.Collections.Immutable;
using Kurrent.Client.Streams;

namespace Testing.ArcadeRacer;

public record SimulatedRace : IEnumerable<AppendRecord> {
    SimulatedRace() { }

    public required StreamName                  Stream        { get; init; }
    public required ImmutableList<AppendRecord> Records       { get; init; }
    public required AppendRecordsRequest        AppendRequest { get; init; }
    public required long                        LastTimestamp { get; init; }

    public static SimulatedRace From(StreamName stream, AppendRecordsRequest request, long lastTimestamp) =>
        new() {
            Stream        = stream,
            AppendRequest = request,
            LastTimestamp = lastTimestamp,
            Records       = request.Records.ToImmutableList()
        };

    public IEnumerator<AppendRecord> GetEnumerator() => Records.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public SimulatedRace SimulateMoreEvents(int? numberOfEvents = null) =>
        ArcadeRacerTestData.SimulateRace(Stream, numberOfEvents);

    public SimulatedRace WithExpectedRevision(long expectedRevision) =>
        this with {
            AppendRequest = AppendRequest with {
                Checks = [ConsistencyCheck.Stream(Stream).AtRevision(Revision.From(expectedRevision))]
            }
        };

    public static implicit operator AppendRecordsRequest(SimulatedRace _) => _.AppendRequest;
}

public record SeededRace(SimulatedRace Race, Revision Revision, LogPosition Position) {
    public StreamName                  Stream        => Race.Stream;
    public ImmutableList<AppendRecord> Records       => Race.Records;
    public long                        LastTimestamp => Race.LastTimestamp;

    public SimulatedRace SimulateMoreEvents(int? numberOfEvents = null) =>
        Race.SimulateMoreEvents(numberOfEvents);

    public SimulatedRace WithExpectedRevision(long expectedRevision) =>
        Race.WithExpectedRevision(expectedRevision);

    public static implicit operator AppendRecordsRequest(SeededRace _) => _.Race;
}
