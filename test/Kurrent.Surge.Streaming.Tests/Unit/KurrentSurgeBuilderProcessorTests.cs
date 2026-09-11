using Bogus;
using Kurrent.Surge.Processors;
using Kurrent.Surge.Client;
using Kurrent.Surge.Processors.Configuration;
using Kurrent.Surge.Processors.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kurrent.Surge.Streaming.Tests;

/// <summary>
/// Unit tests for processor registration through the <c>AddKurrentSurge</c> builder scope.
/// Drone construction and options resolution are covered by <see cref="ConfigurationTests"/>;
/// these verify the builder surface wires into the same pipeline.
/// </summary>
[Category("Processors")]
public class KurrentSurgeBuilderProcessorTests {
    const string TestConnectionString = "kurrentdb://localhost:2113";

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    public class NamedRecordRegistrations : TestCaseDataGenerator<Func<IKurrentSurgeBuilder, string, IKurrentSurgeBuilder>> {
        protected override IEnumerable<TestCase<Func<IKurrentSurgeBuilder, string, IKurrentSurgeBuilder>>> Data() {
            yield return new(static (surge, id) => surge.AddProcessor<TestableRecordProcessor>(id), DisplayName: "resolved");
            yield return new(static (surge, id) => surge.AddProcessor<TestableRecordProcessor>(id, static (_, _) => { }), DisplayName: "resolved, with services");
            yield return new(static (surge, id) => surge.AddProcessor<TestableRecordProcessor>(id, static _ => new TestableRecordProcessor()), DisplayName: "factory");
            yield return new(static (surge, id) => surge.AddProcessor<TestableRecordProcessor>(id, static _ => new TestableRecordProcessor(), static (_, _) => { }), DisplayName: "factory, with services");
            yield return new(static (surge, id) => surge.AddProcessor<TestableRecordProcessor>(id, new TestableRecordProcessor()), DisplayName: "instance");
            yield return new(static (surge, id) => surge.AddProcessor<TestableRecordProcessor>(id, new TestableRecordProcessor(), static (_, _) => { }), DisplayName: "instance, with services");
        }
    }

    public class NamedBatchRegistrations : TestCaseDataGenerator<Func<IKurrentSurgeBuilder, string, IKurrentSurgeBuilder>> {
        protected override IEnumerable<TestCase<Func<IKurrentSurgeBuilder, string, IKurrentSurgeBuilder>>> Data() {
            yield return new(static (surge, id) => surge.AddBatchProcessor<TestableBatchProcessor>(id), DisplayName: "resolved");
            yield return new(static (surge, id) => surge.AddBatchProcessor<TestableBatchProcessor>(id, static (_, _) => { }), DisplayName: "resolved, with services");
            yield return new(static (surge, id) => surge.AddBatchProcessor<TestableBatchProcessor>(id, static _ => new TestableBatchProcessor()), DisplayName: "factory");
            yield return new(static (surge, id) => surge.AddBatchProcessor<TestableBatchProcessor>(id, static _ => new TestableBatchProcessor(), static (_, _) => { }), DisplayName: "factory, with services");
            yield return new(static (surge, id) => surge.AddBatchProcessor<TestableBatchProcessor>(id, new TestableBatchProcessor()), DisplayName: "instance");
            yield return new(static (surge, id) => surge.AddBatchProcessor<TestableBatchProcessor>(id, new TestableBatchProcessor(), static (_, _) => { }), DisplayName: "instance, with services");
        }
    }

    [Test]
    [NamedRecordRegistrations]
    public async ValueTask named_processor_overloads_read_the_section_keyed_by_the_passed_id(Func<IKurrentSurgeBuilder, string, IKurrentSurgeBuilder> register) {
        // Arrange
        var processorId  = Faker.Random.AlphaNumeric(12);
        var maxBatchSize = Faker.Random.Int(501, 10_000);

        var services = NewServices(new() {
            [$"Kurrent:Surge:Processors:{processorId}:Batching:MaxBatchSize"] = $"{maxBatchSize}"
        });

        services.AddSingleton<TestableRecordProcessor>();

        // Act
        services.AddKurrentSurge(surge => register(surge, processorId));

        // Assert
        var options      = DroneOptions<TestableRecordProcessor>(services);
        var actualId     = options.ProcessorId;
        var actualBatch  = options.Batching.MaxBatchSize;

        await Assert.That(actualId).IsEqualTo(processorId);
        await Assert.That(actualBatch).IsEqualTo(maxBatchSize);
    }

