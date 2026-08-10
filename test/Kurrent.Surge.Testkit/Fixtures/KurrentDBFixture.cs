using System.Runtime.CompilerServices;
using Bogus;
using Kurrent.Surge.Client;
using Kurrent.Surge.Schema.Catalogs;
using Kurrent.Surge.Client.Streams;
using Aspire.Hosting.KurrentDB;
using Kurrent.Testkit.Aspire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Testing.ArcadeRacer;

namespace Kurrent.Surge.Testkit;

public class KurrentDBFixture : AspireFixture {
    protected virtual string ResourceName => KurrentDBServerResource.DefaultResourceName;

    public KurrentDBFixture() {
        Options = Options with {
            ShouldWaitForResource = r => r is KurrentDBResource
        };

        ConfigureBuilder = builder => {
            var settings = builder.Configuration.GetSection("KurrentDB")
                .Get<KurrentDBSettings>(opts => opts.BindNonPublicProperties = true) 
                        ?? new KurrentDBSettings();

            ConfigureSettings(settings);

            // The license key is a secret the server reads from its native env var, so source it from
            // the environment when it wasn't supplied via KurrentDB:LicenseKey configuration. AddKurrentDB
            // then models it as a secret ParameterResource.
            settings.LicenseKey ??= Environment.GetEnvironmentVariable("KURRENTDB_LICENSING__LICENSE_KEY");

            builder.AddKurrentDB(ResourceName, settings);

            builder.Services
                .AddSingleton(Time = new FakeTimeProvider(DateTimeOffset.UtcNow))
                .AddSingleton<TimeProvider>(sp => sp.GetRequiredService<FakeTimeProvider>());
        };

        AfterStart = async () => {
            ConnectionString = await AppModel
                .GetRequiredConnectionString<KurrentDBServerResource>(ResourceName);

            AnonymousConnectionString = KurrentClientOptions.Parse(ConnectionString)
                .With(opts => opts.Security = KurrentClientSecurityOptions.Tls)
                .ToString();

            Client = GetNewClient(ConnectionString);
        };

        BeforeStop = async () => {
            if (Client is { } client)
                await client.DisposeAsync();
        };
    }
    
    protected virtual void ConfigureSettings(KurrentDBSettings settings) { }

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    [ClassDataSource<ArcadeRacerTestDataSource>(Shared = SharedType.PerTestSession)]
    public required ITestDataSource TestData { get; [UsedImplicitly] init; }
    
    /// <summary>
    /// The <see cref="FakeTimeProvider"/> instance that allows tests to manipulate
    /// time within the Aspire application.
    /// </summary>
    public FakeTimeProvider Time { get; private set; } = null!;
    
    public KurrentClient Client { get; private set; } = null!;

    public string ConnectionString { get; private set; } = null!;

    public string AnonymousConnectionString { get; private set; } = null!;
    
    /// <summary>
    /// The fixture client's default catalog. Note: mappings added here only affect clients
    /// created AFTER the change — resolvers snapshot catalogs at client construction.
    /// </summary>
    public MessageCatalog Catalog => Client.Options.Messages.Catalog;

    public KurrentClient GetNewClient(string connectionString, Action<KurrentClientOptions>? configure = null) =>
        KurrentClient.Create(connectionString, opts => {
            opts.LoggerFactory = LoggerFactory;
            opts.Schema        = KurrentClientSchemaOptions.NoValidation;
            opts.Resilience    = KurrentClientResilienceOptions.NoResilience;
            // The Testkit catalog (race contracts) is discovered but not always-active, so it must
            // be opted in explicitly — every fixture client speaks the test vocabulary by default.
            opts.Messages.UseTestkitCatalog();
            configure?.Invoke(opts);
        });
    
    public KurrentClient GetNewClient(Action<KurrentClientOptions> configure) =>
        GetNewClient(ConnectionString, configure);
    
    public KurrentClient GetNewClient() =>
        GetNewClient(ConnectionString);
    
    public KurrentClient GetNewAnonymousClient() =>
        GetNewClient(AnonymousConnectionString);

    public async ValueTask<RecordPosition> LatestRecordPosition(CancellationToken ct = default) {
        var options = new ReadRecordsOptions {
            Direction    = ReadDirection.Backwards,
            Start        = LogPosition.Latest,
            Limit        = 1,
            SkipDecoding = true
        };

        return await Client.Streams
            .ReadRecords(options, ct)
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

        var rsp = await Client.Streams.AppendStream(ds.Stream, ds.Records, ct).ConfigureAwait(false);

        var seeded = new SeededTestStream(ds, rsp.Revision, rsp.Position.LogPosition);

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

public sealed class KurrentDBInsecureFixture : KurrentDBFixture {
    protected override void ConfigureSettings(KurrentDBSettings settings) =>
        settings.TlsEnabled = false;
}


/// <summary>
/// Ephemeral single-node fixture for destructive operations like scavenge.
/// Uses session-scoped containers that are not reused across test runs.
/// </summary>
public sealed class KurrentDBScavengeFixture : KurrentDBFixture {
    protected override string ResourceName { get; } = $"kdb-{Guid.NewGuid().ToString("N")[..8]}";

    protected override void ConfigureSettings(KurrentDBSettings settings) =>
        settings.Lifetime = ContainerLifetime.Session;
}

/// <summary>
/// Three-node cluster + one replica with TLS enabled.
/// </summary>
public sealed class KurrentDBClusterFixture : KurrentDBFixture {
    // Distinct resource name: the certs directory is keyed by it, and the cluster needs
    // its own certificate set (SANs covering every node name) without touching the
    // material the persistent single-node containers already loaded.
    protected override string ResourceName => "kdb-cluster";

    protected override void ConfigureSettings(KurrentDBSettings settings) {
        settings.Lifetime   = ContainerLifetime.Persistent;
        settings.Topology   = new() { Nodes = 3, Replicas = 1 };
        settings.TlsEnabled = true;
    }
}
