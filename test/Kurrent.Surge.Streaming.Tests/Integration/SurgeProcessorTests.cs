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
    [Arguments(true)]
    [Arguments(false)]
    public async ValueTask uses_the_processor_id_for_the_drone_and_the_health_check(bool passed, CancellationToken ct) {
        // Arrange — a passed id is used as given; without one the id is the type name in kebab case.
        var processorId = passed ? NewProcessorId() : "testable-record-processor";
        var dronePrefix = $"{processorId}-";

        var builder = passed
            ? SurgeProcessor.CreateBuilder<TestableRecordProcessor>([], processorId)
            : SurgeProcessor.CreateBuilder<TestableRecordProcessor>();

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
    [Arguments(true)]
    [Arguments(false)]
    public async ValueTask sets_the_application_name_to_the_title_case_of_the_processor_id(bool passed) {
        // Arrange
        var words       = Faker.Lorem.Words(3);
        var processorId = passed ? string.Join('-', words) : "testable-record-processor";
        var expected    = passed ? string.Join(' ', words.Select(Capitalise)) : "Testable Record Processor";

        // Act
        var builder = passed
            ? SurgeProcessor.CreateBuilder<TestableRecordProcessor>([], processorId)
            : SurgeProcessor.CreateBuilder<TestableRecordProcessor>();

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
    [Arguments(true)]
    [Arguments(false)]
    public async ValueTask passes_the_section_keyed_by_the_processor_id_to_the_configure_delegate(bool passed) {
        // Arrange
        var processorId  = passed ? NewProcessorId() : "testable-record-processor";
        var maxBatchSize = Faker.Random.Int(501, 10_000);
        var received     = 0;

        string[] args = [$"--Kurrent:Surge:Processors:{processorId}:Batching:MaxBatchSize={maxBatchSize}"];

        var builder = passed
            ? SurgeProcessor.CreateBuilder<TestableRecordProcessor>(args, processorId, opts => received = opts.Batching.MaxBatchSize)
            : SurgeProcessor.CreateBuilder<TestableRecordProcessor>(args, opts => received = opts.Batching.MaxBatchSize);

        UseFixtureConnection(builder);

        // Act
        using var host = builder.Build();

        // Assert
        var actual = DroneOptions(host).Batching.MaxBatchSize;

        await Assert.That(received).IsEqualTo(maxBatchSize);
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
    [Arguments("")]
    [Arguments("   ")]
    public async ValueTask falls_back_to_the_type_name_when_the_passed_processor_id_is_blank(string processorId) {
        // Arrange — a blank id reads as "none given", so the type name applies as if none was passed.
        var expected    = "testable-record-processor";
        var dronePrefix = $"{expected}-";

        var builder = SurgeProcessor.CreateBuilder<TestableRecordProcessor>([], processorId);
        UseFixtureConnection(builder);

        // Act
        using var host = builder.Build();

        // Assert
        var options = DroneOptions(host);
        var actual  = options.ProcessorId;
        var droneId = options.DroneId;

        await Assert.That(actual).IsEqualTo(expected);
        await Assert.That(droneId).StartsWith(dronePrefix);
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
    [NotInParallel("exit-code")]
    public async ValueTask reports_unhealthy_when_the_processor_fails_to_start(CancellationToken ct) {
        var originalExitCode = Environment.ExitCode;

        try {
            // Arrange
            Environment.ExitCode = 0;

            var failure     = new InvalidOperationException(Faker.Lorem.Sentence());
            var processorId = NewProcessorId();

            var builder = SurgeProcessor.CreateBuilder<FailingProcessor>([], processorId);
            UseFixtureConnection(builder);

            builder.Services.AddSingleton(new FailingProcessor(failure));

            using var host = builder.Build();

            var health = host.Services.GetRequiredService<HealthCheckService>();

            // Act
            await host.StartAsync(ct);

            // Assert
            await Assert.That(() => ProcessorHealth())
                .EventuallyPasses(status => status.IsEqualTo(HealthStatus.Unhealthy), Within(ct));

            async Task<HealthStatus> ProcessorHealth() {
                var report = await health.CheckHealthAsync(ct);
                return report.Entries[processorId].Status;
            }
        }
        finally {
            Environment.ExitCode = originalExitCode;
        }
    }

    [Test]
    public async ValueTask reports_degraded_while_checkpoints_fail_and_healthy_once_one_commits(CancellationToken ct) {
        // Arrange
        var data        = await Fixture.SeedTestStream(10, ct);
        var processorId = NewProcessorId();
        var failure     = new InvalidOperationException(Faker.Lorem.Sentence());
        var failing     = true;

        (HealthStatus Status, Exception? Error) degraded = (HealthStatus.Degraded, failure);
        (HealthStatus Status, Exception? Error) healthy  = (HealthStatus.Healthy, null);

        var builder = SurgeProcessor.CreateBuilder<TestableRecordProcessor>([], processorId, opts => {
            Subscribe(opts, data);
            opts.Checkpoint.Interval = TimeSpan.FromMilliseconds(100);
        });
        UseFixtureConnection(builder);

        using var host = builder.Build();

        var processor = host.Services.GetRequiredService<TestableRecordProcessor>();
        var health    = host.Services.GetRequiredService<HealthCheckService>();

        processor.OnCheckpointHook = _ => Volatile.Read(ref failing) ? throw failure : ValueTask.CompletedTask;

        // Act
        await host.StartAsync(ct);

        // Assert
        await Assert.That(() => ProcessorHealth())
            .EventuallyPasses(actual => actual.IsEqualTo(degraded), Within(ct));

        // Act
        Volatile.Write(ref failing, false);

        // Assert
        await Assert.That(() => ProcessorHealth())
            .EventuallyPasses(actual => actual.IsEqualTo(healthy), Within(ct));

        await host.StopAsync(ct);

        async Task<(HealthStatus Status, Exception? Error)> ProcessorHealth() {
            var report = await health.CheckHealthAsync(ct);
            var entry  = report.Entries[processorId];
            return (entry.Status, entry.Exception?.InnerException);
        }
    }

    [Test]
    [NotInParallel("exit-code")]
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

    [Test]
    [NotInParallel("exit-code")]
    public async ValueTask stops_gracefully_when_a_handler_observes_the_stopping_token(CancellationToken ct) {
        var originalExitCode = Environment.ExitCode;

        try {
            // Arrange
            Environment.ExitCode = 0;

            var processorId = NewProcessorId();
            var processor   = new WaitsOnTheStoppingToken();

            var builder = SurgeProcessor.CreateBuilder<WaitsOnTheStoppingToken>([], processorId);
            UseFixtureConnection(builder);

            builder.Services.AddSingleton(processor);

            using var host = builder.Build();

            var service = host.Services.GetRequiredService<DroneService<WaitsOnTheStoppingToken>>();
            var drone   = host.Services.GetRequiredService<IDrone>();

            // Act
            await host.StartAsync(ct);
            await processor.Connected.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            await host.StopAsync(ct);

            // Assert
            var faulted  = service.ExecuteTask is { IsFaulted: true };
            var exitCode = Environment.ExitCode;
            var status   = drone.Status;

            await Assert.That(faulted).IsFalse();
            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(status).IsEqualTo(DroneStatus.Stopped);
        }
        finally {
            Environment.ExitCode = originalExitCode;
        }
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

    sealed class WaitsOnTheStoppingToken : IRecordProcessor {
        public TaskCompletionSource Connected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask ProcessRecord(ProcessRecordContext context) => ValueTask.CompletedTask;

        async ValueTask IProcessor.OnConnected(ConnectedContext context) {
            Connected.TrySetResult();

            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
    }

    sealed class FailingProcessor(Exception failure) : IRecordProcessor {
        public ValueTask ProcessRecord(ProcessRecordContext context) => ValueTask.CompletedTask;

        ValueTask IProcessor.OnInitialize(InitializeContext context) => throw failure;
    }
}
