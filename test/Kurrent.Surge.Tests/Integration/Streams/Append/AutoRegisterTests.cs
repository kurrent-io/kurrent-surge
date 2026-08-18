using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;
using Kurrent.Surge.Schema.Naming;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Tests.Fixtures;
using Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;
using Kurrent.Testkit.Datasets.ArcadeRacer.Model;

namespace Kurrent.Surge.Tests.Streams;

[Category("Streams")]
[Category("Integration")]
public class AutoRegisterTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    // Each test scopes its schemas to a unique namespace so the registry never sees a
    // collision across tests, which lets the assertion `LatestSchemaVersion == 1` stay
    // exclusive even on a shared container.
    static ISchemaNameStrategy NewIsolatedNameStrategy() =>
        SchemaNameStrategies.Namespace($"test-autoreg-{Guid.NewGuid():N}");

    // The simulation can emit any ArcadeRacer contract type, so the isolated catalog must
    // map them all — there is no auto-mapping; a client's vocabulary is always declared.
    static MessageCatalog NewIsolatedCatalog(ISchemaNameStrategy strategy) =>
        new MessageCatalog("autoreg-isolated")
            .Map<RaceCar>(strategy)
            .Map<GridFormed>(strategy)
            .Map<RaceStarted>(strategy)
            .Map<LapCompleted>(strategy)
            .Map<OvertakePerformed>(strategy)
            .Map<PitStopPerformed>(strategy)
            .Map<IncidentOccurred>(strategy)
            .Map<CarRetired>(strategy)
            .Map<WeatherChanged>(strategy)
            .Map<RaceFinished>(strategy)
            .Map<RaceInsight>(strategy);

    // This client opts into no discovered catalogs, so the Testkit catalog — which maps the same
    // contracts under their default names — stays out. The resolver union is 1:1 and a type cannot
    // carry two schema names, so the isolated catalog is this client's entire vocabulary.
    static Action<KurrentClientOptions> ConfigureIsolatedClient(KurrentDBFixture fixture, ISchemaNameStrategy nameStrategy) =>
        opts => {
            opts.LoggerFactory = fixture.LoggerFactory;
            opts.Resilience    = KurrentClientResilienceOptions.NoResilience;
            opts.Schema = KurrentClientSchemaOptions.AutoRegister;
            opts.Messages.UseCatalog(NewIsolatedCatalog(nameStrategy));
        };

    [Test]
    public async ValueTask registers_new_schema(CancellationToken ct) {
        // Arrange
        var nameStrategy = NewIsolatedNameStrategy();

        await using var client = KurrentClient.Create(Fixture.ConnectionString, ConfigureIsolatedClient(Fixture, nameStrategy));

        var data = await Fixture.TestData.GenerateRecord(ct);

        // Act
        await client.Streams.AppendStream(data.Stream, data.Records, ct);

        // Assert
        var schemaName = nameStrategy.GenerateSchemaName(data.Records[0].Value!.GetType());

        await Assert.That(async () => {
            var schema  = await Fixture.Client.Registry.GetSchema(schemaName, ct);
            var version = await Fixture.Client.Registry.GetSchemaVersion(schemaName, schema.LatestSchemaVersion, ct);

            await Assert.That(schema.SchemaName).IsEqualTo(schemaName);
            await Assert.That(schema.Details.DataFormat).IsEqualTo(SchemaDataFormat.Json);
            await Assert.That(version.VersionNumber).IsEqualTo(1);
        }).EventuallyPasses(RetryOnTransients(ct));
    }

    [Test]
    public async ValueTask caches_first_registration(CancellationToken ct) {
        // Arrange
        var nameStrategy = NewIsolatedNameStrategy();

        await using var client = KurrentClient.Create(Fixture.ConnectionString, ConfigureIsolatedClient(Fixture, nameStrategy));

        var data = await Fixture.TestData.GenerateSet(ct);

        // Two records of the same CLR type so the second append hits the governance cache.
        var sameType = data.Records
            .Where(r => r.Value is LapCompleted)
            .Take(2)
            .ToArray();

        // Act
        await client.Streams.AppendStream(data.Stream, [sameType[0]], ct);

        await client.Streams.AppendStream(data.Stream, [sameType[1]], ct);

        // Assert
        var schemaName = nameStrategy.GenerateSchemaName(typeof(LapCompleted));

        await Assert.That(async () => {
            var schema = await Fixture.Client.Registry.GetSchema(schemaName, ct);

            await Assert.That(schema.LatestSchemaVersion).IsEqualTo(1);
        }).EventuallyPasses(RetryOnTransients(ct));
    }

    [Test]
    public async ValueTask throws_when_type_is_unmapped(CancellationToken ct) {
        // Arrange
        await using var client = KurrentClient.Create(Fixture.ConnectionString, opts => {
            opts.LoggerFactory = Fixture.LoggerFactory;
            opts.Resilience    = KurrentClientResilienceOptions.NoResilience;
            // No catalogs at all: nothing is opted in and nothing is mapped, so every append must
            // fail to resolve a schema name — the exact behavior under test.
            opts.Schema = KurrentClientSchemaOptions.Disabled;
        });

        var data = await Fixture.TestData.GenerateRecord(ct);

        var expectedMessageType = data.Records.First().Value!.GetType();

        // Act
        var ex = await Assert.ThrowsAsync<SchemaNameNotMappedException>(
            async () => await client.Streams.AppendStream(data.Stream, data.Records, ct));

        // Assert
        await Assert.That(ex?.MessageType).IsEqualTo(expectedMessageType);
    }

    [Test]
    public async ValueTask reuses_existing_schema(CancellationToken ct) {
        // Arrange
        var nameStrategy = NewIsolatedNameStrategy();
        var data         = await Fixture.TestData.GenerateRecord(ct);
        var schemaName   = nameStrategy.GenerateSchemaName(data.Records.First().Value!.GetType());

        await Fixture.Client.Registry.CreateSchema(
            schemaName,
            KurrentDBFixture.NewJsonSchemaDefinition().ToJson(),
            SchemaDataFormat.Json,
            ct);

        await Assert.That(async () => { await Fixture.Client.Registry.GetSchema(schemaName, ct); }).EventuallyPasses(RetryOnTransients(ct));

        await using var client = KurrentClient.Create(Fixture.ConnectionString, ConfigureIsolatedClient(Fixture, nameStrategy));

        // Act
        await client.Streams.AppendStream(data.Stream, data.Records, ct);

        // Assert
        // Auto-register must reuse the pre-existing schema rather than creating a new version.
        var schema = await Fixture.Client.Registry.GetSchema(schemaName, ct);
        await Assert.That(schema.LatestSchemaVersion).IsEqualTo(1);
    }
}