    [Test]
    [NamedBatchRegistrations]
    public async ValueTask named_batch_processor_overloads_read_the_section_keyed_by_the_passed_id(Func<IKurrentSurgeBuilder, string, IKurrentSurgeBuilder> register) {
        // Arrange
        var processorId  = Faker.Random.AlphaNumeric(12);
        var maxBatchSize = Faker.Random.Int(501, 10_000);

        var services = NewServices(new() {
            [$"Kurrent:Surge:Processors:{processorId}:Batching:MaxBatchSize"] = $"{maxBatchSize}"
        });

        services.AddSingleton<TestableBatchProcessor>();

        // Act
        services.AddKurrentSurge(surge => register(surge, processorId));

        // Assert
        var options      = DroneOptions<TestableBatchProcessor>(services);
        var actualId     = options.ProcessorId;
        var actualBatch  = options.Batching.MaxBatchSize;

        await Assert.That(actualId).IsEqualTo(processorId);
        await Assert.That(actualBatch).IsEqualTo(maxBatchSize);
    }

    [Test]
    public async ValueTask unnamed_overload_reads_the_section_keyed_by_the_type_name() {
        // Arrange
        var maxBatchSize = Faker.Random.Int(501, 10_000);
        var expectedId   = "testable-record-processor";

        var services = NewServices(new() {
            [$"Kurrent:Surge:Processors:{nameof(TestableRecordProcessor)}:Batching:MaxBatchSize"] = $"{maxBatchSize}"
        });

        // Act
        services.AddKurrentSurge(surge => surge.AddProcessor(new TestableRecordProcessor()));

        // Assert
        var options     = DroneOptions<TestableRecordProcessor>(services);
        var actualId    = options.ProcessorId;
        var actualBatch = options.Batching.MaxBatchSize;

        await Assert.That(actualId).IsEqualTo(expectedId);
        await Assert.That(actualBatch).IsEqualTo(maxBatchSize);
    }

    [Test]
    public async ValueTask setting_the_processor_id_in_the_configure_delegate_reads_no_second_section() {
        // Arrange
        var processorId = Faker.Random.AlphaNumeric(12);
        var changedId   = Faker.Random.AlphaNumeric(12);
        var registered  = Faker.Random.Int(501, 5_000);
        var unread      = Faker.Random.Int(5_001, 10_000);

        var services = NewServices(new() {
            [$"Kurrent:Surge:Processors:{processorId}:Batching:MaxBatchSize"] = $"{registered}",
            [$"Kurrent:Surge:Processors:{changedId}:Batching:MaxBatchSize"]   = $"{unread}"
        });

        // Act
        services.AddKurrentSurge(surge =>
            surge.AddProcessor(processorId, new TestableRecordProcessor(), opts => opts.ProcessorId = changedId));

        // Assert
        var options     = DroneOptions<TestableRecordProcessor>(services);
        var actualId    = options.ProcessorId;
        var actualBatch = options.Batching.MaxBatchSize;

        await Assert.That(actualId).IsEqualTo(changedId);
        await Assert.That(actualBatch).IsEqualTo(registered);
    }

    [Test]
    public async ValueTask builder_registers_processor_as_hosted_service() {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddKurrentSurge(surge =>
            surge.AddProcessor(new TestableRecordProcessor(), opts => {
                opts.Connection(TestConnectionString);
                opts.ProcessorId = "builder-processor";
            }));

        var provider     = services.BuildServiceProvider();
        var droneService = provider.GetServices<IHostedService>().OfType<DroneService<TestableRecordProcessor>>().Single();

        await Assert.That(droneService.Drone.Options.ProcessorId).IsEqualTo("builder-processor");
    }

