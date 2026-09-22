using Bogus;
using Kurrent.Surge.Processors;
using Kurrent.Surge.Processors.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kurrent.Surge.Streaming.Tests;

[Category("Processors")]
public class FailProcessOnFatalErrorTests {
    const string TestConnectionString = "kurrentdb://localhost:2113";

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    public class LifetimeRegistrations : TestCaseDataGenerator<Action<IServiceCollection>> {
        protected override IEnumerable<TestCase<Action<IServiceCollection>>> Data() {
            yield return new(static services => services.AddSingleton<IHostLifetime>(new RecordingHostLifetime()), DisplayName: "instance");
            yield return new(static services => services.AddSingleton<IHostLifetime>(static _ => new RecordingHostLifetime()), DisplayName: "factory");
            yield return new(static services => services.AddSingleton<IHostLifetime, RecordingHostLifetime>(), DisplayName: "type");
        }
    }

    [Test]
    [LifetimeRegistrations]
    public async ValueTask wraps_the_registered_host_lifetime(Action<IServiceCollection> registerLifetime, CancellationToken ct) {
        // Arrange
        var services = NewServices();

        registerLifetime(services);

        // Act
        services.AddKurrentSurge(surge => surge.FailProcessOnFatalError());

        var provider = services.BuildServiceProvider();
        var lifetime = provider.GetRequiredService<IHostLifetime>();
        var inner    = (RecordingHostLifetime)provider.GetRequiredKeyedService<IHostLifetime>(KurrentSurgeBuilder.InnerLifetimeKey);

        await lifetime.WaitForStartAsync(ct);
        await lifetime.StopAsync(ct);

        // Assert
        var started = inner.Started;
        var stopped = inner.Stopped;

        await Assert.That(lifetime).IsTypeOf<ProcessorFailureHostLifetime>();
        await Assert.That(started).IsTrue();
        await Assert.That(stopped).IsTrue();
    }

    [Test]
    public async ValueTask rethrows_the_recorded_failure_after_the_inner_lifetime_stops(CancellationToken ct) {
        // Arrange
        var failure  = new InvalidOperationException(Faker.Lorem.Sentence());
        var inner    = new RecordingHostLifetime();
        var services = NewServices();

        services.AddSingleton<IHostLifetime>(inner);
        services.AddKurrentSurge(surge => surge.FailProcessOnFatalError());

        var provider = services.BuildServiceProvider();
        var lifetime = provider.GetRequiredService<IHostLifetime>();
        var recorder = provider.GetRequiredService<ProcessorFailureRecorder>();

        recorder.OnStateChanged(StateChange(DroneStatus.Stopped, failure));

        // Act
        var thrown = await Assert.That(() => lifetime.StopAsync(ct)).Throws<InvalidOperationException>();

        // Assert
        var stopped = inner.Stopped;

        await Assert.That(thrown).IsSameReferenceAs(failure);
        await Assert.That(stopped).IsTrue();
    }

    [Test]
    public async ValueTask a_stop_without_an_error_records_nothing(CancellationToken ct) {
        // Arrange
        var inner    = new RecordingHostLifetime();
        var services = NewServices();

        services.AddSingleton<IHostLifetime>(inner);
        services.AddKurrentSurge(surge => surge.FailProcessOnFatalError());

        var provider = services.BuildServiceProvider();
        var lifetime = provider.GetRequiredService<IHostLifetime>();
        var recorder = provider.GetRequiredService<ProcessorFailureRecorder>();

        recorder.OnStateChanged(StateChange(DroneStatus.Stopping));
        recorder.OnStateChanged(StateChange(DroneStatus.Stopped));

        // Act
        await lifetime.StopAsync(ct);

        // Assert
        var recorded = recorder.Failure;
        var stopped  = inner.Stopped;

        await Assert.That(recorded).IsNull();
        await Assert.That(stopped).IsTrue();
    }

    [Test]
    public async ValueTask keeps_the_first_failure() {
        // Arrange
        var first    = new InvalidOperationException(Faker.Lorem.Sentence());
        var second   = new InvalidOperationException(Faker.Lorem.Sentence());
        var recorder = new ProcessorFailureRecorder();

        // Act
        recorder.OnStateChanged(StateChange(DroneStatus.Stopped, first));
        recorder.OnStateChanged(StateChange(DroneStatus.Stopped, second));

        // Assert
        var recorded = recorder.Failure;

        await Assert.That(recorded).IsSameReferenceAs(first);
    }

