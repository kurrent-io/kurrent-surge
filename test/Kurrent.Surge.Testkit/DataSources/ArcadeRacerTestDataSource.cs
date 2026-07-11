using System.Collections.Immutable;
using Bogus;
using Kurrent.Surge.Client.Streams;
using Kurrent.Testkit.Datasets.ArcadeRacer;
using Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;

namespace Testing.ArcadeRacer;

public class ArcadeRacerTestDataSource(Faker faker) : ITestDataSource {
    public ArcadeRacerTestDataSource() : this(new Faker()) { }

    Faker Faker { get; } = faker;

    public string Name => "Arcade Racer";

    public async ValueTask<TestStream> GenerateSet(int numberOfEvents, CancellationToken cancellationToken) {
        if (TestDataSource.IsUnspecifiedCount(numberOfEvents)) {
            var logbook = ArcadeRaceSimulator
                .NewRace(Faker)
                .WithCars(8)
                .WithLaps(10)
                .Simulate();

            return SimulateRace(logbook.RaceId, logbook.Events);
        } else {
            await using var simulator = ArcadeRaceSimulator
                .NewStreamingRace(Faker)
                .Start();

            var events = await simulator
                .RaceEvents(ct: cancellationToken)
                .Take(numberOfEvents)
                .ToListAsync(cancellationToken);

            return SimulateRace(simulator.RaceId, events);
        }

        static TestStream SimulateRace(string raceId, IReadOnlyList<IRaceEvent> events) {
            var stream = $"race-{raceId}";

            var records = events
                .Select(evt => AppendRecord.Create(evt, r => {
                    r.Stream    = stream;
                    r.RecordId  = evt.EventId;
                    r.Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(evt.Timestamp);
                    r.Metadata.With("stream", stream);
                }))
                .ToImmutableArray();

            return TestStream.From(stream, records);
        }
    }
}
