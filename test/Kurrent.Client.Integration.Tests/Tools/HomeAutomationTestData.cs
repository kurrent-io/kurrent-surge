using System.Collections;
using Kurrent.Client.Streams;
using Kurrent.Testing.Sample.HomeAutomation;

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
            .Homes(homes)
            .Select(home => SimulateHomeActivity(home, eventsPerHome))
            .ToList();
    }

    public static SmartHomeActivity SimulateHomeActivity(SmartHome home, int? numberOfEvents = null, long? startTime = null) {
        var events = HomeAutomationDataSet.Default
            .Events(home, numberOfEvents ?? Random.Shared.Next(5, 15), startTime);

        var request = events
            .Aggregate(
                AppendStreamRequest.New(),
                (bldr, evt) => bldr.WithRecord(CreateRecord(evt, bldr.MessageCount + 1)))
            .ForStream($"{nameof(SmartHomeActivity)}-{home.Id}")
            .Build();

        var lastEventTs = request.Messages.Last().Metadata
            .GetRequired<long>("iot.evt.timestamp");

        return new SmartHomeActivity(home, request, lastEventTs);
    }

    public static SmartHomeActivity SimulateHomeActivity(int? numberOfEvents = null, long? startTime = null) =>
        SimulateHomeActivity(HomeAutomationDataSet.Default.Home(), numberOfEvents, startTime);

    static AppendRecord CreateRecord(object evt, int sequence) {
        dynamic iotEvent = evt;

        Guid recordId  = iotEvent.EventId;
        long timestamp = iotEvent.Timestamp;

        var meta = Metadata.New
            .With("iot.evt.sequence", sequence)
            // .With(SystemMetadataKeys.Timestamp, timestamp)
            .With("iot.evt.timestamp", timestamp);

        var record = AppendRecord.New
            .WithValue(evt)
            .WithDataFormat(SchemaDataFormat.Json)
            .WithRecordId(recordId)
            .WithMetadata(meta)
            .Build();

        return record;
    }
}

public record SmartHomeActivity(SmartHome Home, AppendStreamRequest AppendRequest, long LastTimestamp) : IEnumerable<AppendRecord> {
    public string                    Stream   => AppendRequest.Stream;
    public IEnumerable<AppendRecord> Messages => AppendRequest.Messages;

    public SmartHomeActivity SimulateMoreEvents(int? numberOfEvents = null) =>
        this with { AppendRequest = HomeAutomationTestData.SimulateHomeActivity(Home, numberOfEvents, LastTimestamp) };

    public SmartHomeActivity WithExpectedRevision(long expectedRevision) =>
        this with { AppendRequest = AppendRequest with { ExpectedState = expectedRevision } };

    public static implicit operator AppendStreamRequest(SmartHomeActivity _) => _.AppendRequest;

    public IEnumerator<AppendRecord> GetEnumerator() => AppendRequest.Messages.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