    [Test]
    public async ValueTask the_recorder_observes_every_processor() {
        // Arrange
        var firstId  = Faker.Random.AlphaNumeric(12);
        var secondId = Faker.Random.AlphaNumeric(12);
        var services = NewServices();

        services.AddSingleton<IHostLifetime>(new RecordingHostLifetime());

        // Act
        services.AddKurrentSurge(surge => surge
            .AddProcessor<TestableRecordProcessor>(firstId)
            .FailProcessOnFatalError()
            .AddProcessor<TestableRecordProcessor>(secondId));

        // Assert
        var provider        = services.BuildServiceProvider();
        var recorder        = provider.GetRequiredService<ProcessorFailureRecorder>();
        var firstObservers  = provider.GetRequiredKeyedService<IDrone>(firstId).Options.Observers;
        var secondObservers = provider.GetRequiredKeyedService<IDrone>(secondId).Options.Observers;

        await Assert.That(firstObservers).Contains(recorder);
        await Assert.That(secondObservers).Contains(recorder);
    }

    [Test]
    public async ValueTask without_the_opt_in_the_host_lifetime_is_untouched() {
        // Arrange
        var processorId = Faker.Random.AlphaNumeric(12);
        var inner       = new RecordingHostLifetime();
        var services    = NewServices();

        services.AddSingleton<IHostLifetime>(inner);

        // Act
        services.AddKurrentSurge(surge => surge.AddProcessor<TestableRecordProcessor>(processorId));

        // Assert
        var provider  = services.BuildServiceProvider();
        var lifetime  = provider.GetRequiredService<IHostLifetime>();
        var recorder  = provider.GetService<ProcessorFailureRecorder>();
        var observers = provider.GetRequiredKeyedService<IDrone>(processorId).Options.Observers;
        var recorders = observers.OfType<ProcessorFailureRecorder>().Count();

        await Assert.That(lifetime).IsSameReferenceAs(inner);
        await Assert.That(recorder).IsNull();
        await Assert.That(recorders).IsEqualTo(0);
    }

    [Test]
    public async ValueTask a_second_opt_in_wraps_once() {
        // Arrange
        var inner    = new RecordingHostLifetime();
        var services = NewServices();

        services.AddSingleton<IHostLifetime>(inner);

        // Act
        services.AddKurrentSurge(surge => surge.FailProcessOnFatalError());
        services.AddKurrentSurge(surge => surge.FailProcessOnFatalError());

        // Assert
        var provider      = services.BuildServiceProvider();
        var unkeyed       = services.Count(d => !d.IsKeyedService && d.ServiceType == typeof(IHostLifetime));
        var innerLifetime = provider.GetRequiredKeyedService<IHostLifetime>(KurrentSurgeBuilder.InnerLifetimeKey);

        await Assert.That(unkeyed).IsEqualTo(1);
        await Assert.That(innerLifetime).IsSameReferenceAs(inner);
    }

    [Test]
    public async ValueTask throws_when_no_host_lifetime_is_registered() {
        // Arrange
        var services = NewServices();

        // Act
        var register = () => services.AddKurrentSurge(surge => surge.FailProcessOnFatalError());

        // Assert
        await Assert.That(register).Throws<InvalidOperationException>();
    }

    static ProcessorStateChanged StateChange(DroneStatus to, Exception? error = null) =>
        new(new ProcessorInfo("processor", "processor-drone"), DroneStatus.Processing, to, DroneProgress.Empty, DateTimeOffset.UtcNow, error);

    static ServiceCollection NewServices() {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:KurrentDB"] = TestConnectionString
        }).Build());

        return services;
    }

    sealed class RecordingHostLifetime : IHostLifetime {
        public bool Started { get; private set; }
        public bool Stopped { get; private set; }

        public Task WaitForStartAsync(CancellationToken cancellationToken) {
            Started = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) {
            Stopped = true;
            return Task.CompletedTask;
        }
    }
}
