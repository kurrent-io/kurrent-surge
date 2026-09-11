using Bogus;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Processors;
using Kurrent.Surge.Processors.Configuration;
using Kurrent.Surge.Processors.Hosting;
using Kurrent.Surge.Processors.Models;
using Kurrent.Testkit.TUnit.Assertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Testing.ArcadeRacer;

namespace Kurrent.Surge.Streaming.Tests;

[Category("Processors")]
[Category("Integration")]
[Timeout(10_000)]
public class SurgeProcessorTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask runs_a_class_processor_and_delivers_records(CancellationToken ct) {
        // Arrange
        var data        = await Fixture.SeedTestStream(10, ct);
        var processorId = NewProcessorId();
        var expected    = data.Records.Length;

        var builder = SurgeProcessor.CreateBuilder<TestableRecordProcessor>([], processorId, opts => Subscribe(opts, data));
        UseFixtureConnection(builder);

        using var host = builder.Build();

        var processor = host.Services.GetRequiredService<TestableRecordProcessor>();

        // Act
        await host.StartAsync(ct);

        // Assert
        await Assert.That(() => processor.Records.Length)
            .EventuallyPasses(count => count.IsEqualTo(expected), Within(ct));

        await host.StopAsync(ct);
    }

    [Test]
    public async ValueTask uses_the_passed_processor_id_for_the_drone_and_the_health_check(CancellationToken ct) {
        // Arrange
        var processorId = NewProcessorId();
        var dronePrefix = $"{processorId}-";

        var builder = SurgeProcessor.CreateBuilder<TestableRecordProcessor>([], processorId);
        UseFixtureConnection(builder);

        // Act
        using var host = builder.Build();

        // Assert
        var drone  = host.Services.GetRequiredService<IDrone>();
        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(ct);

        var actualProcessorId = drone.ProcessorId;
        var droneId           = drone.DroneId;
        var checkNames        = report.Entries.Keys.ToList();

        await Assert.That(actualProcessorId).IsEqualTo(processorId);
        await Assert.That(droneId).StartsWith(dronePrefix);
        await Assert.That(checkNames).Contains(processorId);
    }

    [Test]
    public async ValueTask derives_the_processor_id_from_the_type_name_in_kebab_case() {
        // Arrange
        var expected    = "testable-record-processor";
        var dronePrefix = $"{expected}-";

        var builder = SurgeProcessor.CreateBuilder<TestableRecordProcessor>();
        UseFixtureConnection(builder);

        // Act
        using var host = builder.Build();

        // Assert
        var options     = DroneOptions(host);
        var processorId = options.ProcessorId;
        var droneId     = options.DroneId;

        await Assert.That(processorId).IsEqualTo(expected);
        await Assert.That(droneId).StartsWith(dronePrefix);
    }

    [Test]
    public async ValueTask sets_the_application_name_to_the_title_case_of_the_passed_processor_id() {
        // Arrange
        var words       = Faker.Lorem.Words(3);
        var processorId = string.Join('-', words);
        var expected    = string.Join(' ', words.Select(Capitalise));

        // Act
        var builder = SurgeProcessor.CreateBuilder<TestableRecordProcessor>([], processorId);
        UseFixtureConnection(builder);

        var atCreation = builder.Environment.ApplicationName;

        using var host = builder.Build();

        // Assert
        var applicationName = host.Services.GetRequiredService<IHostEnvironment>().ApplicationName;

        await Assert.That(atCreation).IsEqualTo(expected);
        await Assert.That(applicationName).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask sets_the_application_name_to_the_title_case_of_the_derived_processor_id() {
        // Arrange
        var expected = "Testable Record Processor";

        // Act
        var builder = SurgeProcessor.CreateBuilder<TestableRecordProcessor>();
        UseFixtureConnection(builder);

        var atCreation = builder.Environment.ApplicationName;

        using var host = builder.Build();

        // Assert
        var applicationName = host.Services.GetRequiredService<IHostEnvironment>().ApplicationName;

        await Assert.That(atCreation).IsEqualTo(expected);
        await Assert.That(applicationName).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask keeps_an_explicit_application_name() {
        // Arrange
        var applicationName = Faker.Random.AlphaNumeric(12);
        var processorId     = NewProcessorId();

        var builder = SurgeProcessor.CreateBuilder<TestableRecordProcessor>([$"--applicationName={applicationName}"], processorId);
        UseFixtureConnection(builder);

        // Act
        using var host = builder.Build();

        // Assert
        var actualApplicationName = host.Services.GetRequiredService<IHostEnvironment>().ApplicationName;
        var actualProcessorId     = DroneOptions(host).ProcessorId;

        await Assert.That(actualApplicationName).IsEqualTo(applicationName);
        await Assert.That(actualProcessorId).IsEqualTo(processorId);
    }

    [Test]
    public async ValueTask passes_the_section_keyed_by_the_passed_processor_id_to_the_configure_delegate() {
        // Arrange
        var processorId  = NewProcessorId();
        var maxBatchSize = Faker.Random.Int(501, 10_000);
        var received     = 0;

        string[] args = [$"--Kurrent:Surge:Processors:{processorId}:Batching:MaxBatchSize={maxBatchSize}"];

        var builder = SurgeProcessor.CreateBuilder<TestableRecordProcessor>(args, processorId, opts => received = opts.Batching.MaxBatchSize);
        UseFixtureConnection(builder);

        // Act
        using var host = builder.Build();

        // Assert
        var actual = DroneOptions(host).Batching.MaxBatchSize;

        await Assert.That(received).IsEqualTo(maxBatchSize);
        await Assert.That(actual).IsEqualTo(maxBatchSize);
    }

    [Test]
    public async ValueTask reads_the_section_keyed_by_the_derived_processor_id() {
        // Arrange
        var maxBatchSize = Faker.Random.Int(501, 10_000);

        string[] args = [$"--Kurrent:Surge:Processors:testable-record-processor:Batching:MaxBatchSize={maxBatchSize}"];

        var builder = SurgeProcessor.CreateBuilder<TestableRecordProcessor>(args);
        UseFixtureConnection(builder);

        // Act
        using var host = builder.Build();

        // Assert
        var actual = DroneOptions(host).Batching.MaxBatchSize;

        await Assert.That(actual).IsEqualTo(maxBatchSize);
    }

    [Test]
    public async ValueTask values_set_in_the_configure_delegate_win_over_the_section() {
        // Arrange
        var processorId = NewProcessorId();
        var configured  = Faker.Random.Int(501, 5_000);
        var expected    = Faker.Random.Int(5_001, 10_000);

        string[] args = [$"--Kurrent:Surge:Processors:{processorId}:Batching:MaxBatchSize={configured}"];

        var builder = SurgeProcessor.CreateBuilder<TestableRecordProcessor>(args, processorId, opts => opts.Batching.MaxBatchSize = expected);
        UseFixtureConnection(builder);

        // Act
        using var host = builder.Build();

        // Assert
        var actual = DroneOptions(host).Batching.MaxBatchSize;

        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask ignores_a_processor_id_key_in_the_section() {
        // Arrange
        var processorId  = NewProcessorId();
        var configuredId = NewProcessorId();

        // The reader reports the key as an unrecognised setting, observable here only through the log.
        string[] args = [$"--Kurrent:Surge:Processors:{processorId}:ProcessorId={configuredId}"];

        var builder = SurgeProcessor.CreateBuilder<TestableRecordProcessor>(args, processorId);
        UseFixtureConnection(builder);

        // Act
        using var host = builder.Build();

        // Assert
        var actual = host.Services.GetRequiredService<IDrone>().ProcessorId;

        await Assert.That(actual).IsEqualTo(processorId);
    }

    [Test]
    public async ValueTask reads_only_the_registered_section_when_the_configure_delegate_changes_the_processor_id() {
        // Arrange
        var processorId = NewProcessorId();
        var changedId   = NewProcessorId();
        var registered  = Faker.Random.Int(501, 5_000);
        var unread      = Faker.Random.Int(5_001, 10_000);
        var dronePrefix = $"{changedId}-";

        string[] args = [
            $"--Kurrent:Surge:Processors:{processorId}:Batching:MaxBatchSize={registered}",
            $"--Kurrent:Surge:Processors:{changedId}:Batching:MaxBatchSize={unread}"
        ];

        var builder = SurgeProcessor.CreateBuilder<TestableRecordProcessor>(args, processorId, opts => opts.ProcessorId = changedId);
        UseFixtureConnection(builder);

        // Act
        using var host = builder.Build();

        // Assert
        var options      = DroneOptions(host);
        var maxBatchSize = options.Batching.MaxBatchSize;
        var actualId     = options.ProcessorId;
        var droneId      = options.DroneId;

        await Assert.That(maxBatchSize).IsEqualTo(registered);
        await Assert.That(actualId).IsEqualTo(changedId);
        await Assert.That(droneId).StartsWith(dronePrefix);
    }

    [Test]
    public async ValueTask rejects_a_second_processor_at_build() {
        // Arrange
        var builder = SurgeProcessor.CreateBuilder<TestableRecordProcessor>();
        UseFixtureConnection(builder);

        builder.Services.AddKurrentSurge(surge =>
            surge.AddProcessor(new FailingProcessor(new InvalidOperationException(Faker.Lorem.Sentence()))));

        // Act
        var exception = await Assert.That(() => builder.Build()).Throws<InvalidOperationException>();

        // Assert
        var message = exception!.Message;

        await Assert.That(message).Contains(nameof(FailingProcessor));
    }

    [Test]
    public async ValueTask reports_healthy_while_processing_and_unhealthy_after_stop(CancellationToken ct) {
        // Arrange
        var data        = await Fixture.SeedTestStream(10, ct);
        var processorId = NewProcessorId();

        var builder = SurgeProcessor.CreateBuilder<TestableRecordProcessor>([], processorId, opts => Subscribe(opts, data));
        UseFixtureConnection(builder);

        using var host = builder.Build();

        var health = host.Services.GetRequiredService<HealthCheckService>();

        // Act
        await host.StartAsync(ct);

        // Assert
        await Assert.That(() => ProcessorHealth())
            .EventuallyPasses(status => status.IsEqualTo(HealthStatus.Healthy), Within(ct));

        // Act
        await host.StopAsync(ct);

        // Assert
        var stopped = await ProcessorHealth();

        await Assert.That(stopped).IsEqualTo(HealthStatus.Unhealthy);

        async Task<HealthStatus> ProcessorHealth() {
            var report = await health.CheckHealthAsync(ct);
            return report.Entries[processorId].Status;
        }
    }

    [Test]
    [NotInParallel]
    public async ValueTask sets_a_failing_exit_code_when_the_processor_faults(CancellationToken ct) {
        var originalExitCode = Environment.ExitCode;

        try {
            // Arrange
            Environment.ExitCode = 0;

            var failure     = new InvalidOperationException(Faker.Lorem.Sentence());
            var processorId = NewProcessorId();

            var builder = SurgeProcessor.CreateBuilder<FailingProcessor>([], processorId);
            UseFixtureConnection(builder);

            builder.Services.AddSingleton(new FailingProcessor(failure));

            var host    = builder.Build();
            var service = host.Services.GetRequiredService<DroneService<FailingProcessor>>();

            // Act
            await host.RunAsync(ct);

            // Assert
            var exitCode = Environment.ExitCode;
            var faulted  = service.ExecuteTask is { IsFaulted: true };

            await Assert.That(exitCode).IsEqualTo(1);
            await Assert.That(faulted).IsTrue();
        }
        finally {
            Environment.ExitCode = originalExitCode;
        }
    }

    [Test]
    public async ValueTask builds_without_command_line_args() {
        // Arrange
        var builder = SurgeProcessor.CreateBuilder<TestableRecordProcessor>();
        UseFixtureConnection(builder);

        // Act
        using var host = builder.Build();

        // Assert
        var first         = host.Services.GetRequiredService<TestableRecordProcessor>();
        var second        = host.Services.GetRequiredService<TestableRecordProcessor>();
        var clientOptions = DroneOptions(host).ClientOptions;

        await Assert.That(first).IsSameReferenceAs(second);
        await Assert.That(clientOptions).IsNotNull();
    }

    [Test]
    public async ValueTask applies_the_container_logger_factory_to_a_configured_connection() {
        // Arrange
        var builder = SurgeProcessor.CreateBuilder<TestableRecordProcessor>();
        UseFixtureConnection(builder);

        // Act
        using var host = builder.Build();

        // Assert
        var expected      = host.Services.GetRequiredService<ILoggerFactory>();
        var clientOptions = (KurrentClientOptions)DroneOptions(host).ClientOptions!;
        var actual        = clientOptions.LoggerFactory;

        await Assert.That(actual).IsSameReferenceAs(expected);
    }

    [Test]
    public async ValueTask tunes_the_scope_connection_for_the_processor_and_the_container_client() {
        // Arrange
        var loggerFactory = LoggerFactory.Create(static _ => { });

        var builder = SurgeProcessor.CreateBuilder<TestableRecordProcessor>();
        UseFixtureConnection(builder);

        builder.AddKurrentSurge(surge => surge.Connection(client => client.LoggerFactory = loggerFactory));

        // Act
        using var host = builder.Build();

        // Assert
        var containerClient  = host.Services.GetRequiredService<KurrentClient>();
        var processorOptions = (KurrentClientOptions)DroneOptions(host).ClientOptions!;
        var containerFactory = containerClient.Options.LoggerFactory;
        var processorFactory = processorOptions.LoggerFactory;

        await Assert.That(containerFactory).IsSameReferenceAs(loggerFactory);
        await Assert.That(processorFactory).IsSameReferenceAs(loggerFactory);
    }

    string NewProcessorId() => Faker.Random.AlphaNumeric(12);

    void UseFixtureConnection(IHostApplicationBuilder builder) =>
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:KurrentDB"] = Fixture.ConnectionString
        });

    static ProcessorOptions DroneOptions(IHost host) =>
        host.Services.GetRequiredService<IDrone>().Options;

    static void Subscribe(ProcessorOptions options, SeededTestStream data) {
        options.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
        options.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
    }

    static string Capitalise(string word) => $"{char.ToUpperInvariant(word[0])}{word[1..]}";

    static EventuallyOptions Within(CancellationToken ct) =>
        new() { Timeout = TimeSpan.FromSeconds(5), CancellationToken = ct };

    sealed class FailingProcessor(Exception failure) : IRecordProcessor {
        public ValueTask ProcessRecord(ProcessRecordContext context) => ValueTask.CompletedTask;

        ValueTask IProcessor.OnInitialize(InitializeContext context) => throw failure;
    }
}