    [Test]
    public async ValueTask builder_processor_inherits_scope_connection() {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddKurrentSurge(surge => surge
            .Connection(TestConnectionString)
            .AddProcessor(new TestableRecordProcessor(), opts => opts.ProcessorId = "scope-inherited"));

        var provider     = services.BuildServiceProvider();
        var droneService = provider.GetServices<IHostedService>().OfType<DroneService<TestableRecordProcessor>>().Single();

        await Assert.That(droneService.Drone.Options.ClientOptions).IsNotNull();
        await Assert.That(((KurrentClientOptions)droneService.Drone.Options.ClientOptions!).Endpoints[0].Host).IsEqualTo("localhost");
    }

    [Test]
    public async ValueTask builder_processor_explicit_connection_wins_over_scope_connection() {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddKurrentSurge(surge => surge
            .Connection("kurrentdb://other-host:2113")
            .AddProcessor(new TestableRecordProcessor(), opts => {
                opts.ProcessorId = "explicit-wins";
                opts.Connection(TestConnectionString);
            }));

        var provider     = services.BuildServiceProvider();
        var droneService = provider.GetServices<IHostedService>().OfType<DroneService<TestableRecordProcessor>>().Single();

        await Assert.That(((KurrentClientOptions)droneService.Drone.Options.ClientOptions!).Endpoints[0].Host).IsEqualTo("localhost");
    }

    [Test]
    public async ValueTask repeated_registrations_of_the_same_processor_dedupe() {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddKurrentSurge(surge =>
            surge.AddProcessor(new TestableRecordProcessor(), opts => {
                opts.Connection(TestConnectionString);
                opts.ProcessorId = "first";
            }));

        services.AddKurrentSurge(surge =>
            surge.AddProcessor(new TestableRecordProcessor(), opts => {
                opts.Connection(TestConnectionString);
                opts.ProcessorId = "second";
            }));

        var provider      = services.BuildServiceProvider();
        var droneServices = provider.GetServices<IHostedService>().OfType<DroneService<TestableRecordProcessor>>().ToList();

        // Dedup keys on the closed DroneService<T> type, so the first registration wins.
        await Assert.That(droneServices.Count).IsEqualTo(1);
        await Assert.That(droneServices[0].Drone.Options.ProcessorId).IsEqualTo("first");
    }

    [Test]
    public async ValueTask builder_processor_derives_drone_id_from_the_configured_processor_id() {
        var services = new ServiceCollection();
        services.AddLogging();

        // The derived id is assigned before the delegate runs; the drone id follows the delegate's id.
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddKurrentSurge(surge =>
            surge.AddProcessor(new TestableRecordProcessor(), opts => {
                opts.Connection(TestConnectionString);
                opts.ProcessorId = "custom-processor";
            }));

        var provider     = services.BuildServiceProvider();
        var droneService = provider.GetServices<IHostedService>().OfType<DroneService<TestableRecordProcessor>>().Single();

        await Assert.That(droneService.Drone.DroneId).StartsWith("custom-processor-");
    }

    [Test]
    public async ValueTask builder_registers_batch_processor_as_hosted_service() {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddKurrentSurge(surge =>
            surge.AddBatchProcessor(new TestableBatchProcessor(), opts => {
                opts.Connection(TestConnectionString);
                opts.ProcessorId = "builder-batch-processor";
            }));

        var provider     = services.BuildServiceProvider();
        var droneService = provider.GetServices<IHostedService>().OfType<DroneService<TestableBatchProcessor>>().Single();

        await Assert.That(droneService.Drone.Options.ProcessorId).IsEqualTo("builder-batch-processor");
    }

    static ServiceCollection NewServices(Dictionary<string, string?> settings) {
        settings["ConnectionStrings:KurrentDB"] = TestConnectionString;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        return services;
    }

    static ProcessorOptions DroneOptions<TProcessor>(IServiceCollection services) where TProcessor : class, IProcessor =>
        services.BuildServiceProvider().GetServices<IHostedService>().OfType<DroneService<TProcessor>>().Single().Drone.Options;

    sealed class TestableBatchProcessor : RecordBatchProcessor {
        public override ValueTask ProcessBatch(BatchContext context) => ValueTask.CompletedTask;
    }
}
