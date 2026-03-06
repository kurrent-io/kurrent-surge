using System.Collections;
using System.Collections.Immutable;
using Kurrent.Client.Streams;
using Testing.Datasets.HomeAutomation;
using Testing.Datasets.HomeAutomation.Model;

namespace Kurrent.Client.Tests.Tools;

/// <summary>
/// Provides methods to simulate home automation activities and generate test data for smart home scenarios.
/// This includes creating streams of events for multiple homes in a housing complex.
/// It is useful for testing and development purposes.
/// </summary>
public static class HomeAutomationTestData {
    public static List<SmartHomeActivity> SimulateHousingComplexActivity(int homes = 3, int eventsPerHome = 100) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(homes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(eventsPerHome);

        return HomeAutomationDataSet.Default
            .Homes()
            .Take(homes)
            .Select(home => SimulateHomeActivity(home, eventsPerHome))
            .ToList();
    }

    public static SmartHomeActivity SimulateHomeActivity(SmartHome home, int? numberOfEvents = null, long? startTime = null) {
        var events = HomeAutomationDataSet.Default
            .Events(home).Take(numberOfEvents ?? Random.Shared.Next(5, 15)).ToList();

        var request = events
            .Aggregate(
                AppendRecordsRequest.ToStream($"{nameof(SmartHomeActivity)}-{home.Id}"),
                (bldr, evt) => {
                    dynamic iotEvent = evt;
                    return bldr.WithRecord(rec => rec
                        .WithRecordId(iotEvent.EventId)
                        .WithValue(evt)
                        .WithDataFormat(SchemaDataFormat.Json)
                        .WithMetadata("iot.evt.sequence", bldr.RecordsCount + 1)
                        .WithMetadata("iot.evt.timestamp", iotEvent.Timestamp));
                }
            )
            .Build();

        var lastEventTs = request.Records.Last().Metadata.GetRequiredValue<long>("iot.evt.timestamp");

        return SmartHomeActivity.From(home, request, lastEventTs);
    }

    public static SmartHomeActivity SimulateHomeActivity(int? numberOfEvents = null, long? startTime = null) =>
        SimulateHomeActivity(HomeAutomationDataSet.Default.Home(), numberOfEvents, startTime);
}

public record SmartHomeActivity : IEnumerable<AppendRecord> {
    SmartHomeActivity() { }

    public required StreamName                  Stream        { get; init; }
    public required ImmutableList<AppendRecord> Records       { get; init; }
    public required SmartHome                   Home          { get; init; }
    public required AppendRecordsRequest        AppendRequest { get; init; }
    public required long                        LastTimestamp { get; init; }

    public static SmartHomeActivity From(SmartHome home, AppendRecordsRequest request, long lastTimestamp) =>
        new() {
            Home          = home,
            AppendRequest = request,
            LastTimestamp = lastTimestamp,
            Stream        = request.Records[0].Stream,
            Records       = request.Records.ToImmutableList()
        };

    public IEnumerator<AppendRecord> GetEnumerator() => Records.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public SmartHomeActivity SimulateMoreEvents(int? numberOfEvents = null) => HomeAutomationTestData.SimulateHomeActivity(Home, numberOfEvents, LastTimestamp);

    public SmartHomeActivity WithExpectedRevision(long expectedRevision) =>
        this with {
            AppendRequest = AppendRequest with {
                Checks = [ConsistencyCheck.Stream(Stream).AtRevision(Revision.From(expectedRevision))]
            }
        };

    public static implicit operator AppendRecordsRequest(SmartHomeActivity _) => _.AppendRequest;

    public void Deconstruct(out SmartHome home, out AppendRecordsRequest request, out long lastTimestamp) {
        home          = Home;
        request       = AppendRequest;
        lastTimestamp = LastTimestamp;
    }
}
