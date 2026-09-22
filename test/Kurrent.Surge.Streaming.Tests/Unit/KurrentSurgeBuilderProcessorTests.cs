using Bogus;
using Kurrent.Surge.Processors;
using Kurrent.Surge.Client;
using Kurrent.Surge.Processors.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Kurrent.Surge.Streaming.Tests;

[Category("Processors")]
public class KurrentSurgeBuilderProcessorTests {
    const string TestConnectionString = "kurrentdb://localhost:2113";

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    public class NamedRegistrations : TestCaseDataGenerator<Func<IKurrentSurgeBuilder, string, IKurrentSurgeBuilder>> {
        protected override IEnumerable<TestCase<Func<IKurrentSurgeBuilder, string, IKurrentSurgeBuilder>>> Data() {
            yield return new(static (surge, id) => surge.AddProcessor<TestableRecordProcessor>(id), DisplayName: "record");
            yield return new(static (surge, id) => surge.AddProcessor<TestableRecordProcessor>(id, static (_, _) => { }), DisplayName: "record, with services");
            yield return new(static (surge, id) => surge.AddBatchProcessor<TestableBatchProcessor>(id), DisplayName: "batch");
            yield return new(static (surge, id) => surge.AddBatchProcessor<TestableBatchProcessor>(id, static (_, _) => { }), DisplayName: "batch, with services");
        }
    }

    public class UnnamedRegistrations : TestCaseDataGenerator<(Func<IKurrentSurgeBuilder, IKurrentSurgeBuilder> Register, string ExpectedId)> {
        protected override IEnumerable<TestCase<(Func<IKurrentSurgeBuilder, IKurrentSurgeBuilder> Register, string ExpectedId)>> Data() {
            yield return new((static surge => surge.AddProcessor<TestableRecordProcessor>(), "testable-record-processor"), DisplayName: "record");
            yield return new((static surge => surge.AddProcessor<TestableRecordProcessor>(static (_, _) => { }), "testable-record-processor"), DisplayName: "record, with services");
            yield return new((static surge => surge.AddBatchProcessor<TestableBatchProcessor>(), "testable-batch-processor"), DisplayName: "batch");
            yield return new((static surge => surge.AddBatchProcessor<TestableBatchProcessor>(static (_, _) => { }), "testable-batch-processor"), DisplayName: "batch, with services");
        }
    }

