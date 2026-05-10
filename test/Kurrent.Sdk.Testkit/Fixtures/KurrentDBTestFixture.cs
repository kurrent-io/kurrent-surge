using System.Runtime.CompilerServices;
using Bogus;
using Kurrent.Client;
using Kurrent.Client.Schema;
using Kurrent.Client.Streams;
using Kurrent.Sdk.Testkit.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testing.ArcadeRacer;

namespace Kurrent.Sdk.Testkit.Fixtures;

public class KurrentDBTestFixtureV1 : AspireFixture {
    protected KurrentDBSettings Settings { get; init; } = KurrentDBSettings.SingleNode;
    
    public KurrentDBTestFixtureV1() {
        Options = new() {
            StartupTimeout        = TimeSpan.FromMinutes(1),
            ShouldWaitForResource = r => r is KurrentDBResource
        };

        ConfigureBuilder = builder => {
            builder.AddKurrentDB(KurrentDBResource.DefaultResourceName, Settings);
        };

        OnStarted = async () => {
            var db = AppModel.Resources.OfType<KurrentDBResource>()
                .First(r => r.Name == KurrentDBResource.DefaultResourceName);

            ConnectionString = await db.ConnectionStringExpression.GetValueAsync(CancellationToken.None)
                                     ?? throw new InvalidOperationException("Connection string was null.");

            CertsDirectory = db.CertsHostPath;

            Client = GetNewClient(ConnectionString);
        };
        
        OnTeardown = async () => {
            if (Client is { } client)
                await client.DisposeAsync();
        };
    }

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    [ClassDataSource<ArcadeRacerTestDataSource>(Shared = SharedType.PerTestSession)]
    public required ITestDataSource TestData { get; [UsedImplicitly] init; }

    public KurrentClient Client { get; private set; } = null!;

    public string ConnectionString { get; private set; } = null!;

    public string? CertsDirectory { get; private set; }

    public MessageTypeMapper TypeMapper => Client.Options.Schema.Mapper;

    public KurrentClient GetNewClient(string? connectionString = null) =>
        KurrentClient.Create(connectionString ?? ConnectionString, opts => {
            opts.LoggerFactory      = App.Services.GetRequiredService<ILoggerFactory>();
            opts.Schema.PreValidate = false;
            opts.Resilience         = KurrentClientResilienceOptions.NoResilience;
        });

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


public class KurrentDBTestFixture : AspireFixture<Program> {
    protected KurrentDBSettings Settings { get; init; } = KurrentDBSettings.SingleNode;

    public KurrentDBTestFixture() {
        Options = new() {
            StartupTimeout        = TimeSpan.FromMinutes(1),
            ShouldWaitForResource = r => r is KurrentDBResource
        };

        OnStarted = async () => {
            var db = AppModel.Resources.OfType<KurrentDBResource>()
                .First(r => r.Name == KurrentDBResource.DefaultResourceName);

            ConnectionString = await db.ConnectionStringExpression.GetValueAsync(CancellationToken.None)
                                     ?? throw new InvalidOperationException("Connection string was null.");

            CertsDirectory = db.CertsHostPath;

            Client = GetNewClient(ConnectionString);
        };

        OnTeardown = async () => {
            if (Client is { } client)
                await client.DisposeAsync();
        };
    }
    
    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    [ClassDataSource<ArcadeRacerTestDataSource>(Shared = SharedType.PerTestSession)]
    public required ITestDataSource TestData { get; [UsedImplicitly] init; }
    
    public KurrentClient Client { get; private set; } = null!;
    
    public string ConnectionString { get; private set; } = null!;

    public string? CertsDirectory { get; private set; }

    public MessageTypeMapper TypeMapper => Client.Options.Schema.Mapper;

