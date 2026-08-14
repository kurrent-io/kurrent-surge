using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Client.Schema.Governance;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;
using Kurrent.Surge.Schema.Naming;

namespace Kurrent.Surge.Tests.Streams;

[Category("Streams")]
[Category("Integration")]
[Timeout(30_000)]
public class CatalogRegistrationTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    // Self-composed, equivalent to the fixture client's own defaults — the pipeline-composition
    // seam on KurrentClient is gone, so tests build their own resolver/schema instead of
    // borrowing the client's.
    static readonly KurrentClientSchemaOptions Schema = KurrentClientSchemaOptions.Disabled;

    static ISchemaNameStrategy NewIsolatedNameStrategy() =>
        SchemaNameStrategies.Namespace($"test-registration-{Guid.NewGuid():N}");

    [Test]
    public async ValueTask first_connection_runs_initialization_tasks_exactly_once(CancellationToken ct) {
        // Arrange — a counting task on a default (lax) client; the shared fixture client
        // seeds the stream the reads use.
        var data = await Fixture.SeedTestStream(1, ct);

        var executions = 0;

        var options = KurrentClientOptions.Parse(Fixture.ConnectionString);
        options.Initialization.AddTask("count-executions", (_, _) => {
            Interlocked.Increment(ref executions);
            return ValueTask.CompletedTask;
        });

        await using var client = new KurrentClient(options);

        // Act — two calls; initialization runs off the first connection only.
        await client.Streams.ReadStream(data.Stream, ct).Where(msg => msg.IsRecord).ToListAsync(ct);
        await client.Streams.ReadStream(data.Stream, ct).Where(msg => msg.IsRecord).ToListAsync(ct);

        // Assert — the runner is asynchronous: it fires on connection, not inside the call.
        await Assert.That(async () => {
            await Assert.That(executions).IsEqualTo(1);
        }).EventuallyPasses(RetryOnTransients(ct));
    }

    [Test]
    public async ValueTask connect_async_runs_initialization_without_any_operation(CancellationToken ct) {
        // Arrange — a counting task; no read or append ever happens.
        var executions = 0;

        var options = KurrentClientOptions.Parse(Fixture.ConnectionString);
        options.Initialization.AddTask("count-executions", (_, _) => {
            Interlocked.Increment(ref executions);
            return ValueTask.CompletedTask;
        });

        await using var client = new KurrentClient(options);

        // Act — the explicit pre-warm is the only trigger.
        await client.ConnectAsync(ct);

        // Assert
        await Assert.That(async () => {
            await Assert.That(executions).IsEqualTo(1);
        }).EventuallyPasses(RetryOnTransients(ct));
    }

    [Test]
    public async ValueTask failed_warn_task_does_not_affect_the_client(CancellationToken ct) {
        // Arrange — a Warn task that always fails; the client must stay fully usable.
        var data = await Fixture.SeedTestStream(1, ct);

        var options = KurrentClientOptions.Parse(Fixture.ConnectionString);
        options.Initialization.AddTask("induced-warn-failure", (_, _) =>
            throw new InvalidOperationException("induced initialization failure"));

        await using var client = new KurrentClient(options);

        // Act
        var read = await client.Streams.ReadStream(data.Stream, ct).Where(msg => msg.IsRecord).ToListAsync(ct);

        // Assert
        await Assert.That(read).Count().IsEqualTo(1);
    }

    [Test]
    public async ValueTask failed_fault_task_faults_subsequent_stream_operations(CancellationToken ct) {
        // Arrange — a Fault task that always fails; once initialization stores the fault,
        // every stream operation throws it.
        var data = await Fixture.SeedTestStream(1, ct);

        var options = KurrentClientOptions.Parse(Fixture.ConnectionString);
        options.Initialization.AddTask(
            "induced-fault-failure",
            (_, _) => throw new InvalidOperationException("induced initialization fault"),
            throwOnFailure: true);

        await using var client = new KurrentClient(options);

        // Act — the first call triggers the connection; initialization runs concurrently, so
        // the fault lands eventually, then every operation throws it.
        await Assert.That(async () => {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await client.Streams.ReadStream(data.Stream, ct).Where(msg => msg.IsRecord).ToListAsync(ct));
        }).EventuallyPasses(RetryOnTransients(ct));
    }

    [Test]
    public async ValueTask first_connection_registers_the_catalog_through_real_governance(CancellationToken ct) {
        // Arrange — an auto-registering client whose only catalog maps one isolated name. The
        // triggering read skips decoding, so registration is provably driven by the connection,
        // never by a governed operation.
        var seeded       = await Fixture.SeedTestStream(1, ct);
        var nameStrategy = NewIsolatedNameStrategy();
        var messageType  = seeded.Records[0].Value!.GetType();
        var schemaName   = nameStrategy.GenerateSchemaName(messageType);
        var catalog      = new MessageCatalog("registration-single-type").Map(schemaName, messageType);

        await using var client = KurrentClient.Create(Fixture.ConnectionString, opts => {
            opts.LoggerFactory = Fixture.LoggerFactory;
            opts.Resilience    = KurrentClientResilienceOptions.NoResilience;
            opts.Schema        = KurrentClientSchemaOptions.AutoRegister;
            opts.Messages.UseCatalog(catalog);
        });

        // Act — a non-governed call: decode skipped, no pipeline governance involved.
        await client.Streams
            .ReadStream(seeded.Stream, new ReadStreamOptions { SkipDecoding = true }, ct)
            .Where(msg => msg.IsRecord)
            .ToListAsync(ct);

        // Assert — the registry learned the schema on the back of the connection alone.
        await Assert.That(async () => {
            var schema = await Fixture.Client.Registry.GetSchema(schemaName, ct);
            await Assert.That(schema.SchemaName).IsEqualTo(schemaName);
        }).EventuallyPasses(RetryOnTransients(ct));
    }

    [Test]
    public async ValueTask catalog_registration_is_empty_under_no_requirements_policy(CancellationToken ct) {
        // Arrange — real governance, the live catalog entry for RaceCar, and today's live
        // policy (NoRequirements): the registration completes empty without touching the
        // registry, and steady-state governance stays pure lookup.
        var streams = new StreamsClient(Fixture.Client);
        var policy  = streams.RegistryPolicy;

        var raceCarType = typeof(Kurrent.Testkit.Datasets.ArcadeRacer.Model.RaceCar);

        var catalogs = MessageCatalogDiscovery.GetCatalogs()
            .Where(catalog => catalog.Types.Contains(raceCarType))
            .ToList();

        var schemaName = MessageResolver.From(catalogs).ResolveSchemaName(raceCarType);

        var resolved = policy
            .Resolve(Schema, null, SchemaDataFormat.Json)
            .EnsureDataFormatCompliance(SchemaDataFormat.Json);

        var governance = SchemaGovernance.Create(Fixture.Client, catalogs, resolved);

        // Act
        var registered = await governance.CatalogRegistration;

        // Assert — nothing was required, so nothing was walked, and steady-state governance
        // is pure lookup returning Unset.
        await Assert.That(registered.Count).IsEqualTo(0);

        var versionId = await governance.EnsureRegistered(schemaName, raceCarType, resolved, ct);
        await Assert.That(versionId).IsEqualTo(SchemaVersionId.Unset);
    }
}
