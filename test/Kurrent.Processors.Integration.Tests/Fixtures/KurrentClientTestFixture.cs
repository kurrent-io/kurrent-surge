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
                .ConfigureSchema(schema => schema
                    .WithValidation(false)
                    .ConfigureMessages(msg => msg.Map<StreamMetadata>("$metadata")))
                .ConfigureResilience(KurrentClientResilienceOptions.NoResilience);

            return builder.CreateClient();
        });

        Client = lazyClient.Value;

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() =>
        await Client.DisposeAsync().ConfigureAwait(false);

    public async ValueTask<SeededSmartHomeActivity> SeedSmartHomeActivity(int numberOfEvents, CancellationToken cancellationToken) {
        var omgRefactor  = HomeAutomationTestData.SimulateHomeActivity(numberOfEvents);

        var request = new AppendRecordsRequest {
            Records = [],
            Checks  = [ConsistencyCheck.Stream(omgRefactor.Stream).DoesNotExist()]
        };

        var response = await Client.Streams.AppendRecords(request, cancellationToken: cancellationToken);

        Log.Debug(
            "Seeded SmartHomeActivity to stream {Stream} with {Count} events with revision {Revision} at position {Position}",
            request.Checks[0].AsStreamStateCheck.Stream, request.Records.Length, response.Revisions[0].Revision, response.Position);

        return new SeededSmartHomeActivity(omgRefactor, response.Revisions[0].Revision, response.Position);
    }

    public async ValueTask<SeededSmartHomeActivity> SeedSmartHomeActivity(CancellationToken cancellationToken) =>
        await SeedSmartHomeActivity(Random.Shared.Next(3, 13), cancellationToken);
}

public record SeededSmartHomeActivity(SmartHomeActivity Activity, Revision Revision, LogPosition Position) {
    public SmartHome                   Home          => Activity.Home;
    public StreamName                  Stream        => Activity.Stream;
    public ImmutableList<AppendRecord> Records       => Activity.Messages;
    public long                        LastTimestamp => Activity.LastTimestamp;

    public SmartHomeActivity SimulateMoreEvents(int? numberOfEvents = null) =>
        Activity.SimulateMoreEvents(numberOfEvents);

    public SmartHomeActivity WithExpectedRevision(long expectedRevision) =>
        Activity.WithExpectedRevision(expectedRevision);

    public static implicit operator AppendRecordsRequest(SeededSmartHomeActivity _) => _.Activity;
}

static class HomeAutomationTestData {
    public static SmartHomeActivity SimulateHomeActivity(int? numberOfEvents = null, long? startTime = null) =>
        SimulateHomeActivity(HomeAutomationDataSet.Default.Home(), numberOfEvents, startTime);

    public static SmartHomeActivity SimulateHomeActivity(SmartHome home, int? numberOfEvents = null, long? startTime = null) {
        var events = HomeAutomationDataSet.Default
            .Events(home, numberOfEvents ?? Random.Shared.Next(5, 15), startTime);

        var stream = $"{nameof(SmartHomeActivity)}-{home.Id}";

        var request = events
            .Aggregate(
                AppendRecordsRequest.ToStream(stream),
                (bldr, evt) => bldr.WithRecord(CreateRecord(evt, bldr.RecordsCount + 1)))
            .Build();

        var lastEventTs = request.Records.Last().Metadata
            .GetRequiredValue<long>("iot.evt.timestamp");

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

public record SmartHomeActivity(SmartHome Home, AppendRecordsRequest Request, long LastTimestamp) : IEnumerable<AppendRecord> {
    public StreamName                  Stream   { get; } = Request.Records[0].Stream;
    public ImmutableList<AppendRecord> Messages { get; } = Request.Records.ToImmutableList();

    public SmartHomeActivity SimulateMoreEvents(int? numberOfEvents = null) =>
        HomeAutomationTestData.SimulateHomeActivity(Home, numberOfEvents, LastTimestamp);

    public SmartHomeActivity WithExpectedRevision(long expectedRevision) =>
        this with {
            Request = Request with {
                Checks = [ConsistencyCheck.Stream(Stream).AtRevision(Revision.From(expectedRevision))]
            }
        };

    public static implicit operator AppendRecordsRequest(SmartHomeActivity _) => _.Request;

    public IEnumerator<AppendRecord> GetEnumerator() => Messages.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
