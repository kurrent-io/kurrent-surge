using Bogus;
using Kurrent.Client.Streams;
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

        static TestStream SimulateRace(Guid raceId, IReadOnlyList<IRaceEvent> events) {
            StreamName stream  = $"ArcadeRace-{raceId:N}";
            var        request = events
                .Aggregate(
                    AppendRecordsRequest.New(),
                    (bldr, evt) => bldr.WithRecord(rec => rec.ForStream(stream).WithId(evt.EventId).WithValue(evt).WithTimestamp(evt.Timestamp)))
                .Build();

            return TestStream.From(request);
        }
    }
}
