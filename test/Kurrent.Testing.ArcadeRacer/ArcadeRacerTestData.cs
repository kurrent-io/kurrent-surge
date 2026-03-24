using Kurrent.Client.Streams;
using Testing.Datasets.ArcadeRacer;
using Testing.Datasets.ArcadeRacer.Contracts;

namespace Testing.ArcadeRacer;

public static class ArcadeRacerTestData {
    public static SimulatedRace SimulateRace(int? numberOfEvents = null) =>
        SimulateRace($"ArcadeRace-{Guid.NewGuid():N}", numberOfEvents);

    internal static SimulatedRace SimulateRace(StreamName stream, int? numberOfEvents = null) {
        var eventCount = numberOfEvents ?? Random.Shared.Next(5, 15);

        var race = new ArcadeRaceBuilder()
            .WithCars(4)
            .WithLaps(Math.Max(eventCount, 5))
            .SimulateAsync().GetAwaiter().GetResult();

        var events = race.Events.Take(eventCount).ToList();

        var request = events
            .Aggregate(
                AppendRecordsRequest.ToStream(stream),
                (bldr, evt) => bldr.WithRecord(rec => rec
                    .WithRecordId(evt.EventId)
                    .WithValue(evt)
                    .WithDataFormat(SchemaDataFormat.Json)
                    .WithMetadata("race.evt.sequence", bldr.RecordsCount + 1)
                    .WithMetadata("race.evt.timestamp", evt.Timestamp)))
            .Build();

        return SimulatedRace.From(stream, request, events[^1].Timestamp);
    }

    public static List<SimulatedRace> SimulateRaces(int races = 3, int eventsPerRace = 100) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(races);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(eventsPerRace);

        return Enumerable.Range(0, races)
            .Select(_ => SimulateRace(eventsPerRace))
            .ToList();
    }
}
