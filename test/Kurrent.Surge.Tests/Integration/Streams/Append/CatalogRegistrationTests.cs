using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Schema.Governance;
using Kurrent.Surge.Schema.Governance;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;
using Kurrent.Surge.Schema.Naming;
using Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;
using Kurrent.Testkit.Datasets.ArcadeRacer.Model;

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
    public async ValueTask cancelled_walk_surfaces_cancellation_instead_of_a_registration_failure(CancellationToken ct) {
        // Arrange — the walk collects each entry's failure so one restart fixes the whole catalog,
        // but cancellation is not a schema failure. It ends the walk instead of being collected as
        // one, so disposing a client mid-walk stops it rather than fabricating a catalog failure.
        var nameStrategy = NewIsolatedNameStrategy();
        var catalog      = new MessageCatalog("registration-cancelled").Map<RaceCar>(nameStrategy);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var governance = SchemaGovernance.Create(
            Fixture.Client, [catalog], KurrentClientSchemaOptions.AutoRegister, cancellation.Token);

        // Act & Assert
        await Assert.That(async () => await governance.CatalogRegistration)
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async ValueTask walk_reports_every_failed_entry_rather_than_stopping_at_the_first(CancellationToken ct) {
        // Arrange — a strict client over two schema names that were never registered, so each
        // entry fails its own compatibility check. The walk covers every entry before reporting.
        var nameStrategy = NewIsolatedNameStrategy();

        var catalog = new MessageCatalog("registration-two-unregistered")
            .Map<RaceCar>(nameStrategy)
            .Map<LapCompleted>(nameStrategy);

        var governance = SchemaGovernance.Create(
            Fixture.Client, [catalog], KurrentClientSchemaOptions.Strict, ct);

        // Act
        var ex = await Assert.ThrowsAsync<CatalogRegistrationFailedException>(
            async () => await governance.CatalogRegistration);

        // Assert — both entries reported, not just the first.
        await Assert.That(ex?.Failures).Count().IsEqualTo(2);
    }

    [Test]
    [Skip("The server reports a lost create race two ways: FailedPrecondition with a stream revision "
        + "conflict, which SchemaAlreadyExistsException.Match now classifies, and Internal wrapping "
        + "ReadResponseException+StreamNotFound, which is the same shape a genuinely failed create "
        + "produces on a cold cluster. Classifying the second as a race would hide real failures, so "
        + "the loser's outcome is not decidable client-side. Unskip when the server classifies a lost "
        + "create as AlreadyExists.")]
    public async ValueTask concurrent_walks_registering_one_schema_converge_on_a_single_version(CancellationToken ct) {
        // Arrange — two walks racing the same isolated name. One create wins; the loser's
        // SchemaAlreadyExists resolves through a compatibility check instead of a retry, so both
        // walks complete and the registry holds one schema at one version.
        var nameStrategy = NewIsolatedNameStrategy();
        var catalog      = new MessageCatalog("registration-race").Map<RaceCar>(nameStrategy);
        var schemaName   = nameStrategy.GenerateSchemaName(typeof(RaceCar));

        var first  = SchemaGovernance.Create(Fixture.Client, [catalog], KurrentClientSchemaOptions.AutoRegister, ct);
        var second = SchemaGovernance.Create(Fixture.Client, [catalog], KurrentClientSchemaOptions.AutoRegister, ct);

        // Act — both start before either completes.
        await Task.WhenAll(first.CatalogRegistration, second.CatalogRegistration);

        // Assert
        await Assert.That(async () => {
            var schema = await Fixture.Client.Registry.GetSchema(schemaName, ct);
            await Assert.That(schema.LatestSchemaVersion).IsEqualTo(1);
        }).EventuallyPasses(RetryOnTransients(ct));
    }

    [Test]
    public async ValueTask concurrent_compatibility_checks_return_one_cached_version_id(CancellationToken ct) {
        // Arrange — a registered schema, then many callers checking it at once. The version cache
        // swaps its arrays instead of mutating them, so every racing caller sees one whole value.
        var nameStrategy = NewIsolatedNameStrategy();
        var catalog      = new MessageCatalog("registration-cache-race").Map<RaceCar>(nameStrategy);
        var schemaName   = nameStrategy.GenerateSchemaName(typeof(RaceCar));

        var registrar = SchemaGovernance.Create(Fixture.Client, [catalog], KurrentClientSchemaOptions.AutoRegister, ct);
        await registrar.CatalogRegistration;

        var governance = SchemaGovernance.Create(Fixture.Client, [catalog], KurrentClientSchemaOptions.Strict, ct);
        var schemaInfo = new RecordSchemaInfo { Schema = schemaName, DataFormat = SchemaDataFormat.Json };

        // Registry reads lag writes, so the schema has to be readable before the race starts.
        await Assert.That(async () => {
            await Fixture.Client.Registry.GetSchema(schemaName, ct);
        }).EventuallyPasses(RetryOnTransients(ct));

        // Act — no version id on the record, so every call resolves by name through the cache.
        var results = await Task.WhenAll(
            Enumerable.Range(0, 16).Select(_ =>
                governance.EnsureCompatible(schemaInfo, typeof(RaceCar), ct).AsTask()));

        // Assert
        await Assert.That(results.Distinct()).Count().IsEqualTo(1);
        await Assert.That(results[0]).IsNotEqualTo(SchemaVersionId.Unset);
    }

    [Test]
    public async ValueTask catalog_registration_is_empty_when_schema_options_are_disabled(CancellationToken ct) {
        // Arrange — real governance, the live catalog entry for RaceCar, and the default
        // schema options (Disabled): the registration completes empty without touching the
        // registry, and steady-state governance stays pure lookup.
        var raceCarType = typeof(Kurrent.Testkit.Datasets.ArcadeRacer.Model.RaceCar);

        var catalogs = MessageCatalogDiscovery.GetCatalogs()
            .Where(catalog => catalog.Types.Contains(raceCarType))
            .ToList();

        var schemaName = MessageResolver.From(catalogs).ResolveSchemaName(raceCarType);

        var governance = SchemaGovernance.Create(Fixture.Client, catalogs, Schema);

        // Act
        var registered = await governance.CatalogRegistration;

        // Assert — nothing was required, so nothing was walked, and steady-state governance
        // is pure lookup returning Unset.
        await Assert.That(registered.Count).IsEqualTo(0);

        var versionId = await governance.EnsureRegistered(schemaName, raceCarType, ct);
        await Assert.That(versionId).IsEqualTo(SchemaVersionId.Unset);
    }
}
