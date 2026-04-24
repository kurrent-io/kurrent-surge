// ReSharper disable ArrangeTypeMemberModifiers

using System.Runtime.CompilerServices;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Bogus;
using Kurrent.Client;
using Kurrent.Client.Schema;
using Kurrent.Client.Streams;
using Kurrent.Testing.Containers.KurrentDB;
using Kurrent.Testing.Testkit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Testing.ArcadeRacer;
using TUnit.Core.Interfaces;
using TUnit.Core.Services;

namespace Sdk.TestKit.Fixtures;

public sealed class KurrentClientAspireTestFixture : IAsyncInitializer, IAsyncDisposable {
    
    DistributedApplication? _app;

    
    
    [ClassDataSource<KurrentDBTestContainer>(Shared = SharedType.PerTestSession)]
    public required KurrentDBTestContainer KurrentDB { get; [UsedImplicitly] init; }

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    [ClassDataSource<ArcadeRacerTestDataSource>(Shared = SharedType.PerTestSession)]
    public required ITestDataSource TestData { get; [UsedImplicitly] init; }

    /// <summary>
    /// The logger used for logging in tests.
    /// </summary>
    public ILogger Logger => TestkitExecutionContext.Logger;

    /// <summary>
    /// The logger factory used for creating loggers in tests.
    /// </summary>
    public ILoggerFactory LoggerFactory => TestkitExecutionContext.LoggerFactory;

    /// <summary>
    /// Time provider used for simulating and controlling time in tests.
    /// </summary>
    public FakeTimeProvider Time { get; } = new FakeTimeProvider();

    /// <summary>
    /// The Kurrent client used for testing.
    /// </summary>
    public KurrentClient Client => LazyClient.Value;

    /// <summary>
    /// The message type mapper for the test client.
    /// </summary>
    public MessageTypeMapper TypeMapper => Client.Options.Schema.Mapper;

    Lazy<KurrentClient> LazyClient { get; set; } = null!;

    public Task InitializeAsync() {
        LazyClient = new Lazy<KurrentClient>(() => {
            var opts = KurrentClientOptions.Parse(KurrentDB.AuthenticatedConnectionString);
            opts.LoggerFactory      = TestkitExecutionContext.LoggerFactory;
            opts.Schema.PreValidate = false;
            opts.Schema.Mapper      = MessageTypeMapper.Create(m => m.Map<StreamMetadata>("$metadata"));
            opts.Resilience         = KurrentClientResilienceOptions.NoResilience;
            return KurrentClient.Create(opts);
        });

        if(!KurrentDB.Enabled) {
            Logger.LogWarning(
                "KurrentDB container is disabled. Expecting localhost:2113 to be available. Connection string: {ConnectionString}",
                KurrentDB.AuthenticatedConnectionString);
        }

        return Task.CompletedTask;
    }
    
    
     async Task AspireAttemptToInitializeAsync() {
        var builder =  DistributedApplicationTestingBuilder.Create([], (options, settings) => {

        });

        _app = await builder.BuildAsync();

        var notifications = _app.Services.GetRequiredService<ResourceNotificationService>();

        await _app.StartAsync();

        // Wait for health only when Aspire manages the container.
        // When using an existing instance via connection string, the resource
        // is a reference with no health endpoint on the Aspire side.
        try {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await notifications.WaitForResourceHealthyAsync("kurrentdb", cts.Token);
        } catch (InvalidOperationException) {
            // Resource is a connection string reference; no health check needed.
        } catch (OperationCanceledException ex) {
            throw new TimeoutException("KurrentDB did not become healthy within 2 minutes.", ex);
        }

        var baseConnectionString = await _app.GetConnectionStringAsync("kurrentdb")
                                ?? throw new InvalidOperationException("Failed to get KurrentDB connection string.");

        // // Aspire's connection string lacks credentials; inject default admin credentials.
        // var uri = new Uri(baseConnectionString);
        // ConnectionString = $"{uri.Scheme}://admin:changeit@{uri.Host}:{uri.Port}{uri.PathAndQuery}";
        //
        // Client = KurrentClientOptions.Parse(ConnectionString).GetBuilder()
        //     .WithLoggerFactory(new TestkitLoggerFactory())
        //     .ConfigureSchema(schema => schema
        //         .WithValidation(false)
        //         .ConfigureMessages(msg => msg.Map<StreamMetadata>("$metadata")))
        //     .ConfigureResilience(KurrentClientResilienceOptions.NoResilience)
        //     .CreateClient();
    }

    public async ValueTask DisposeAsync() {
        if (LazyClient is not { IsValueCreated: true })
            return;

        try {
            await LazyClient.Value.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Failed to dispose of Client");
            throw;
        }
        
        if (_app is not null) {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    public KurrentClient GetNewClient() {
        var opts = KurrentClientOptions.Parse(KurrentDB.AuthenticatedConnectionString);
        opts.LoggerFactory      = TestkitExecutionContext.LoggerFactory;
        opts.Schema.PreValidate = false;
        opts.Schema.Mapper      = MessageTypeMapper.Create(m => m.Map<StreamMetadata>("$metadata"));
        opts.Resilience         = KurrentClientResilienceOptions.NoResilience;
        return KurrentClient.Create(opts);
    }

    public async ValueTask<RecordPosition> LatestRecordPosition(CancellationToken ct = default) {
        var request = new ReadRecordsRequest {
            Direction    = ReadDirection.Backwards,
            Start        = LogPosition.Latest,
            Limit        = 1,
            SkipDecoding = true
        };

        return await Client.Streams
            .ReadRecords(request, ct)
            .Select(x => x.AsRecord.Position)
            .FirstAsync(ct)
            .ConfigureAwait(false);
    }

    public async ValueTask<SeededTestStream> SeedTestStream(
        int? numberOfEvents = null,
        CancellationToken ct = default
    ) {
        var ds = numberOfEvents is null
            ? await TestData.GenerateSet(ct).ConfigureAwait(false)
            : await TestData.GenerateSet(numberOfEvents.Value, ct).ConfigureAwait(false);

        var req = AppendStreamRequest.For(ds.Stream).WithRecords(ds.Records).Build();
        var rsp = await Client.Streams.AppendStream(req, ct).ConfigureAwait(false);

        var seeded = new SeededTestStream(ds, rsp.Revision, rsp.Position);

        Logger.LogDebug(
            "Seeded {Count} {DataSource} events: {Stream} v{Revision} @ {Position}",
            seeded.Records.Length, TestData.Name, seeded.Stream, seeded.Revision.Value, seeded.Position.Value);

        return seeded;
    }

    public ValueTask<SeededTestStream> SeedTestStream(CancellationToken ct) => SeedTestStream(null, ct);

    public async IAsyncEnumerable<SeededTestStream> TestStreams([EnumeratorCancellation] CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            SeededTestStream seededStream;
            try {
                seededStream = await SeedTestStream(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                yield break;
            }
            yield return seededStream;
        }
    }

    public ValueTask<List<SeededTestStream>> SeedManyTestStreams(int numberOfStreams, CancellationToken ct) =>
        TestStreams(ct).Take(numberOfStreams).ToListAsync(ct);
}
