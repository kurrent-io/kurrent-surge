// ReSharper disable ArrangeTypeMemberModifiers

using System.Collections;
using System.Collections.Immutable;
using Kurrent.Client;
using Kurrent.Client.Streams;
using Kurrent.Testing.Bogus;
using Kurrent.Testing.Containers.KurrentDB;
using Kurrent.Testing.Sample.HomeAutomation;
using Kurrent.Testing.TUnit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Serilog;
using TUnit.Core.Interfaces;
using AppendRecord = Kurrent.Client.Streams.AppendRecord;

using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Kurrent.Processors.Tests.Fixtures;

public sealed class KurrentClientTestFixture : IAsyncInitializer, IAsyncDisposable {
    [ClassDataSource<KurrentDBTestContainer>(Shared = SharedType.PerTestSession)]
    public required KurrentDBTestContainer KurrentDB { get; [UsedImplicitly] init; }

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    /// <summary>
    /// The logger used for logging in tests.
    /// </summary>
    public ILogger Logger => TestContext.CurrrentLogger;

    /// <summary>
    /// The logger factory used for creating loggers in tests.
    /// </summary>
    public ILoggerFactory LoggerFactory => TestContext.CurrentLoggerFactory;

    /// <summary>
    /// Time provider used for simulating and controlling time in tests.
    /// </summary>
    public FakeTimeProvider Time { get; } = new FakeTimeProvider();

    /// <summary>
    /// The Kurrent client used for testing.
    /// </summary>
    public KurrentClient Client { get; private set; } = null!;

    public Task InitializeAsync() {
       var lazyClient = new Lazy<KurrentClient>(() => {
            var builder = KurrentClientOptions.Parse(KurrentDB.AuthenticatedConnectionString).GetBuilder()
                .WithLoggerFactory(new TestkitLoggerFactory())
                .ConfigureSchema(KurrentClientSchemaOptions.NoValidation)
                .ConfigureResilience(KurrentClientResilienceOptions.NoResilience)
                .ConfigureMessages(options => options.Map<StreamMetadata>("$metadata"));

            return builder.CreateClient();
        });

        Client = lazyClient.Value;

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() =>
        await Client.DisposeAsync().ConfigureAwait(false);

    public async ValueTask<SeededSmartHomeActivity> SeedSmartHomeActivity(int numberOfEvents, CancellationToken cancellationToken) {
        var request  = HomeAutomationTestData.SimulateHomeActivity(numberOfEvents);
        var response = await Client.Streams.AppendAsync(request, cancellationToken: cancellationToken);

        Log.Debug(
            "Seeded SmartHomeActivity to stream {Stream} with {Count} events with revision {Revision} at position {Position}",
            request.Stream, request.Messages.Count, response.StreamRevision, response.Position);

        return new SeededSmartHomeActivity(request, response.StreamRevision, response.Position);
    }

    public async ValueTask<SeededSmartHomeActivity> SeedSmartHomeActivity(CancellationToken cancellationToken) =>
        await SeedSmartHomeActivity(Random.Shared.Next(3, 13), cancellationToken);
}

public record SeededSmartHomeActivity(SmartHomeActivity Activity, StreamRevision StreamRevision, LogPosition Position) {
    public SmartHome                   Home          => Activity.Home;
    public StreamName                  Stream        => Activity.Stream;
    public ImmutableList<AppendRecord> Records       => Activity.Messages;
    public long                        LastTimestamp => Activity.LastTimestamp;

    public SmartHomeActivity SimulateMoreEvents(int? numberOfEvents = null) =>
        Activity.SimulateMoreEvents(numberOfEvents);

    public SmartHomeActivity WithExpectedRevision(long expectedRevision) =>
        Activity.WithExpectedRevision(expectedRevision);

    public static implicit operator AppendStreamRequest(SeededSmartHomeActivity _) => _.Activity;
}

static class HomeAutomationTestData {
    public static SmartHomeActivity SimulateHomeActivity(int? numberOfEvents = null, long? startTime = null) =>
        SimulateHomeActivity(HomeAutomationDataSet.Default.Home(), numberOfEvents, startTime);

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

    static AppendRecord CreateRecord(object evt, int sequence) {
        dynamic iotEvent = evt;

        Guid recordId  = iotEvent.EventId;
        long timestamp = iotEvent.Timestamp;

        var meta = Metadata.New
            .With("iot.evt.sequence", sequence)
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
    public StreamName                  Stream   { get; } = AppendRequest.Stream;
    public ImmutableList<AppendRecord> Messages { get; } = AppendRequest.Messages.ToImmutableList();

    public SmartHomeActivity SimulateMoreEvents(int? numberOfEvents = null) =>
        this with { AppendRequest = HomeAutomationTestData.SimulateHomeActivity(Home, numberOfEvents, LastTimestamp) };

    public SmartHomeActivity WithExpectedRevision(long expectedRevision) =>
        this with { AppendRequest = AppendRequest with { ExpectedState = expectedRevision } };

    public static implicit operator AppendStreamRequest(SmartHomeActivity _) => _.AppendRequest;

    public IEnumerator<AppendRecord> GetEnumerator() => Messages.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
