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
using Testing.ArcadeRacer;

namespace Kurrent.Surge.Streaming.Tests;

[Category("Processors")]
[Category("Integration")]
[Timeout(10_000)]
public class ProcessorHostingTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask runs_the_processor_the_container_registered(CancellationToken ct) {
        // Arrange
        var data        = await Fixture.SeedTestStream(10, ct);
        var processorId = NewProcessorId();
        var expected    = data.Records.Length;

        var builder = NewHostBuilder();

        builder.AddKurrentSurge(surge => surge.AddProcessor<TestableRecordProcessor>(processorId, opts => Subscribe(opts, data)));

        using var host = builder.Build();

        var processor = host.Services.GetRequiredKeyedService<TestableRecordProcessor>(processorId);

        // Act
        await host.StartAsync(ct);

        // Assert
        await Assert.That(() => processor.Records.Length)
            .EventuallyPasses(count => count.IsEqualTo(expected), Within(ct));

        await host.StopAsync(ct);
    }

    [Test]
    public async ValueTask runs_the_same_type_once_per_processor_id(CancellationToken ct) {
        // Arrange
        var firstData  = await Fixture.SeedTestStream(5, ct);
        var secondData = await Fixture.SeedTestStream(7, ct);
        var firstId    = NewProcessorId();
        var secondId   = NewProcessorId();

        var expectedFirst  = firstData.Records.Length;
        var expectedSecond = secondData.Records.Length;

        var builder = NewHostBuilder();

        builder.AddKurrentSurge(surge => surge
            .AddProcessor<TestableRecordProcessor>(firstId, opts => Subscribe(opts, firstData))
            .AddProcessor<TestableRecordProcessor>(secondId, opts => Subscribe(opts, secondData)));

        using var host = builder.Build();

        var first  = host.Services.GetRequiredKeyedService<TestableRecordProcessor>(firstId);
        var second = host.Services.GetRequiredKeyedService<TestableRecordProcessor>(secondId);

        // Act
        await host.StartAsync(ct);

        // Assert
        await Assert.That(() => first.Records.Length)
            .EventuallyPasses(count => count.IsEqualTo(expectedFirst), Within(ct));

        await Assert.That(() => second.Records.Length)
            .EventuallyPasses(count => count.IsEqualTo(expectedSecond), Within(ct));

        var report     = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(ct);
        var checkNames = report.Entries.Keys.ToList();
        var firstDrone = host.Services.GetRequiredKeyedService<IDrone>(firstId).ProcessorId;

        await Assert.That(second).IsNotSameReferenceAs(first);
        await Assert.That(checkNames).Contains(firstId).And.Contains(secondId);
        await Assert.That(firstDrone).IsEqualTo(firstId);

        await host.StopAsync(ct);
    }

    [Test]
    public async ValueTask reports_healthy_while_processing_and_unhealthy_after_stop(CancellationToken ct) {
        // Arrange
        var data        = await Fixture.SeedTestStream(10, ct);
        var processorId = NewProcessorId();

        var builder = NewHostBuilder();

        builder.AddKurrentSurge(surge => surge.AddProcessor<TestableRecordProcessor>(processorId, opts => Subscribe(opts, data)));

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
    public async ValueTask reports_unhealthy_when_the_processor_fails_to_start(CancellationToken ct) {
        // Arrange
        var failure     = new InvalidOperationException(Faker.Lorem.Sentence());
        var processorId = NewProcessorId();

        var builder = NewHostBuilder();

        builder.Services.AddKeyedSingleton(processorId, new TestableRecordProcessor { OnInitializeHook = _ => throw failure });
        builder.AddKurrentSurge(surge => surge.AddProcessor<TestableRecordProcessor>(processorId));

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

    [Test]
    public async ValueTask reports_degraded_while_checkpoints_fail_and_healthy_once_one_commits(CancellationToken ct) {
        // Arrange
        var data        = await Fixture.SeedTestStream(10, ct);
        var processorId = NewProcessorId();
        var failure     = new InvalidOperationException(Faker.Lorem.Sentence());
        var failing     = true;

        (HealthStatus Status, Exception? Error) degraded = (HealthStatus.Degraded, failure);
        (HealthStatus Status, Exception? Error) healthy  = (HealthStatus.Healthy, null);

        var builder = NewHostBuilder();

        builder.AddKurrentSurge(surge => surge.AddProcessor<TestableRecordProcessor>(processorId, opts => {
            Subscribe(opts, data);
            opts.Checkpoint.Interval = TimeSpan.FromMilliseconds(100);
        }));

        using var host = builder.Build();

        var processor = host.Services.GetRequiredKeyedService<TestableRecordProcessor>(processorId);
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
    public async ValueTask stops_when_a_handler_observes_the_stopping_token(CancellationToken ct) {
        // Arrange
        var processorId = NewProcessorId();
        var processor   = new TestableRecordProcessor {
            OnConnectedHook = async context => await Task.Delay(Timeout.Infinite, context.CancellationToken)
        };

        var builder = NewHostBuilder();

        builder.Services.AddKeyedSingleton(processorId, processor);
        builder.AddKurrentSurge(surge => surge.AddProcessor<TestableRecordProcessor>(processorId));

        using var host = builder.Build();

        var service = host.Services.GetRequiredKeyedService<DroneService<TestableRecordProcessor>>(processorId);
        var drone   = host.Services.GetRequiredKeyedService<IDrone>(processorId);

        // Act
        await host.StartAsync(ct);

        await Assert.That(() => processor.Connected.Count)
            .EventuallyPasses(count => count.IsGreaterThanOrEqualTo(1), Within(ct));

        await host.StopAsync(ct);

        // Assert
        var faulted = service.ExecuteTask is { IsFaulted: true };
        var status  = drone.Status;

        await Assert.That(faulted).IsFalse();
        await Assert.That(status).IsEqualTo(DroneStatus.Stopped);
    }

    [Test]
    public async ValueTask run_async_returns_when_the_processor_fails_without_the_opt_in(CancellationToken ct) {
        // Arrange
        var failure     = new InvalidOperationException(Faker.Lorem.Sentence());
        var processorId = NewProcessorId();

        var builder = NewHostBuilder();

        builder.Services.AddKeyedSingleton(processorId, new TestableRecordProcessor { OnInitializeHook = _ => throw failure });
        builder.AddKurrentSurge(surge => surge.AddProcessor<TestableRecordProcessor>(processorId));

        var host    = builder.Build();
        var service = host.Services.GetRequiredKeyedService<DroneService<TestableRecordProcessor>>(processorId);

        // Act
        await host.RunAsync(ct);

        // Assert
        var faulted = service.ExecuteTask is { IsFaulted: true };

        await Assert.That(faulted).IsTrue();
    }

    [Test]
    public async ValueTask run_async_throws_the_processor_failure_when_opted_in(CancellationToken ct) {
        // Arrange
        var failure     = new InvalidOperationException(Faker.Lorem.Sentence());
        var processorId = NewProcessorId();

        var builder = NewHostBuilder();

        builder.Services.AddKeyedSingleton(processorId, new TestableRecordProcessor { OnInitializeHook = _ => throw failure });
        builder.AddKurrentSurge(surge => surge
            .FailProcessOnFatalError()
            .AddProcessor<TestableRecordProcessor>(processorId));

        var host = builder.Build();

        // Act
        var thrown = await Assert.That(async () => await host.RunAsync(ct)).Throws<Exception>();

        // Assert
        var causes = Causes(thrown!).ToList();

        await Assert.That(causes).Contains(failure);
    }

    [Test]
    public async ValueTask resumes_after_its_checkpoint_when_started_again(CancellationToken ct) {
        // Arrange — the same processor id runs twice against one stream. The first host stops after the
        // seeded records, and the checkpoint its shutdown commits must carry the second host past them.
        var data        = await Fixture.SeedTestStream(10, ct);
        var more        = await Fixture.TestData.GenerateSet(5, ct);
        var processorId = NewProcessorId();
        var firstRun    = new TestableRecordProcessor();
        var secondRun   = new TestableRecordProcessor();

        var expectedFirst  = data.Records.Length;
        var expectedSecond = more.Records.Length;
        var expectedResume = Revision.From(expectedFirst);
        var expectedLast   = Revision.From(expectedFirst + expectedSecond - 1);

        using (var first = NewHost(processorId, firstRun)) {
            await first.StartAsync(ct);

            await Assert.That(() => firstRun.Records.Length)
                .EventuallyPasses(count => count.IsEqualTo(expectedFirst), Within(ct));

            await first.StopAsync(ct);
        }

        await Fixture.Client.Streams.AppendStream(data.Stream, more.Records, ct);

        using var second = NewHost(processorId, secondRun);

        // Act
        await second.StartAsync(ct);

        // Assert
        await Assert.That(() => secondRun.Records.Length)
            .EventuallyPasses(count => count.IsEqualTo(expectedSecond), Within(ct));

        var resumedAt = secondRun.Records[0].Revision;
        var lastSeen  = secondRun.Records[^1].Revision;

        await Assert.That(resumedAt).IsEqualTo(expectedResume);
        await Assert.That(lastSeen).IsEqualTo(expectedLast);

        await second.StopAsync(ct);

        IHost NewHost(string id, TestableRecordProcessor processor) {
            var builder = NewHostBuilder();

            builder.Services.AddKeyedSingleton(id, processor);
            builder.AddKurrentSurge(surge => surge.AddProcessor<TestableRecordProcessor>(id, opts => Subscribe(opts, data)));

            return builder.Build();
        }
    }

    string NewProcessorId() => Faker.Random.AlphaNumeric(12);

    HostApplicationBuilder NewHostBuilder() {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:KurrentDB"] = Fixture.ConnectionString
        });

        return builder;
    }

    static void Subscribe(ProcessorOptions options, SeededTestStream data) {
        options.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
        options.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
    }

    static IEnumerable<Exception> Causes(Exception exception) {
        yield return exception;

        IEnumerable<Exception> inner = exception switch {
            AggregateException aggregate => aggregate.InnerExceptions,
            { InnerException: { } single } => [single],
            _ => []
        };

        foreach (var cause in inner.SelectMany(Causes))
            yield return cause;
    }

    static EventuallyOptions Within(CancellationToken ct) =>
        new() { Timeout = TimeSpan.FromSeconds(5), CancellationToken = ct };
}
