using Kurrent.Client.Registry;
using Kurrent.Client.Schema;
using Kurrent.Client.Schema.NameStrategies;
using Kurrent.Client.Schema.Serialization;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;
using Kurrent.Testkit.Datasets.ArcadeRacer.Model;

namespace Kurrent.Client.Tests.Streams;

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

    // Discovery is disabled because the Testkit catalog already maps the same contracts with
    // their default names, and the resolver union is 1:1 — a type cannot carry two schema
    // names. The isolated catalog is this client's entire vocabulary.
    static Action<KurrentClientOptions> ConfigureIsolatedClient(KurrentDBFixture fixture, ISchemaNameStrategy nameStrategy) =>
        opts => {
            opts.LoggerFactory = fixture.LoggerFactory;
            opts.Resilience    = KurrentClientResilienceOptions.NoResilience;
            opts.Messages.AutoUseCatalogs = false;
            opts.Messages.UseCatalog(NewIsolatedCatalog(nameStrategy));
            opts.Messages.Json = new() { SchemaRegistration = SchemaRegistrationOptions.AutoRegister };
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

        await Eventually(async () => {
            var schema  = await Fixture.Client.Registry.GetSchema(schemaName, ct);
            var version = await Fixture.Client.Registry.GetSchemaVersion(schemaName, schema.LatestSchemaVersion, ct);

            await Assert.That(schema.SchemaName).IsEqualTo(schemaName);
            await Assert.That(schema.Details.DataFormat).IsEqualTo(SchemaDataFormat.Json);
            await Assert.That(version.VersionNumber).IsEqualTo(1);
        }, ct);
    }

    [Test]
    public async ValueTask caches_first_registration(CancellationToken ct) {
        // Arrange
        var nameStrategy = NewIsolatedNameStrategy();

        await using var client = KurrentClient.Create(Fixture.ConnectionString, ConfigureIsolatedClient(Fixture, nameStrategy));

        var data = await Fixture.TestData.GenerateSet(ct);

        // Two records of the same CLR type so the second append hits the SchemaManager cache.
        var sameType = data.Records
            .Where(r => r.Value is LapCompleted)
            .Take(2)
            .ToArray();

        // Act
        await client.Streams.AppendStream(data.Stream, [sameType[0]], ct);

        await client.Streams.AppendStream(data.Stream, [sameType[1]], ct);

        // Assert
        var schemaName = nameStrategy.GenerateSchemaName(typeof(LapCompleted));

        await Eventually(async () => {
            var schema = await Fixture.Client.Registry.GetSchema(schemaName, ct);

            await Assert.That(schema.LatestSchemaVersion).IsEqualTo(1);
        }, ct);
    }

    [Test]
    public async ValueTask throws_when_type_is_unmapped(CancellationToken ct) {
        // Arrange
        await using var client = KurrentClient.Create(Fixture.ConnectionString, opts => {
            opts.LoggerFactory = Fixture.LoggerFactory;
            opts.Resilience    = KurrentClientResilienceOptions.NoResilience;
            // No catalogs at all: with discovery off and nothing mapped, every append must
            // fail to resolve a schema name — the exact behavior under test.
            opts.Messages.AutoUseCatalogs = false;
            opts.Messages.Json = new() { SchemaRegistration = SchemaRegistrationOptions.ManualMap };
        });

        var data = await Fixture.TestData.GenerateRecord(ct);

        // Act
        // The append-record mapper wraps the serializer's exception in an ArgumentException
        // ("Failed to map AppendRecord for stream ..."), so the registration error surfaces as the inner.
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            async () => await client.Streams.AppendStream(data.Stream, data.Records, ct));

        // Assert
        await Assert.That(ex?.InnerException).IsTypeOf<SchemaNameNotMappedException>();
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

        await Eventually(() => Fixture.Client.Registry.GetSchema(schemaName, ct), ct);

        await using var client = KurrentClient.Create(Fixture.ConnectionString, ConfigureIsolatedClient(Fixture, nameStrategy));

        // Act
        await client.Streams.AppendStream(data.Stream, data.Records, ct);

        // Assert
        // Auto-register must reuse the pre-existing schema rather than creating a new version.
        var schema = await Fixture.Client.Registry.GetSchema(schemaName, ct);
        await Assert.That(schema.LatestSchemaVersion).IsEqualTo(1);
    }
}
