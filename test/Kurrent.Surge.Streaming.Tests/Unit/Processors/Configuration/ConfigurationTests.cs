using System.Net;
using Bogus;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Processors;
using Kurrent.Surge.Processors.Configuration;
using Kurrent.Surge.Processors.Hosting;
using Kurrent.Surge.Processors.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ValidationException = Kurrent.Surge.Validation.ValidationException;

namespace Kurrent.Surge.Streaming.Tests;

[Category("Processors")]
public class ConfigurationTests {
    const string TestConnectionString = "kurrentdb://localhost:2113";

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    #region ->> Options <<-

    [Test]
    public async ValueTask options_default_to_the_documented_values() {
        // Arrange
        var expectedBatchWindow = TimeSpan.FromMilliseconds(100);
        var expectedInterval    = TimeSpan.FromSeconds(30);

        // Act
        var options = new ProcessorOptions();

        // Assert
        await Assert.That(options.ProcessorId).IsEqualTo("");
        await Assert.That(options.DroneId).IsEqualTo("");
        await Assert.That(options.SkipDecoding).IsFalse();
        await Assert.That(options.Batching.MaxBatchSize).IsEqualTo(500);
        await Assert.That(options.Batching.BatchWindow).IsEqualTo(expectedBatchWindow);
        await Assert.That(options.Batching.BufferMultiplier).IsEqualTo(2);
        await Assert.That(options.Checkpoint.Interval).IsEqualTo(expectedInterval);
        await Assert.That(options.Subscription.InitialPosition).IsEqualTo(SubscriptionInitialPosition.Latest);
    }

    [Test]
    public async ValueTask drone_id_follows_the_processor_id_until_set_explicitly() {
        // Arrange
        var firstId    = Faker.Random.AlphaNumeric(10);
        var secondId   = Faker.Random.AlphaNumeric(10);
        var thirdId    = Faker.Random.AlphaNumeric(10);
        var fourthId   = Faker.Random.AlphaNumeric(10);
        var explicitId = Faker.Random.AlphaNumeric(10);

        var options = new ProcessorOptions { ProcessorId = firstId };
        var derived = options.DroneId;
        var suffix  = derived[(firstId.Length + 1)..];

        var expectedPrefix         = $"{firstId}-";
        var expectedAfterChange    = $"{secondId}-{suffix}";
        var expectedAfterWriteBack = $"{thirdId}-{suffix}";

        // Act
        options.ProcessorId = secondId;
        var afterChange = options.DroneId;

        // Configuration binding writes an unconfigured DroneId back unchanged.
        options.DroneId     = options.DroneId;
        options.ProcessorId = thirdId;
        var afterWriteBack = options.DroneId;

        options.DroneId     = explicitId;
        options.ProcessorId = fourthId;
        var afterExplicit = options.DroneId;

        var pinnedBeforeAnyId = new ProcessorOptions { DroneId = explicitId, ProcessorId = fourthId }.DroneId;

        // Assert
        var suffixLength = suffix.Length;

        await Assert.That(derived).StartsWith(expectedPrefix);
        await Assert.That(suffixLength).IsEqualTo(8);
        await Assert.That(afterChange).IsEqualTo(expectedAfterChange);
        await Assert.That(afterWriteBack).IsEqualTo(expectedAfterWriteBack);
        await Assert.That(afterExplicit).IsEqualTo(explicitId);
        await Assert.That(pinnedBeforeAnyId).IsEqualTo(explicitId);
    }

