using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;
using Kurrent.Surge.Schema.Naming;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Tests.Fixtures;
using Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;
using Kurrent.Testkit.Datasets.ArcadeRacer.Model;
using NJsonSchema;

namespace Kurrent.Surge.Tests.Streams;

[Category("Streams")]
[Category("Integration")]
[Timeout(30_000)]
public class SchemaGovernanceTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    // Each test scopes its schemas to a unique namespace so the registry never sees a
    // collision across tests, mirroring AutoRegisterTests' isolation idiom.
    static ISchemaNameStrategy NewIsolatedNameStrategy() =>
        SchemaNameStrategies.Namespace($"test-governance-{Guid.NewGuid():N}");

    static MessageCatalog NewIsolatedCatalog(ISchemaNameStrategy strategy) =>
        new MessageCatalog("governance-isolated")
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

    // registration: null keeps the framework default (Manual/Disabled — lax, no registry contact).
    static Action<KurrentClientOptions> ConfigureIsolatedClient(
        KurrentDBFixture fixture, ISchemaNameStrategy nameStrategy, SchemaRegistrationOptions? registration = null
    ) =>
        ConfigureClientWithCatalog(fixture, NewIsolatedCatalog(nameStrategy), registration);

    static Action<KurrentClientOptions> ConfigureClientWithCatalog(
        KurrentDBFixture fixture, MessageCatalog catalog, SchemaRegistrationOptions? registration = null
    ) =>
        opts => {
            opts.LoggerFactory = fixture.LoggerFactory;
            opts.Resilience    = KurrentClientResilienceOptions.NoResilience;
            opts.Messages.UseCatalog(catalog);

            if (registration is not null)
                opts.Messages.Json = new() { SchemaRegistration = registration };
        };

    // A definition that structurally conflicts with whatever the client itself would export for
    // messageType: an extra required field breaks Backward compatibility deterministically
    // (proven by CheckSchemaCompatibilityTests.check_schema_compatibility_backward_is_incompatible).
    static async ValueTask<string> NewConflictingDefinition(Type messageType) {
        var exported = await JsonSchema.FromJsonAsync(NJsonSchemaExporter.Instance.Export(messageType, SchemaDataFormat.Json));
        return exported.AddRequired("governance-test-conflict", JsonObjectType.String).ToJson();
    }

    [Test]
    public async ValueTask walk_registered_schema_version_id_lands_on_the_wire(CancellationToken ct) {
        // Arrange
        var nameStrategy = NewIsolatedNameStrategy();

        await using var client = KurrentClient.Create(
            Fixture.ConnectionString, ConfigureIsolatedClient(Fixture, nameStrategy, SchemaRegistrationOptions.AutoRegister));

        var data        = await Fixture.TestData.GenerateRecord(ct);
        var messageType = data.Records[0].Value!.GetType();
        var schemaName  = nameStrategy.GenerateSchemaName(messageType);

        // Act — the walk auto-registers the type on the back of this call; GovernWriteStep reads
        // the version id out of the walk's results and FrameWriteStep puts it on the wire.
        await client.Streams.AppendStream(data.Stream, data.Records, ct);

        var raw = await Fixture.Client.Streams
            .ReadStream(data.Stream, new ReadStreamOptions { SkipDecoding = true }, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        // Assert — the wire schema info carries the exact version id the registry assigned.
        await Assert.That(async () => {
            var schema  = await Fixture.Client.Registry.GetSchema(schemaName, ct);
            var version = await Fixture.Client.Registry.GetSchemaVersion(schemaName, schema.LatestSchemaVersion, ct);

            await Assert.That(raw[0].Schema.Schema).IsEqualTo(schemaName);
            await Assert.That(raw[0].Schema.HasSchemaId).IsTrue();
            await Assert.That(raw[0].Schema.VersionId).IsEqualTo(version.VersionId);
        }).EventuallyPasses(RetryOnTransients(ct));
    }

    [Test]
    public async ValueTask validate_only_client_with_unregistered_schema_fails_the_first_governed_append(CancellationToken ct) {
        // Arrange — a validate-only client whose isolated schema names were never registered.
        var nameStrategy = NewIsolatedNameStrategy();

        await using var client = KurrentClient.Create(
            Fixture.ConnectionString, ConfigureIsolatedClient(Fixture, nameStrategy, SchemaRegistrationOptions.ManualRegister));

        var data = await Fixture.TestData.GenerateRecord(ct);

        // Act — no registry call exists on the write path itself: the walk validates the catalog
        // on the back of this call, and its stored fault rethrows here, at the first governed record.
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            async () => await client.Streams.AppendStream(data.Stream, data.Records, ct));

        // Assert — WriteRecordPipeline.Execute's mapping wrap around the walk's own wrap:
        // ArgumentException -> SchemaSerializationFailedException -> SchemaNotFoundException.
        await Assert.That(ex?.InnerException).IsTypeOf<SchemaSerializationFailedException>();
        await Assert.That(ex?.InnerException?.InnerException).IsTypeOf<SchemaNotFoundException>();
    }

    [Test]
    public async ValueTask validate_only_client_fails_the_append_when_the_registered_schema_is_incompatible(CancellationToken ct) {
        // Arrange — pre-register a conflicting definition under the exact name the client resolves.
        // The client's catalog maps ONLY this one type: the walk validates every catalog entry it
        // sees, and the other 10 ArcadeRacer types would surface as unrelated SchemaNotFoundException
        // failures (nothing else is pre-registered) if they shared the isolated 11-type catalog.
        var nameStrategy = NewIsolatedNameStrategy();
        var data         = await Fixture.TestData.GenerateRecord(ct);
        var messageType  = data.Records[0].Value!.GetType();
        var schemaName   = nameStrategy.GenerateSchemaName(messageType);
        var catalog      = new MessageCatalog("governance-single-type").Map(schemaName, messageType);

        await Fixture.Client.Registry.CreateSchema(
            schemaName, await NewConflictingDefinition(messageType), SchemaDataFormat.Json, CompatibilityMode.Backward, ct);

        await Assert.That(async () => { await Fixture.Client.Registry.GetSchema(schemaName, ct); }).EventuallyPasses(RetryOnTransients(ct));

        await using var client = KurrentClient.Create(
            Fixture.ConnectionString, ConfigureClientWithCatalog(Fixture, catalog, SchemaRegistrationOptions.ManualRegister));

        // Act — same walk-fault-at-append surface as the unregistered case, but the registry
        // check now fails on incompatibility rather than absence.
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            async () => await client.Streams.AppendStream(data.Stream, data.Records, ct));

        // Assert — SchemaValidationException is not itself a SchemaSerializationException, so
        // EnsureRegistered's catch-all wraps it exactly like the not-found case above.
        await Assert.That(ex?.InnerException).IsTypeOf<SchemaSerializationFailedException>();
        await Assert.That(ex?.InnerException?.InnerException).IsTypeOf<SchemaValidationException>();
    }

    [Test]
    public async ValueTask validating_client_decodes_a_record_with_a_registered_compatible_schema(CancellationToken ct) {
        // Arrange — one isolated namespace shared by both clients' catalogs.
        var nameStrategy = NewIsolatedNameStrategy();

        await using var producer = KurrentClient.Create(
            Fixture.ConnectionString, ConfigureIsolatedClient(Fixture, nameStrategy, SchemaRegistrationOptions.AutoRegister));

        await using var consumer = KurrentClient.Create(
            Fixture.ConnectionString, ConfigureIsolatedClient(Fixture, nameStrategy, SchemaRegistrationOptions.ManualRegister));

        var data = await Fixture.TestData.GenerateRecord(ct);

        // Act — the producer registers and frames with a version id; the consumer decodes by version.
        await producer.Streams.AppendStream(data.Stream, data.Records, ct);

        // Assert — the consumer's compatibility check reads the registry projection, which
        // catches up to the producer's registration eventually, not immediately.
        await Assert.That(async () => {
            var read = await consumer.Streams
                .ReadStream(data.Stream, ct)
                .Where(msg => msg.IsRecord)
                .Select(msg => msg.AsRecord)
                .ToListAsync(ct);

            await Assert.That(read).Count().IsEqualTo(1);
            await Assert.That(read[0].IsDecoded).IsTrue();
            await Assert.That(read[0].Value).IsEquivalentTo(data.Records[0].Value);
        }).EventuallyPasses(RetryOnTransients(ct));
    }

    [Test]
    public async ValueTask validating_client_fails_the_decode_when_the_registered_schema_is_incompatible(CancellationToken ct) {
        // Arrange — a plain (lax) producer frames the record by name only, no registry contact.
        var nameStrategy = NewIsolatedNameStrategy();

        await using var producer = KurrentClient.Create(
            Fixture.ConnectionString, ConfigureIsolatedClient(Fixture, nameStrategy, SchemaRegistrationOptions.ManualMap));

        var data        = await Fixture.TestData.GenerateRecord(ct);
        var messageType = data.Records[0].Value!.GetType();
        var schemaName  = nameStrategy.GenerateSchemaName(messageType);

        await producer.Streams.AppendStream(data.Stream, data.Records, ct);

        await Fixture.Client.Registry.CreateSchema(
            schemaName, await NewConflictingDefinition(messageType), SchemaDataFormat.Json, CompatibilityMode.Backward, ct);

        await Assert.That(async () => { await Fixture.Client.Registry.GetSchema(schemaName, ct); }).EventuallyPasses(RetryOnTransients(ct));

        await using var consumer = KurrentClient.Create(
            Fixture.ConnectionString, ConfigureIsolatedClient(Fixture, nameStrategy, SchemaRegistrationOptions.ManualRegister));

        // Act — unlike the write path, GovernReadStep calls EnsureCompatible directly, per
        // record, with no walk/interceptor involved: no schema id on the wire means compatibility
        // is checked by name.
        var ex = await Assert.ThrowsAsync<SchemaDeserializationFailedException>(
            async () => await consumer.Streams.ReadStream(data.Stream, ct).Where(msg => msg.IsRecord).ToListAsync(ct));

        // Assert — EnsureCompatibleByName wraps SchemaIncompatibleException as SchemaValidationException;
        // EnsureCompatible's own catch wraps that again as SchemaDeserializationFailedException.
        await Assert.That(ex?.InnerException).IsTypeOf<SchemaValidationException>();
    }

    [Test]
    public async ValueTask validating_client_fails_the_read_for_an_unknown_schema_name(CancellationToken ct) {
        // Arrange — producer and consumer map the same contract types under disjoint namespaces,
        // so the consumer's resolver never recognizes the producer's wire schema name.
        var producerNames = NewIsolatedNameStrategy();
        var consumerNames = NewIsolatedNameStrategy();

        await using var producer = KurrentClient.Create(
            Fixture.ConnectionString, ConfigureIsolatedClient(Fixture, producerNames, SchemaRegistrationOptions.ManualMap));

        await using var consumer = KurrentClient.Create(
            Fixture.ConnectionString, ConfigureIsolatedClient(Fixture, consumerNames, SchemaRegistrationOptions.ManualRegister));

        var data = await Fixture.TestData.GenerateRecord(ct);

        await producer.Streams.AppendStream(data.Stream, data.Records, ct);

        // Act & Assert — ResolveTypeReadStep throws directly under a validating policy: an
        // unresolved name is never a per-operation "unknown type" outcome for this client.
        await Assert.ThrowsAsync<MessageTypeResolutionException>(
            async () => await consumer.Streams.ReadStream(data.Stream, ct).Where(msg => msg.IsRecord).ToListAsync(ct));
    }

    [Test]
    public async ValueTask lax_client_passes_an_unknown_schema_record_through_undecoded(CancellationToken ct) {
        // Arrange — same disjoint-namespace setup, but the consumer keeps the lax default policy.
        var producerNames = NewIsolatedNameStrategy();
        var consumerNames = NewIsolatedNameStrategy();

        await using var producer = KurrentClient.Create(
            Fixture.ConnectionString, ConfigureIsolatedClient(Fixture, producerNames, SchemaRegistrationOptions.ManualMap));

        await using var consumer = KurrentClient.Create(
            Fixture.ConnectionString, ConfigureIsolatedClient(Fixture, consumerNames));

        var data = await Fixture.TestData.GenerateRecord(ct);

        await producer.Streams.AppendStream(data.Stream, data.Records, ct);

        // Act — the lax policy tolerates an unresolved name: the record passes through raw.
        var read = await consumer.Streams
            .ReadStream(data.Stream, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        // Assert
        await Assert.That(read).Count().IsEqualTo(1);
        await Assert.That(read[0].IsDecoded).IsFalse();
        await Assert.That(read[0].Value).IsNull();
    }
}