    public class BlankIds : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() {
            yield return new("", DisplayName: "empty");
            yield return new("   ", DisplayName: "whitespace");
        }
    }

    public class DuplicateIds : TestCaseDataGenerator<Action<IKurrentSurgeBuilder, string>> {
        protected override IEnumerable<TestCase<Action<IKurrentSurgeBuilder, string>>> Data() {
            yield return new(static (surge, id) => surge.AddProcessor<TestableRecordProcessor>(id), DisplayName: "same type");
            yield return new(static (surge, id) => surge.AddBatchProcessor<TestableBatchProcessor>(id), DisplayName: "another type");
        }
    }

    [Test]
    [NamedRegistrations]
    public async ValueTask named_overloads_read_the_section_keyed_by_the_passed_id(Func<IKurrentSurgeBuilder, string, IKurrentSurgeBuilder> register) {
        // Arrange
        var processorId  = Faker.Random.AlphaNumeric(12);
        var maxBatchSize = Faker.Random.Int(501, 10_000);

        var services = NewServices(new() {
            [$"Kurrent:Surge:Processors:{processorId}:Batching:MaxBatchSize"] = $"{maxBatchSize}"
        });

        // Act
        services.AddKurrentSurge(surge => register(surge, processorId));

        // Assert
        var options     = DroneOptions(services, processorId);
        var actualId    = options.ProcessorId;
        var actualBatch = options.Batching.MaxBatchSize;

        await Assert.That(actualId).IsEqualTo(processorId);
        await Assert.That(actualBatch).IsEqualTo(maxBatchSize);
    }

    [Test]
    [UnnamedRegistrations]
    public async ValueTask unnamed_overloads_read_the_section_keyed_by_the_type_name(Func<IKurrentSurgeBuilder, IKurrentSurgeBuilder> register, string expectedId) {
        // Arrange
        var maxBatchSize = Faker.Random.Int(501, 10_000);

        var services = NewServices(new() {
            [$"Kurrent:Surge:Processors:{expectedId}:Batching:MaxBatchSize"] = $"{maxBatchSize}"
        });

        // Act
        services.AddKurrentSurge(surge => register(surge));

        // Assert
        var options     = DroneOptions(services, expectedId);
        var actualId    = options.ProcessorId;
        var actualBatch = options.Batching.MaxBatchSize;

        await Assert.That(actualId).IsEqualTo(expectedId);
        await Assert.That(actualBatch).IsEqualTo(maxBatchSize);
    }

    [Test]
    [BlankIds]
    public async ValueTask a_blank_id_reads_as_none_given(string processorId) {
        // Arrange
        var expectedId  = "testable-record-processor";
        var dronePrefix = $"{expectedId}-";
        var services    = NewServices();

        // Act
        services.AddKurrentSurge(surge => surge.AddProcessor<TestableRecordProcessor>(processorId));

        // Assert
        var drone    = services.BuildServiceProvider().GetRequiredKeyedService<IDrone>(expectedId);
        var actualId = drone.ProcessorId;
        var droneId  = drone.DroneId;

        await Assert.That(actualId).IsEqualTo(expectedId);
        await Assert.That(droneId).StartsWith(dronePrefix);
    }

    [Test]
    public async ValueTask setting_the_processor_id_in_the_configure_delegate_reads_no_second_section() {
        // Arrange
        var processorId = Faker.Random.AlphaNumeric(12);
        var changedId   = Faker.Random.AlphaNumeric(12);
        var registered  = Faker.Random.Int(501, 5_000);
        var unread      = Faker.Random.Int(5_001, 10_000);
        var dronePrefix = $"{changedId}-";

        var services = NewServices(new() {
            [$"Kurrent:Surge:Processors:{processorId}:Batching:MaxBatchSize"] = $"{registered}",
            [$"Kurrent:Surge:Processors:{changedId}:Batching:MaxBatchSize"]   = $"{unread}"
        });

        // Act
        services.AddKurrentSurge(surge => surge.AddProcessor<TestableRecordProcessor>(processorId, opts => opts.ProcessorId = changedId));

        // Assert
        var options     = DroneOptions(services, processorId);
        var actualId    = options.ProcessorId;
        var actualBatch = options.Batching.MaxBatchSize;
        var droneId     = options.DroneId;

        await Assert.That(actualId).IsEqualTo(changedId);
        await Assert.That(actualBatch).IsEqualTo(registered);
        await Assert.That(droneId).StartsWith(dronePrefix);
    }

    [Test]
    public async ValueTask registers_the_processor_as_a_keyed_singleton() {
        // Arrange
        var processorId = Faker.Random.AlphaNumeric(12);
        var otherId     = Faker.Random.AlphaNumeric(12);
        var services    = NewServices();

        // Act
        services.AddKurrentSurge(surge => surge.AddProcessor<TestableRecordProcessor>(processorId));

        // Assert
        var provider = services.BuildServiceProvider();
        var first    = provider.GetRequiredKeyedService<TestableRecordProcessor>(processorId);
        var second   = provider.GetRequiredKeyedService<TestableRecordProcessor>(processorId);
        var other    = provider.GetKeyedService<TestableRecordProcessor>(otherId);

        await Assert.That(second).IsSameReferenceAs(first);
        await Assert.That(other).IsNull();
    }

    [Test]
    public async ValueTask keeps_a_keyed_registration_the_caller_made() {
        // Arrange
        var processorId = Faker.Random.AlphaNumeric(12);
        var instance    = new TestableRecordProcessor();
        var services    = NewServices();

        services.AddKeyedSingleton(processorId, instance);

        // Act
        services.AddKurrentSurge(surge => surge.AddProcessor<TestableRecordProcessor>(processorId));

        // Assert
        var resolved = services.BuildServiceProvider().GetRequiredKeyedService<TestableRecordProcessor>(processorId);

        await Assert.That(resolved).IsSameReferenceAs(instance);
    }

    [Test]
    public async ValueTask a_lone_processor_also_resolves_unkeyed() {
        // Arrange
        var processorId = Faker.Random.AlphaNumeric(12);
        var services    = NewServices();

        // Act
        services.AddKurrentSurge(surge => surge.AddProcessor<TestableRecordProcessor>(processorId));

        // Assert
        var provider = services.BuildServiceProvider();
        var keyed    = provider.GetRequiredKeyedService<TestableRecordProcessor>(processorId);
        var unkeyed  = provider.GetRequiredService<TestableRecordProcessor>();

        await Assert.That(unkeyed).IsSameReferenceAs(keyed);
    }

    [Test]
    public async ValueTask a_second_id_of_the_same_type_removes_the_unkeyed_registration() {
        // Arrange
        var firstId  = Faker.Random.AlphaNumeric(12);
        var secondId = Faker.Random.AlphaNumeric(12);
        var services = NewServices();

        // Act
        services.AddKurrentSurge(surge => surge
            .AddProcessor<TestableRecordProcessor>(firstId)
            .AddProcessor<TestableRecordProcessor>(secondId));

        // Assert
        var provider = services.BuildServiceProvider();
        var first    = provider.GetRequiredKeyedService<TestableRecordProcessor>(firstId);
        var second   = provider.GetRequiredKeyedService<TestableRecordProcessor>(secondId);
        var unkeyed  = provider.GetService<TestableRecordProcessor>();

        await Assert.That(second).IsNotSameReferenceAs(first);
        await Assert.That(unkeyed).IsNull();
    }

    [Test]
    public async ValueTask an_unkeyed_registration_the_caller_made_is_a_separate_instance() {
        // Arrange
        var firstId  = Faker.Random.AlphaNumeric(12);
        var secondId = Faker.Random.AlphaNumeric(12);
        var instance = new TestableRecordProcessor();
        var services = NewServices();

        services.AddSingleton(instance);

        // Act
        services.AddKurrentSurge(surge => surge
            .AddProcessor<TestableRecordProcessor>(firstId)
            .AddProcessor<TestableRecordProcessor>(secondId));

        // Assert
        var provider = services.BuildServiceProvider();
        var keyed    = provider.GetRequiredKeyedService<TestableRecordProcessor>(firstId);
        var unkeyed  = provider.GetRequiredService<TestableRecordProcessor>();

        await Assert.That(unkeyed).IsSameReferenceAs(instance);
        await Assert.That(keyed).IsNotSameReferenceAs(instance);
    }

    [Test]
    [DuplicateIds]
    public async ValueTask a_duplicate_processor_id_throws(Action<IKurrentSurgeBuilder, string> registerAgain) {
        // Arrange
        var processorId = Faker.Random.AlphaNumeric(12);
        var services    = NewServices();

        services.AddKurrentSurge(surge => surge.AddProcessor<TestableRecordProcessor>(processorId));

        // Act
        var exception = await Assert.That(() => services.AddKurrentSurge(surge => registerAgain(surge, processorId)))
            .Throws<InvalidOperationException>();

        // Assert
        var message = exception!.Message;

        await Assert.That(message).Contains(processorId);
    }

    [Test]
    public async ValueTask registers_the_drone_keyed_by_the_processor_id() {
        // Arrange
        var firstId     = Faker.Random.AlphaNumeric(12);
        var secondId    = Faker.Random.AlphaNumeric(12);
        var firstPrefix = $"{firstId}-";
        var services    = NewServices();

        // Act
        services.AddKurrentSurge(surge => surge
            .AddProcessor<TestableRecordProcessor>(firstId)
            .AddProcessor<TestableRecordProcessor>(secondId));

        // Assert
        var provider      = services.BuildServiceProvider();
        var first         = provider.GetRequiredKeyedService<IDrone>(firstId);
        var second        = provider.GetRequiredKeyedService<IDrone>(secondId);
        var firstActualId = first.ProcessorId;
        var firstDroneId  = first.DroneId;
        var secondActual  = second.ProcessorId;

        await Assert.That(firstActualId).IsEqualTo(firstId);
        await Assert.That(firstDroneId).StartsWith(firstPrefix);
        await Assert.That(secondActual).IsEqualTo(secondId);
    }

    [Test]
    public async ValueTask registers_a_health_check_named_by_the_processor_id(CancellationToken ct) {
        // Arrange
        var processorId  = Faker.Random.AlphaNumeric(12);
        var expectedTags = new[] { "kurrent", "surge", "processor" };
        var services     = NewServices();

        // Act
        services.AddKurrentSurge(surge => surge.AddProcessor<TestableRecordProcessor>(processorId));

        // Assert
        var provider     = services.BuildServiceProvider();
        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations.Single(r => r.Name == processorId);
        var report       = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(ct);
        var tags         = registration.Tags.ToArray();
        var status       = report.Entries[processorId].Status;

        await Assert.That(tags).IsEquivalentTo(expectedTags);
        await Assert.That(status).IsEqualTo(HealthStatus.Degraded);
    }

    [Test]
    public async ValueTask inherits_the_scope_connection() {
        // Arrange
        var processorId = Faker.Random.AlphaNumeric(12);
        var services    = new ServiceCollection();

        services.AddLogging();

        // Act
        services.AddKurrentSurge(surge => surge
            .Connection(TestConnectionString)
            .AddProcessor<TestableRecordProcessor>(processorId));

        // Assert
        var clientOptions = (KurrentClientOptions)DroneOptions(services, processorId).ClientOptions!;
        var host          = clientOptions.Endpoints[0].Host;

        await Assert.That(host).IsEqualTo("localhost");
    }

    [Test]
    public async ValueTask an_explicit_connection_wins_over_the_scope_connection() {
        // Arrange
        var processorId = Faker.Random.AlphaNumeric(12);
        var services    = new ServiceCollection();

        services.AddLogging();

        // Act
        services.AddKurrentSurge(surge => surge
            .Connection("kurrentdb://other-host:2113")
            .AddProcessor<TestableRecordProcessor>(processorId, opts => opts.Connection(TestConnectionString)));

        // Assert
        var clientOptions = (KurrentClientOptions)DroneOptions(services, processorId).ClientOptions!;
        var host          = clientOptions.Endpoints[0].Host;

        await Assert.That(host).IsEqualTo("localhost");
    }

    static ServiceCollection NewServices(Dictionary<string, string?>? settings = null) {
        settings ??= [];
        settings["ConnectionStrings:KurrentDB"] = TestConnectionString;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        return services;
    }

    static ProcessorOptions DroneOptions(IServiceCollection services, string processorId) =>
        services.BuildServiceProvider().GetRequiredKeyedService<IDrone>(processorId).Options;

    sealed class TestableBatchProcessor : RecordBatchProcessor {
        public override ValueTask ProcessBatch(BatchContext context) => ValueTask.CompletedTask;
    }
}