    [Test]
    public async ValueTask build_carries_every_option_to_the_drone() {
        // Arrange
        var processorId      = Faker.Random.AlphaNumeric(12);
        var droneId          = Faker.Random.AlphaNumeric(12);
        var maxBatchSize     = Faker.Random.Int(1, 10_000);
        var batchWindow      = TimeSpan.FromMilliseconds(Faker.Random.Int(1, 5_000));
        var bufferMultiplier = Faker.Random.Int(1, 100);
        var interval         = TimeSpan.FromSeconds(Faker.Random.Int(1, 300));
        var retentionCount   = Faker.Random.Int(1, 100);
        var selector         = $"data.{Faker.Random.String2(8, "abcdefghijklmnopqrstuvwxyz")}";
        var filter           = RecordFilter.ByExpression($"stream LIKE '{Faker.Random.AlphaNumeric(8)}-%'");
        var maxRetryAttempts = Faker.Random.Int(0, 100);
        var baseDelay        = TimeSpan.FromMilliseconds(Faker.Random.Int(100, 1_000));
        var maxDelay         = TimeSpan.FromSeconds(Faker.Random.Int(2, 60));
        var connectionString = NewConnectionString();
        var expectedEndpoint = EndpointOf(KurrentClientOptions.Parse(connectionString));

        // Act
        var drone = KurrentProcessor.Create(new TestableRecordProcessor(), opts => {
            opts.Connection(connectionString);

            opts.ProcessorId    = processorId;
            opts.DroneId        = droneId;
            opts.Debug          = true;
            opts.SkipDecoding   = true;
            opts.TrackSourceLag = true;

            opts.Batching.MaxBatchSize     = maxBatchSize;
            opts.Batching.BatchWindow      = batchWindow;
            opts.Batching.BufferMultiplier = bufferMultiplier;

            opts.Checkpointing(c => {
                c.IntervalFrom(interval);
                c.RetentionCount = retentionCount;
                c.AutoTrack      = false;
                c.AutoCheckpoint = false;
                c.FailurePolicy  = CheckpointFailurePolicy.Fail;
            });

            opts.Subscription.Filter               = filter;
            opts.Subscription.InitialPosition      = SubscriptionInitialPosition.Earliest;
            opts.Subscription.PartitionKeySelector = selector;

            opts.Resilience.MaxRetryAttempts = maxRetryAttempts;
            opts.Resilience.BaseDelay        = baseDelay;
            opts.Resilience.MaxDelay         = maxDelay;
        }).Build();

        // Assert
        var options        = drone.Options;
        var actualEndpoint = EndpointOf(options.ClientOptions);

        await Assert.That(drone.ProcessorId).IsEqualTo(processorId);
        await Assert.That(drone.DroneId).IsEqualTo(droneId);
        await Assert.That(drone.Status).IsEqualTo(DroneStatus.Starting);
        await Assert.That(options.Debug).IsTrue();
        await Assert.That(options.SkipDecoding).IsTrue();
        await Assert.That(options.TrackSourceLag).IsTrue();
        await Assert.That(options.Batching.MaxBatchSize).IsEqualTo(maxBatchSize);
        await Assert.That(options.Batching.BatchWindow).IsEqualTo(batchWindow);
        await Assert.That(options.Batching.BufferMultiplier).IsEqualTo(bufferMultiplier);
        await Assert.That(options.Checkpoint.Interval).IsEqualTo(interval);
        await Assert.That(options.Checkpoint.RetentionCount).IsEqualTo(retentionCount);
        await Assert.That(options.Checkpoint.AutoTrack).IsFalse();
        await Assert.That(options.Checkpoint.AutoCheckpoint).IsFalse();
        await Assert.That(options.Checkpoint.FailurePolicy).IsEqualTo(CheckpointFailurePolicy.Fail);
        await Assert.That(options.Subscription.Filter).IsEqualTo(filter);
        await Assert.That(options.Subscription.Filter).IsTypeOf<ExpressionFilter>();
        await Assert.That(options.Subscription.InitialPosition).IsEqualTo(SubscriptionInitialPosition.Earliest);
        await Assert.That(options.Subscription.PartitionKeySelector).IsEqualTo(selector);
        await Assert.That(options.Resilience.MaxRetryAttempts).IsEqualTo(maxRetryAttempts);
        await Assert.That(options.Resilience.BaseDelay).IsEqualTo(baseDelay);
        await Assert.That(options.Resilience.MaxDelay).IsEqualTo(maxDelay);
        await Assert.That(actualEndpoint).IsEqualTo(expectedEndpoint);
    }