    public KurrentClient GetNewClient(string? connectionString = null) =>
        KurrentClient.Create(connectionString ?? ConnectionString, opts => {
            opts.LoggerFactory      = App.Services.GetRequiredService<ILoggerFactory>();
            opts.Schema.PreValidate = false;
            opts.Resilience         = KurrentClientResilienceOptions.NoResilience;
        });

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



//
// public class KurrentDBTestFixture : AspireFixture<Projects.Kurrent_Sdk_Testkit_AppHost> {
//     
//     [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
//     public required BogusFaker Faker { get; [UsedImplicitly] init; }
//
//     [ClassDataSource<ArcadeRacerTestDataSource>(Shared = SharedType.PerTestSession)]
//     public required ITestDataSource TestData { get; [UsedImplicitly] init; }
//
//     public ILogger Logger => TestkitExecutionContext.Logger;
//
//     public ILoggerFactory LoggerFactory => TestkitExecutionContext.LoggerFactory;
//
//     public FakeTimeProvider Time { get; } = new();
//
//     public KurrentClient Client { get; private set; } = null!;
//
//     public string ConnectionString { get; private set; } = "";
//
//     public string AnonymousConnectionString { get; private set; } = "";
//
//     public string? CertsDirectory { get; private set; }
//
//     public MessageTypeMapper TypeMapper => Client.Options.Schema.Mapper;
//
//     
//     #region ->> Aspire Configuration <<-
//     
//     protected override TimeSpan ResourceTimeout => TimeSpan.FromMinutes(2);
//
//     protected override ResourceWaitBehavior WaitBehavior => ResourceWaitBehavior.Named;
//     
//     protected override void LogProgress(string message) =>
//         Logger.LogInformation("[Aspire] {Message}", message);
//
//     protected override void ConfigureBuilder(IDistributedApplicationTestingBuilder builder) =>
//         builder.Services.AddSingleton(LoggerFactory);
//
//     protected override IEnumerable<string> ResourcesToWaitFor() =>
//         App.Services
//             .GetRequiredService<DistributedApplicationModel>().Resources
//             // .OfType<KurrentDBResource>()
//             .Select(resource => resource.Name);
//
//     #endregion // Aspire Configuration
//     
//     public override async Task InitializeAsync() {
//         await base.InitializeAsync();
//
//         var model = App.Services.GetRequiredService<DistributedApplicationModel>();
//         
//         var resource = model.Resources
//             .OfType<KurrentDBResource>()
//             .First(resource => resource.Name == KurrentDBAppHostArgs.DefaultResourceName);
//
//         ConnectionString = await resource.AuthenticatedConnectionStringExpression.GetValueAsync(CancellationToken.None)
//                         ?? throw new InvalidOperationException("Authenticated connection string was null.");
//
//         AnonymousConnectionString = await resource.ConnectionStringExpression.GetValueAsync(CancellationToken.None)
//                         ?? throw new InvalidOperationException("Anonymous connection string was null.");
//
//         CertsDirectory = resource.CertsHostPath;
//
//         Client = GetNewClient();
//     }
//
//     public KurrentClient GetNewClient() =>
//         KurrentClient.Create(ConnectionString, opts => {
//             opts.LoggerFactory      = LoggerFactory;
//             opts.Schema.PreValidate = false;
//             opts.Resilience         = KurrentClientResilienceOptions.NoResilience;
//         });
//
//     public override async ValueTask DisposeAsync() {
//         if (Client is { } client)
//             await client.DisposeAsync();
//
//         await base.DisposeAsync();
//
//         if (CertsDirectory is { } dir)
//             Directory.Delete(dir, true);
//     }
//
//     public async ValueTask<RecordPosition> LatestRecordPosition(CancellationToken ct = default) {
//         var request = new ReadRecordsRequest {
//             Direction    = ReadDirection.Backwards,
//             Start        = LogPosition.Latest,
//             Limit        = 1,
//             SkipDecoding = true
//         };
//
//         return await Client.Streams
//             .ReadRecords(request, ct)
//             .Select(x => x.AsRecord.Position)
//             .FirstAsync(ct)
//             .ConfigureAwait(false);
//     }
//
//     public async ValueTask<SeededTestStream> SeedTestStream(
//         int? numberOfEvents = null,
//         CancellationToken ct = default
//     ) {
//         var ds = numberOfEvents is null
//             ? await TestData.GenerateSet(ct).ConfigureAwait(false)
//             : await TestData.GenerateSet(numberOfEvents.Value, ct).ConfigureAwait(false);
//
//         var req = AppendStreamRequest.For(ds.Stream).WithRecords(ds.Records).Build();
//         var rsp = await Client.Streams.AppendStream(req, ct).ConfigureAwait(false);
//
//         var seeded = new SeededTestStream(ds, rsp.Revision, rsp.Position);
//
//         Logger.LogDebug(
//             "Seeded {Count} {DataSource} events: {Stream} v{Revision} @ {Position}",
//             seeded.Records.Length, TestData.Name, seeded.Stream, seeded.Revision.Value, seeded.Position.Value);
//
//         return seeded;
//     }
//
//     public ValueTask<SeededTestStream> SeedTestStream(CancellationToken ct) => SeedTestStream(null, ct);
//
//     public async IAsyncEnumerable<SeededTestStream> TestStreams([EnumeratorCancellation] CancellationToken ct) {
//         while (!ct.IsCancellationRequested) {
//             SeededTestStream seededStream;
//             try {
//                 seededStream = await SeedTestStream(ct).ConfigureAwait(false);
//             }
//             catch (OperationCanceledException) {
//                 yield break;
//             }
//             yield return seededStream;
//         }
//     }
//
//     public ValueTask<List<SeededTestStream>> SeedManyTestStreams(int numberOfStreams, CancellationToken ct) =>
//         TestStreams(ct).Take(numberOfStreams).ToListAsync(ct);
// }


public sealed class KurrentDBSecureTestFixture : KurrentDBTestFixture {
    public KurrentDBSecureTestFixture() {
        // Options = new() {
        //     ResourceTimeout    = TimeSpan.FromMinutes(2),
        //     WaitBehavior       = ResourceWaitBehavior.Named,
        //     ResourcesToWaitFor = r => r.OfType<KurrentDBResource>().ToList<IResource>(),
        //     Args = new[] { KurrentDBAppHostArgs.Tls(true) }
        // };
        
        // Settings = new KurrentDBSettings { Secure = true, };
    }
    
    // static readonly string[] DefaultArgs = [KurrentDBAppHostArgs.Tls(true)];
    //
    // protected override string[] Args => DefaultArgs;
}

/// <summary>
/// Three-node cluster + one replica with TLS enabled.
/// </summary>
public sealed class KurrentDBClusterTestFixture : KurrentDBTestFixture { 
    KurrentDBClusterTestFixture() =>
        Settings = KurrentDBSettings.DefaultCluster;
}
