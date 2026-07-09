using Kurrent.Surge.Client;
using Kurrent.Processors.Configuration;
using Kurrent.Surge.Streaming.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kurrent.Surge.Streaming.Tests;

/// <summary>
/// Unit tests for processor registration through the <c>AddKurrent</c> builder scope.
/// Delegation semantics (dedup, options resolution) are covered by <see cref="ConfigurationTests"/>;
/// these verify the builder surface wires into the same pipeline.
/// </summary>
[Category("Processors")]
public class KurrentBuilderProcessorTests {
    const string TestConnectionString = "kurrentdb://localhost:2113";

    [Test]
    public async ValueTask builder_registers_processor_as_hosted_service() {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddKurrent(kurrent =>
            kurrent.AddProcessor(new TestableRecordProcessor(), opts => {
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

        services.AddKurrent(kurrent => kurrent
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

        services.AddKurrent(kurrent => kurrent
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
    public async ValueTask builder_and_service_collection_registrations_dedupe() {
        var services = new ServiceCollection();

        services.AddKurrentProcessor(new TestableRecordProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId = "first";
        });

        services.AddKurrent(kurrent =>
            kurrent.AddProcessor(new TestableRecordProcessor(), opts => {
                opts.Connection(TestConnectionString);
                opts.ProcessorId = "second";
            }));

        var hostedServiceCount = services.Count(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationFactory is not null);

        await Assert.That(hostedServiceCount).IsEqualTo(1);
    }

    [Test]
    public async ValueTask builder_registers_batch_processor_as_hosted_service() {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddKurrent(kurrent =>
            kurrent.AddBatchProcessor(new TestableBatchProcessor(), opts => {
                opts.Connection(TestConnectionString);
                opts.ProcessorId = "builder-batch-processor";
            }));

        var provider     = services.BuildServiceProvider();
        var droneService = provider.GetServices<IHostedService>().OfType<DroneService<TestableBatchProcessor>>().Single();

        await Assert.That(droneService.Drone.Options.ProcessorId).IsEqualTo("builder-batch-processor");
    }

    sealed class TestableBatchProcessor : RecordBatchProcessor {
        public override ValueTask ProcessBatch(BatchContext context) => ValueTask.CompletedTask;
    }
}
