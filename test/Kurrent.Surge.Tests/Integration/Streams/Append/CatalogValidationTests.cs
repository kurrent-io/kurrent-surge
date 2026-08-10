using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Schema.Governance;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Experimental;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;

namespace Kurrent.Surge.Tests.Streams;

[Category("Streams")]
[Category("Integration")]
[Timeout(30_000)]
public class CatalogValidationTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask first_grpc_call_starts_catalog_validation_exactly_once(CancellationToken ct) {
        // Arrange — a dedicated client carrying the interceptor; the shared fixture client
        // seeds the stream both clients read.
        var data = await Fixture.SeedTestStream(1, ct);

        var validations = 0;

        IReadOnlyDictionary<(Type MessageType, SchemaDataFormat Format), RecordSchemaInfo> empty =
            new Dictionary<(Type MessageType, SchemaDataFormat Format), RecordSchemaInfo>();

        var interceptor = new CatalogValidationInterceptor(() => {
            Interlocked.Increment(ref validations);
            return Task.FromResult(empty);
        });

        var options = KurrentClientOptions.Parse(Fixture.ConnectionString);
        options.Interceptors = [interceptor];

        await using var client = new KurrentClient(options);

        // Act — two calls; only the first may start validation.
        await client.Streams.ReadStream(data.Stream, ct).Where(msg => msg.IsRecord).ToListAsync(ct);
        await client.Streams.ReadStream(data.Stream, ct).Where(msg => msg.IsRecord).ToListAsync(ct);

        // Assert — the counter is checked BEFORE touching Validation: its getter would start
        // the task itself and mask a call path that never triggered.
        await Assert.That(validations).IsEqualTo(1);
    }

    [Test]
    public async ValueTask validation_failure_does_not_fault_the_triggering_call(CancellationToken ct) {
        // Arrange — a validation that always fails; the fault must stay stored in the task.
        var data = await Fixture.SeedTestStream(1, ct);

        var interceptor = new CatalogValidationInterceptor(
            () => Task.FromException<IReadOnlyDictionary<(Type MessageType, SchemaDataFormat Format), RecordSchemaInfo>>(
                new InvalidOperationException("induced validation failure")));

        var options = KurrentClientOptions.Parse(Fixture.ConnectionString);
        options.Interceptors = [interceptor];

        await using var client = new KurrentClient(options);

        // Act — the triggering call succeeds untaxed.
        var read = await client.Streams.ReadStream(data.Stream, ct).Where(msg => msg.IsRecord).ToListAsync(ct);

        // Assert — the failure surfaces only to whoever awaits the validation.
        await Assert.That(read).Count().IsEqualTo(1);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await interceptor.Validation);
    }

    [Test]
    public async ValueTask first_grpc_call_validates_the_catalog_through_real_governance(CancellationToken ct) {
        // Arrange — the real constructor: governance + live catalog entries + resolved policy,
        // attached to a dedicated client.
        var data    = await Fixture.SeedTestStream(1, ct);
        var streams = new StreamsClient(Fixture.Client);
        var mapping = streams.CreateMappingContext(ct);

        var (_, messages, registrations) = SchemaManagerGovernanceAdapter.Extract(mapping.SerializerProvider);

        var raceCarType = typeof(Kurrent.Testkit.Datasets.ArcadeRacer.Model.RaceCar);
        var schemaName  = messages.ResolveSchemaName(raceCarType);

        var resolved = mapping.Policy
            .Resolve(registrations[SchemaDataFormat.Json], null, SchemaDataFormat.Json)
            .EnsureDataFormatCompliance(SchemaDataFormat.Json);

        var catalogs = MessageCatalogDiscovery.GetCatalogs()
            .Where(catalog => catalog.Types.Contains(raceCarType))
            .ToList();

        var governance  = SchemaGovernance.Create(Fixture.Client);
        var interceptor = new CatalogValidationInterceptor(governance, catalogs, resolved, ct);

        var options = KurrentClientOptions.Parse(Fixture.ConnectionString);
        options.Interceptors = [interceptor];

        await using var client = new KurrentClient(options);

        // Act — the first call starts the validation; awaiting proves it completes.
        await client.Streams.ReadStream(data.Stream, ct).Where(msg => msg.IsRecord).ToListAsync(ct);
        await interceptor.Validation;

        // Assert — steady-state governance after validation is pure lookup returning Unset.
        var versionId = await governance.EnsureRegistered(schemaName, raceCarType, resolved, ct);
        await Assert.That(versionId).IsEqualTo(SchemaVersionId.Unset);
    }

    [Test]
    public async ValueTask catalog_validation_is_a_noop_under_no_requirements_policy(CancellationToken ct) {
        // Arrange — real governance, the live catalog entry for RaceCar, and today's live
        // policy (NoRequirements): validation must complete without touching the registry.
        var streams = new StreamsClient(Fixture.Client);
        var mapping = streams.CreateMappingContext(ct);

        var (_, messages, registrations) = SchemaManagerGovernanceAdapter.Extract(mapping.SerializerProvider);

        var raceCarType = typeof(Kurrent.Testkit.Datasets.ArcadeRacer.Model.RaceCar);
        var schemaName  = messages.ResolveSchemaName(raceCarType);

        var resolved = mapping.Policy
            .Resolve(registrations[SchemaDataFormat.Json], null, SchemaDataFormat.Json)
            .EnsureDataFormatCompliance(SchemaDataFormat.Json);

        var catalogs = MessageCatalogDiscovery.GetCatalogs()
            .Where(catalog => catalog.Types.Contains(raceCarType))
            .ToList();

        var governance = SchemaGovernance.Create(Fixture.Client);

        // Act
        var validated = await governance.ValidateCatalog(catalogs, resolved, ct);

        // Assert — the entry carries the full schema info with no version id, and steady-state
        // governance after validation is pure lookup returning Unset.
        var expectedInfo = new RecordSchemaInfo { Schema = schemaName, DataFormat = SchemaDataFormat.Json };
        await Assert.That(validated[(raceCarType, SchemaDataFormat.Json)]).IsEqualTo(expectedInfo);

        var versionId = await governance.EnsureRegistered(schemaName, raceCarType, resolved, ct);
        await Assert.That(versionId).IsEqualTo(SchemaVersionId.Unset);
    }
}