    [Test]
    [OutOfRangeValues]
    public async ValueTask rejects_out_of_range_values(Action<ProcessorOptions> corrupt) {
        // Arrange
        var options = ValidOptions();
        corrupt(options);

        // Act
        var result = options.Validate();

        // Assert
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    [ValidValues]
    public async ValueTask accepts_valid_values(Action<ProcessorOptions> configure) {
        // Arrange
        var options = ValidOptions();
        configure(options);

        // Act
        var result = options.Validate();

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async ValueTask build_validates_and_derives_the_id() {
        // Arrange
        var expectedId     = "testable-record-processor";
        var expectedPrefix = $"{expectedId}-";

        var unnamed = KurrentProcessor.Create(new TestableRecordProcessor(), opts => opts.Connection(TestConnectionString));

        var invalid = KurrentProcessor.Create(new TestableRecordProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.Batching.MaxBatchSize = 0;
        });

        var unconnected = KurrentProcessor.Create(new TestableRecordProcessor());

        // Act
        var drone = unnamed.Build();

        // Assert
        await Assert.That(drone.ProcessorId).IsEqualTo(expectedId);
        await Assert.That(drone.DroneId).StartsWith(expectedPrefix);
        await Assert.That(() => invalid.Build()).Throws<ValidationException>();
        await Assert.That(() => unconnected.Build()).Throws<InvalidOperationException>();
    }

    #endregion // Options

    #region ->> Hosting <<-

    [Test]
    public async ValueTask registers_one_hosted_service_per_processor_id() {
        // Arrange
        var services = NewServices();
        var firstId  = Faker.Random.AlphaNumeric(12);
        var secondId = Faker.Random.AlphaNumeric(12);

        Action<ProcessorOptions> connect = static opts => opts.Connection(TestConnectionString);

        // Act
        services.AddKurrentSurge(surge => surge.AddProcessor<TestableRecordProcessor>(firstId, connect));
        services.AddKurrentSurge(surge => surge.AddProcessor<TestableRecordProcessor>(secondId, connect));
        services.AddKurrentSurge(surge => surge.AddBatchProcessor<TestableBatchProcessor>(connect));

        // Assert
        var descriptors = services.Count(d => d.ServiceType == typeof(IHostedService) && d.ImplementationFactory is not null);
        var hosted      = services.BuildServiceProvider().GetServices<IHostedService>().ToList();
        var records     = hosted.OfType<DroneService<TestableRecordProcessor>>().Count();
        var batches     = hosted.OfType<DroneService<TestableBatchProcessor>>().Count();

        await Assert.That(descriptors).IsEqualTo(3);
        await Assert.That(records).IsEqualTo(2);
        await Assert.That(batches).IsEqualTo(1);
    }

    [Test]
    [Shapes]
    public async ValueTask reads_the_processor_section(Shape shape) {
        // Arrange
        var droneId          = Faker.Random.AlphaNumeric(12);
        var maxBatchSize     = Faker.Random.Int(1, 10_000);
        var batchWindowMs    = Faker.Random.Int(1, 5_000);
        var bufferMultiplier = Faker.Random.Int(1, 100);
        var intervalMs       = Faker.Random.Int(1_000, 300_000);
        var retentionCount   = Faker.Random.Int(1, 100);
        var maxRetryAttempts = Faker.Random.Int(0, 100);
        var baseDelayMs      = Faker.Random.Int(100, 1_000);
        var maxDelayMs       = Faker.Random.Int(1_000, 60_000);
        var connectionString = NewConnectionString();

        var expectedBatchWindow = TimeSpan.FromMilliseconds(batchWindowMs);
        var expectedInterval    = TimeSpan.FromMilliseconds(intervalMs);
        var expectedBaseDelay   = TimeSpan.FromMilliseconds(baseDelayMs);
        var expectedMaxDelay    = TimeSpan.FromMilliseconds(maxDelayMs);
        var expectedEndpoint    = EndpointOf(KurrentClientOptions.Parse(connectionString));

        var services = NewServices(Configuration(new() {
            [Key("DroneId")]                      = droneId,
            [Key("Debug")]                        = "true",
            [Key("SkipDecoding")]                 = "true",
            [Key("TrackSourceLag")]               = "true",
            [Key("Batching:MaxBatchSize")]        = $"{maxBatchSize}",
            [Key("Batching:BatchWindow")]         = $"{batchWindowMs}",
            [Key("Batching:BufferMultiplier")]    = $"{bufferMultiplier}",
            [Key("Checkpoint:AutoTrack")]         = "false",
            [Key("Checkpoint:AutoCheckpoint")]    = "false",
            [Key("Checkpoint:Interval")]          = $"{intervalMs}",
            [Key("Checkpoint:RetentionCount")]    = $"{retentionCount}",
            [Key("Checkpoint:FailurePolicy")]     = nameof(CheckpointFailurePolicy.Fail),
            [Key("Subscription:InitialPosition")] = nameof(SubscriptionInitialPosition.Earliest),
            [Key("Resilience:MaxRetryAttempts")]  = $"{maxRetryAttempts}",
            [Key("Resilience:BaseDelay")]         = $"{baseDelayMs}",
            [Key("Resilience:MaxDelay")]          = $"{maxDelayMs}",
            [Key("ConnectionString")]             = connectionString
        }));

        // Act
        services.AddKurrentSurge(surge => shape.Register(surge));

        var options = shape.Options(services);

        // Assert
        var actualEndpoint = EndpointOf(options.ClientOptions);

        await Assert.That(options.ProcessorId).IsEqualTo(shape.ExpectedId);
        await Assert.That(options.DroneId).IsEqualTo(droneId);
        await Assert.That(options.Debug).IsTrue();
        await Assert.That(options.SkipDecoding).IsTrue();
        await Assert.That(options.TrackSourceLag).IsTrue();
        await Assert.That(options.Batching.MaxBatchSize).IsEqualTo(maxBatchSize);
        await Assert.That(options.Batching.BatchWindow).IsEqualTo(expectedBatchWindow);
        await Assert.That(options.Batching.BufferMultiplier).IsEqualTo(bufferMultiplier);
        await Assert.That(options.Checkpoint.AutoTrack).IsFalse();
        await Assert.That(options.Checkpoint.AutoCheckpoint).IsFalse();
        await Assert.That(options.Checkpoint.Interval).IsEqualTo(expectedInterval);
        await Assert.That(options.Checkpoint.RetentionCount).IsEqualTo(retentionCount);
        await Assert.That(options.Checkpoint.FailurePolicy).IsEqualTo(CheckpointFailurePolicy.Fail);
        await Assert.That(options.Subscription.InitialPosition).IsEqualTo(SubscriptionInitialPosition.Earliest);
        await Assert.That(options.Resilience.MaxRetryAttempts).IsEqualTo(maxRetryAttempts);
        await Assert.That(options.Resilience.BaseDelay).IsEqualTo(expectedBaseDelay);
        await Assert.That(options.Resilience.MaxDelay).IsEqualTo(expectedMaxDelay);
        await Assert.That(actualEndpoint).IsEqualTo(expectedEndpoint);

        string Key(string path) {
            var key = $"Kurrent:Surge:Processors:{shape.SectionName}:{path}";
            return shape.CamelCaseKeys ? CamelCase(key) : key;
        }
    }

    [Test]
    [ConnectionPrecedence]
    public async ValueTask resolves_the_connection_in_precedence_order(Precedence precedence) {
        // Arrange
        var processorId = Faker.Random.AlphaNumeric(12);
        var changedId   = Faker.Random.AlphaNumeric(12);
        var connections = Enum.GetValues<ConnectionSource>().ToDictionary(source => source, _ => NewConnectionString());
        var expected    = EndpointOf(KurrentClientOptions.Parse(connections[precedence.Winner]));

        var settings = new Dictionary<string, string?>();

        if (precedence.Has(ConnectionSource.Section))
            settings[$"Kurrent:Surge:Processors:{processorId}:ConnectionString"] = connections[ConnectionSource.Section];

        if (precedence.Has(ConnectionSource.SectionOfIdSetInLambda))
            settings[$"Kurrent:Surge:Processors:{changedId}:ConnectionString"] = connections[ConnectionSource.SectionOfIdSetInLambda];

        if (precedence.Has(ConnectionSource.SharedSurge))
            settings["ConnectionStrings:Kurrent:Surge"] = connections[ConnectionSource.SharedSurge];

        if (precedence.Has(ConnectionSource.SharedKurrentDB))
            settings["ConnectionStrings:kurrent-db"] = connections[ConnectionSource.SharedKurrentDB];

        var services = NewServices(Configuration(settings));

        // Act
        services.AddKurrentSurge(surge => {
            if (precedence.Has(ConnectionSource.Scope))
                surge.Connection(connections[ConnectionSource.Scope]);

            surge.AddProcessor<TestableRecordProcessor>(processorId, opts => {
                if (precedence.Has(ConnectionSource.SectionOfIdSetInLambda))
                    opts.ProcessorId = changedId;

                if (precedence.Has(ConnectionSource.Lambda))
                    opts.Connection(connections[ConnectionSource.Lambda]);
            });
        });

        // Assert
        var actual = EndpointOf(DroneOptions<TestableRecordProcessor>(services).ClientOptions);

        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask configure_delegate_sees_configuration_and_wins() {
        // Arrange
        var processorId        = Faker.Random.AlphaNumeric(12);
        var changedId          = Faker.Random.AlphaNumeric(12);
        var configuredBatch    = Faker.Random.Int(1, 5_000);
        var unreadBatch        = Faker.Random.Int(5_001, 10_000);
        var configuredInterval = Faker.Random.Int(1_000, 100_000);
        var overriddenInterval = TimeSpan.FromMilliseconds(Faker.Random.Int(100_001, 200_000));
        var connectionString   = NewConnectionString();
        var expectedEndpoint   = EndpointOf(KurrentClientOptions.Parse(connectionString));

        var services = NewServices(Configuration(new() {
            [$"Kurrent:Surge:Processors:{processorId}:Batching:MaxBatchSize"] = $"{configuredBatch}",
            [$"Kurrent:Surge:Processors:{processorId}:Checkpoint:Interval"]   = $"{configuredInterval}",
            [$"Kurrent:Surge:Processors:{changedId}:Batching:MaxBatchSize"]   = $"{unreadBatch}",
            ["ConnectionStrings:KurrentDB"]                                    = connectionString
        }));

        services.AddSingleton(new TestSettings { ProcessorId = changedId });

        var          seenBatch    = 0;
        DnsEndPoint? seenEndpoint = null;

        // Act
        services.AddKurrentSurge(surge => surge.AddProcessor<TestableRecordProcessor>(processorId, (opts, sp) => {
            seenBatch    = opts.Batching.MaxBatchSize;
            seenEndpoint = EndpointOf(opts.ClientOptions);

            opts.ProcessorId         = sp.GetRequiredService<TestSettings>().ProcessorId;
            opts.Checkpoint.Interval = overriddenInterval;
        }));

        var options = DroneOptions<TestableRecordProcessor>(services);

        // Assert
        await Assert.That(seenBatch).IsEqualTo(configuredBatch);
        await Assert.That(seenEndpoint).IsEqualTo(expectedEndpoint);
        await Assert.That(options.ProcessorId).IsEqualTo(changedId);
        await Assert.That(options.Batching.MaxBatchSize).IsEqualTo(configuredBatch);
        await Assert.That(options.Checkpoint.Interval).IsEqualTo(overriddenInterval);
    }

    [Test]
    [WithoutConfiguration]
    public async ValueTask uses_defaults_without_configuration(Action<IServiceCollection> arrange) {
        // Arrange
        var defaults = new ProcessorOptions();
        var services = NewServices();

        arrange(services);

        // Act
        services.AddKurrentSurge(surge => surge.AddProcessor<TestableRecordProcessor>(opts => opts.Connection(TestConnectionString)));

        var options = DroneOptions<TestableRecordProcessor>(services);

        // Assert
        await Assert.That(options.SkipDecoding).IsEqualTo(defaults.SkipDecoding);
        await Assert.That(options.Batching.MaxBatchSize).IsEqualTo(defaults.Batching.MaxBatchSize);
        await Assert.That(options.Batching.BatchWindow).IsEqualTo(defaults.Batching.BatchWindow);
        await Assert.That(options.Batching.BufferMultiplier).IsEqualTo(defaults.Batching.BufferMultiplier);
        await Assert.That(options.Checkpoint.Interval).IsEqualTo(defaults.Checkpoint.Interval);
        await Assert.That(options.Subscription.InitialPosition).IsEqualTo(defaults.Subscription.InitialPosition);
    }

    [Test]
    public async ValueTask ignores_a_processor_id_key_and_reports_it() {
        // Arrange
        var processorId  = Faker.Random.AlphaNumeric(12);
        var configuredId = Faker.Random.AlphaNumeric(12);
        var expectedPath = $"Kurrent:Surge:Processors:{processorId}:ProcessorId";

        var configuration = Configuration(new() {
            [expectedPath]                  = configuredId,
            ["ConnectionStrings:KurrentDB"] = TestConnectionString
        });

        var options  = new ProcessorOptions { ProcessorId = processorId };
        var services = NewServices(configuration);

        // Act
        var read = options.FromConfiguration(configuration, processorId);

        services.AddKurrentSurge(surge => surge.AddProcessor<TestableRecordProcessor>(processorId));

        var hosted = DroneOptions<TestableRecordProcessor>(services);

        // Assert
        var warnings = read.Problems.Where(p => p.Severity == ConfigurationProblemSeverity.Warning).Select(p => p.Path).ToList();
        var readId   = options.ProcessorId;
        var hostedId = hosted.ProcessorId;

        await Assert.That(warnings).Contains(expectedPath);
        await Assert.That(readId).IsEqualTo(processorId);
        await Assert.That(hostedId).IsEqualTo(processorId);
    }

    [Test]
    public async ValueTask unusable_configuration_value_fails_drone_materialization() {
        // Arrange
        var processorId = Faker.Random.AlphaNumeric(12);

        var services = NewServices(Configuration(new() {
            [$"Kurrent:Surge:Processors:{processorId}:Checkpoint:Interval"] = "not-a-duration",
            ["ConnectionStrings:KurrentDB"]                                 = TestConnectionString
        }));

        services.AddKurrentSurge(surge => surge.AddProcessor<TestableRecordProcessor>(processorId));

        var provider = services.BuildServiceProvider();

        // Act
        var materialize = () => provider.GetServices<IHostedService>().ToList();

        // Assert
        await Assert.That(materialize).Throws<InvalidOperationException>();
    }

    #endregion // Hosting

    #region ->> Data <<-

    public class OutOfRangeValues : TestCaseDataGenerator<Action<ProcessorOptions>> {
        protected override IEnumerable<TestCase<Action<ProcessorOptions>>> Data() {
            yield return new(static o => o.ProcessorId = "", DisplayName: "empty ProcessorId");
            yield return new(static o => o.MaxParallelism = 0, DisplayName: "MaxParallelism 0");
            yield return new(static o => o.MaxParallelism = -1, DisplayName: "MaxParallelism -1");
            yield return new(static o => o.MaxParallelism = 32769, DisplayName: "MaxParallelism 32769");
            yield return new(static o => o.MaxParallelism = 4, DisplayName: "MaxParallelism 4 without PartitionKeySelector");
            yield return new(static o => o.Batching.MaxBatchSize = 0, DisplayName: "MaxBatchSize 0");
            yield return new(static o => o.Batching.MaxBatchSize = 10001, DisplayName: "MaxBatchSize 10001");
            yield return new(static o => o.Batching.BatchWindow = TimeSpan.Zero, DisplayName: "BatchWindow zero");
            yield return new(static o => o.Batching.BufferMultiplier = 0, DisplayName: "BufferMultiplier 0");
            yield return new(static o => o.Batching.BufferMultiplier = 101, DisplayName: "BufferMultiplier 101");
            yield return new(static o => o.Checkpoint.Interval = TimeSpan.Zero, DisplayName: "Checkpoint.Interval zero");
        }
    }

    public class ValidValues : TestCaseDataGenerator<Action<ProcessorOptions>> {
        protected override IEnumerable<TestCase<Action<ProcessorOptions>>> Data() {
            yield return new(static _ => { }, DisplayName: "defaults with an id and a connection");
            yield return new(static o => {
                o.MaxParallelism                    = 4;
                o.Subscription.PartitionKeySelector = "stream";
            }, DisplayName: "MaxParallelism 4 with PartitionKeySelector");
        }
    }

    public sealed record Shape(
        string SectionName,
        string ExpectedId,
        bool CamelCaseKeys,
        Func<IKurrentSurgeBuilder, IKurrentSurgeBuilder> Register,
        Func<IServiceCollection, ProcessorOptions> Options);

    public class Shapes : TestCaseDataGenerator<Shape> {
        protected override IEnumerable<TestCase<Shape>> Data() {
            yield return new(new(
                nameof(TestableRecordProcessor), "testable-record-processor", CamelCaseKeys: false,
                static surge => surge.AddProcessor<TestableRecordProcessor>(),
                DroneOptions<TestableRecordProcessor>), DisplayName: "record, PascalCase keys");

            yield return new(new(
                nameof(TestableBatchProcessor), "testable-batch-processor", CamelCaseKeys: true,
                static surge => surge.AddBatchProcessor<TestableBatchProcessor>(),
                DroneOptions<TestableBatchProcessor>), DisplayName: "batch, camelCase keys");
        }
    }

    public enum ConnectionSource { Lambda, Section, Scope, SharedSurge, SharedKurrentDB, SectionOfIdSetInLambda }

    public sealed record Precedence(ConnectionSource[] Present, ConnectionSource Winner) {
        public bool Has(ConnectionSource source) => Present.Contains(source);
    }

    public class ConnectionPrecedence : TestCaseDataGenerator<Precedence> {
        protected override IEnumerable<TestCase<Precedence>> Data() {
            yield return new(new([ConnectionSource.Lambda, ConnectionSource.Section, ConnectionSource.Scope, ConnectionSource.SharedSurge, ConnectionSource.SharedKurrentDB], ConnectionSource.Lambda), DisplayName: "lambda beats the section");
            yield return new(new([ConnectionSource.Section, ConnectionSource.Scope, ConnectionSource.SharedSurge, ConnectionSource.SharedKurrentDB], ConnectionSource.Section), DisplayName: "section beats the scope");
            yield return new(new([ConnectionSource.Scope, ConnectionSource.SharedSurge, ConnectionSource.SharedKurrentDB], ConnectionSource.Scope), DisplayName: "scope beats the shared Kurrent Surge key");
            yield return new(new([ConnectionSource.SharedSurge, ConnectionSource.SharedKurrentDB], ConnectionSource.SharedSurge), DisplayName: "shared Kurrent Surge key beats KurrentDB");
            yield return new(new([ConnectionSource.SharedKurrentDB], ConnectionSource.SharedKurrentDB), DisplayName: "KurrentDB alone, spelling-insensitive");
            yield return new(new([ConnectionSource.SectionOfIdSetInLambda, ConnectionSource.SharedKurrentDB], ConnectionSource.SharedKurrentDB), DisplayName: "an id set in the lambda reads no connection from that section");
        }
    }

    public class WithoutConfiguration : TestCaseDataGenerator<Action<IServiceCollection>> {
        protected override IEnumerable<TestCase<Action<IServiceCollection>>> Data() {
            yield return new(static _ => { }, DisplayName: "no IConfiguration registered");
            yield return new(static services => services.AddSingleton(Configuration(new() { ["SomeOther:Section"] = "value" })), DisplayName: "no processor section");
        }
    }

    #endregion // Data

    #region ->> Helpers <<-

    string NewConnectionString() => $"kurrentdb://node-{Faker.Random.Hash(8)}:2113";

    ProcessorOptions ValidOptions() {
        var options = new ProcessorOptions { ProcessorId = Faker.Random.AlphaNumeric(12) };
        options.Connection(TestConnectionString);
        return options;
    }

    static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    static ServiceCollection NewServices(IConfiguration? configuration = null) {
        var services = new ServiceCollection();
        services.AddLogging();

        if (configuration is not null)
            services.AddSingleton(configuration);

        return services;
    }

    static ProcessorOptions DroneOptions<TProcessor>(IServiceCollection services) where TProcessor : class, IProcessor =>
        services.BuildServiceProvider().GetServices<IHostedService>().OfType<DroneService<TProcessor>>().Single().Drone.Options;

    static DnsEndPoint? EndpointOf(IKurrentClientOptions? client) =>
        client is KurrentClientOptions options ? options.Endpoints.Single() : null;

    static string CamelCase(string path) =>
        string.Join(':', path.Split(':').Select(segment => $"{char.ToLowerInvariant(segment[0])}{segment[1..]}"));

    record TestSettings {
        public string ProcessorId { get; init; } = "";
    }

    sealed class TestableBatchProcessor : RecordBatchProcessor {
        public override ValueTask ProcessBatch(BatchContext context) => ValueTask.CompletedTask;
    }

    #endregion // Helpers
}
