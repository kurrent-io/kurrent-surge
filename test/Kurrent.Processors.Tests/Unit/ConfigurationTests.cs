using System.Text;
using Kurrent.Client;
using Kurrent.Client.Streams;
using Kurrent.Processors.Models;
using Kurrent.Processors.Options;
using Kurrent.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kurrent.Processors.Tests;

/// <summary>
/// Unit tests for processor configuration, validation, and worker creation.
/// </summary>
[Category("Processors")]
public class ConfigurationTests {
    #region Default Values Tests

    [Test]
    public async Task default_processor_id_is_empty() {
        var settings = new ProcessorSettings();
        await Assert.That(settings.ProcessorId).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task default_worker_id_is_not_empty() {
        var settings = new ProcessorSettings();
        await Assert.That(settings.WorkerId).IsNotNull().And.IsNotEmpty();
    }

    [Test]
    public async Task default_buffer_capacity_is_1000() {
        var settings = new ProcessorSettings();
        await Assert.That(settings.Worker.BufferCapacity).IsEqualTo(1000);
    }

    [Test]
    public async Task default_batching_max_size_is_200() {
        var settings = new ProcessorSettings();
        await Assert.That(settings.Batching.MaxSize).IsEqualTo(200);
    }

    [Test]
    public async Task default_batching_timeout_is_250() {
        var settings = new ProcessorSettings();
        await Assert.That(settings.Batching.Timeout).IsEqualTo(250);
    }

    [Test]
    public async Task default_checkpoint_interval_is_30000() {
        var settings = new ProcessorSettings();
        await Assert.That(settings.Checkpoint.Interval).IsEqualTo(30000);
    }

    [Test]
    public async Task default_checkpoint_timeout_is_30000() {
        var settings = new ProcessorSettings();
        await Assert.That(settings.Checkpoint.Timeout).IsEqualTo(30000);
    }

    [Test]
    public async Task default_initial_position_is_latest() {
        var settings = new ProcessorSettings();
        await Assert.That(settings.Subscription.InitialPosition).IsEqualTo(SubscriptionInitialPosition.Latest);
    }

    [Test]
    public async Task default_skip_decoding_is_false() {
        var settings = new ProcessorSettings();
        await Assert.That(settings.Worker.SkipDecoding).IsFalse();
    }

    #endregion

    #region Builder Configuration Tests

    [Test]
    public async Task with_processor_id_sets_settings_processor_id() {
        var processorId = "test-processor-id";

        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId(processorId)
            .WithMaxParallelism(1)
            .Build();

        await Assert.That(worker.Settings.ProcessorId).IsEqualTo(processorId);
        await Assert.That(worker.ProcessorId).IsEqualTo(processorId);
    }

    [Test]
    public async Task with_worker_id_sets_settings_worker_id() {
        var workerId = "test-worker-id";

        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("test-processor")
            .WithWorkerId(workerId)
            .WithMaxParallelism(1)
            .Build();

        await Assert.That(worker.Settings.WorkerId).IsEqualTo(workerId);
        await Assert.That(worker.WorkerId).IsEqualTo(workerId);
    }

    [Test]
    public async Task without_worker_id_generates_unique_id() {
        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("test-processor")
            .WithMaxParallelism(1)
            .Build();

        await Assert.That(worker.WorkerId).IsNotNull().And.IsNotEmpty();
    }

    [Test]
    public async Task with_max_parallelism_sets_settings() {
        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("test-processor")
            .WithMaxParallelism(1)
            .Build();

        await Assert.That(worker.Settings.MaxParallelism).IsEqualTo(1);
    }

    [Test]
    public async Task configure_worker_sets_buffer_capacity() {
        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("test-processor")
            .WithMaxParallelism(1)
            .ConfigureWorker(w => w.BufferCapacity(5000))
            .Build();

        await Assert.That(worker.Settings.Worker.BufferCapacity).IsEqualTo(5000);
    }

    [Test]
    public async Task configure_batching_sets_settings() {
        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("test-processor")
            .WithMaxParallelism(1)
            .ConfigureBatching(b => b.MaxSize(200).Timeout(500))
            .Build();

        await Assert.That(worker.Settings.Batching.MaxSize).IsEqualTo(200);
        await Assert.That(worker.Settings.Batching.Timeout).IsEqualTo(500);
    }

    [Test]
    public async Task configure_checkpointing_sets_settings() {
        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("test-processor")
            .WithMaxParallelism(1)
            .ConfigureCheckpointing(c => c.Interval(60000).Timeout(30000).RetentionCount(5))
            .Build();

        await Assert.That(worker.Settings.Checkpoint.Interval).IsEqualTo(60000);
        await Assert.That(worker.Settings.Checkpoint.Timeout).IsEqualTo(30000);
        await Assert.That(worker.Settings.Checkpoint.RetentionCount).IsEqualTo(5);
    }

    [Test]
    public async Task with_initial_position_sets_settings() {
        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("test-processor")
            .WithMaxParallelism(1)
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .Build();

        await Assert.That(worker.Settings.Subscription.InitialPosition).IsEqualTo(SubscriptionInitialPosition.Earliest);
    }

    [Test]
    public async Task with_filter_sets_settings() {
        var filter = RecordFilter.ByStream("test-stream");

        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("test-processor")
            .WithMaxParallelism(1)
            .WithFilter(filter)
            .Build();

        await Assert.That(worker.Settings.Subscription.Filter).IsEqualTo(filter);
    }

    [Test]
    public async Task with_filter_expression_sets_both_expression_and_filter() {
        var expression = "stream_id LIKE 'orders-%'";

        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("test-processor")
            .WithMaxParallelism(1)
            .WithFilterExpression(expression)
            .Build();

        await Assert.That(worker.Settings.Subscription.FilterExpression).IsEqualTo(expression);
        await Assert.That(worker.Settings.Subscription.Filter).IsNotNull();
        await Assert.That(worker.Settings.Subscription.Filter).IsTypeOf<ExpressionFilter>();
    }

    [Test]
    public async Task with_partition_key_selector_sets_settings() {
        var selector = "data.customer_id";

        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("test-processor")
            .WithMaxParallelism(1)
            .WithPartitionKeySelector(selector)
            .Build();

        await Assert.That(worker.Settings.Subscription.PartitionKeySelector).IsEqualTo(selector);
    }

    [Test]
    public async Task configure_worker_sets_skip_decoding() {
        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("test-processor")
            .WithMaxParallelism(1)
            .ConfigureWorker(w => w.SkipDecoding())
            .Build();

        await Assert.That(worker.Settings.Worker.SkipDecoding).IsTrue();
    }

    [Test]
    public async Task configure_multiple_settings_applies_all() {
        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("test-processor")
            .WithMaxParallelism(1)
            .ConfigureWorker(w => w.BufferCapacity(2000))
            .ConfigureBatching(b => b.MaxSize(50).Timeout(200))
            .Build();

        await Assert.That(worker.Settings.MaxParallelism).IsEqualTo(1);
        await Assert.That(worker.Settings.Worker.BufferCapacity).IsEqualTo(2000);
        await Assert.That(worker.Settings.Batching.MaxSize).IsEqualTo(50);
        await Assert.That(worker.Settings.Batching.Timeout).IsEqualTo(200);
    }

    #endregion

    #region Worker Creation Tests

    [Test]
    public async Task build_returns_worker_with_correct_settings() {
        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("test-processor")
            .WithWorkerId("test-worker")
            .WithMaxParallelism(1)
            .Build();

        await Assert.That(worker).IsNotNull();
        await Assert.That(worker.ProcessorId).IsEqualTo("test-processor");
        await Assert.That(worker.WorkerId).IsEqualTo("test-worker");
        await Assert.That(worker.Settings).IsNotNull();
    }

    [Test]
    public async Task worker_initial_status_is_starting() {
        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("test-processor")
            .WithMaxParallelism(1)
            .Build();

        await Assert.That(worker.Status).IsEqualTo(ProcessorWorkerStatus.Starting);
    }

    #endregion

    #region Validation Tests

    [Test]
    public async Task valid_configuration_passes_validation() {
        var settings = new ProcessorSettings {
            ProcessorId    = "valid-processor",
            MaxParallelism = 1,
            Worker         = new WorkerSettings { BufferCapacity = 1000 }
        };

        var result = settings.Validate();

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task empty_processor_id_fails_validation() {
        var settings = new ProcessorSettings {
            ProcessorId    = string.Empty,
            MaxParallelism = 1
        };

        var result = settings.Validate();

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task invalid_max_parallelism_zero_fails_validation() {
        var settings = new ProcessorSettings {
            ProcessorId    = "test-processor",
            MaxParallelism = 0
        };

        var result = settings.Validate();

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task invalid_max_parallelism_negative_fails_validation() {
        var settings = new ProcessorSettings {
            ProcessorId    = "test-processor",
            MaxParallelism = -1
        };

        var result = settings.Validate();

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task invalid_max_parallelism_exceeds_limit_fails_validation() {
        var settings = new ProcessorSettings {
            ProcessorId    = "test-processor",
            MaxParallelism = 32769
        };

        var result = settings.Validate();

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task invalid_buffer_capacity_zero_fails_validation() {
        var settings = new ProcessorSettings {
            ProcessorId    = "test-processor",
            MaxParallelism = 1,
            Worker         = new WorkerSettings { BufferCapacity = 0 }
        };

        var result = settings.Validate();

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task invalid_buffer_capacity_exceeds_limit_fails_validation() {
        var settings = new ProcessorSettings {
            ProcessorId    = "test-processor",
            MaxParallelism = 1,
            Worker         = new WorkerSettings { BufferCapacity = 10001 }
        };

        var result = settings.Validate();

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task invalid_batching_max_size_fails_validation() {
        var settings = new ProcessorSettings {
            ProcessorId    = "test-processor",
            MaxParallelism = 1,
            Batching       = new BatchingSettings { MaxSize = 0, Timeout = 100 }
        };

        var result = settings.Validate();

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task invalid_batching_timeout_fails_validation() {
        var settings = new ProcessorSettings {
            ProcessorId    = "test-processor",
            MaxParallelism = 1,
            Batching       = new BatchingSettings { MaxSize = 100, Timeout = 0 }
        };

        var result = settings.Validate();

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task invalid_checkpoint_interval_fails_validation() {
        var settings = new ProcessorSettings {
            ProcessorId    = "test-processor",
            MaxParallelism = 1,
            Checkpoint     = new CheckpointSettings { Interval = 0 }
        };

        var result = settings.Validate();

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task max_parallelism_without_partition_key_passes_with_warning() {
        // PartitionKeySelector is a warning, not an error, for backward compatibility
        var settings = new ProcessorSettings {
            ProcessorId    = "test-processor",
            MaxParallelism = 4,
            Subscription   = new SubscriptionSettings { PartitionKeySelector = string.Empty }
        };

        var result = settings.Validate();

        // Validation passes (warnings are ignored), but the warning is generated
        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task max_parallelism_with_partition_key_passes_validation() {
        var settings = new ProcessorSettings {
            ProcessorId    = "test-processor",
            MaxParallelism = 4,
            Subscription   = new SubscriptionSettings { PartitionKeySelector = "stream_id" }
        };

        var result = settings.Validate();

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task build_with_invalid_buffer_capacity_throws() {
        await Assert.That(() =>
            KurrentProcessors.Create(new NoOpProcessor())
                .ConnectTo("kurrentdb://localhost:2113")
                .WithProcessorId("test-processor")
                .WithMaxParallelism(1)
                .ConfigureWorker(w => w.BufferCapacity(0))
                .Build()
        ).Throws<ValidationException>();
    }

    [Test]
    public async Task build_without_processor_id_auto_generates_from_type_name() {
        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithMaxParallelism(1)
            .Build();

        await Assert.That(worker.ProcessorId).IsEqualTo("no_op_processor");
    }

    [Test]
    public async Task build_with_invalid_max_parallelism_throws() {
        await Assert.That(() =>
            KurrentProcessors.Create(new NoOpProcessor())
                .ConnectTo("kurrentdb://localhost:2113")
                .WithProcessorId("test-processor")
                .WithMaxParallelism(0)
                .Build()
        ).Throws<ValidationException>();
    }

    #endregion

    #region DI Registration Tests

    [Test]
    public async Task duplicate_registration_is_idempotent() {
        var services = new ServiceCollection();
        services.AddKurrentClient("kurrentdb://localhost:2113");

        // Register the same processor type twice
        services.AddKurrentProcessor<NoOpProcessor>(c => c.WithProcessorId("first"));
        services.AddKurrentProcessor<NoOpProcessor>(c => c.WithProcessorId("second"));

        // Should only have one hosted service registered
        var hostedServiceCount = services.Count(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationFactory is not null);

        await Assert.That(hostedServiceCount).IsEqualTo(1);
    }

    [Test]
    public async Task duplicate_registration_with_factory_is_idempotent() {
        var services = new ServiceCollection();
        services.AddKurrentClient("kurrentdb://localhost:2113");

        // Register the same processor type twice using factory overload
        services.AddKurrentProcessor<NoOpProcessor>(
            _ => new NoOpProcessor(),
            c => c.WithProcessorId("first"));
        services.AddKurrentProcessor<NoOpProcessor>(
            _ => new NoOpProcessor(),
            c => c.WithProcessorId("second"));

        // Should only have one hosted service registered
        var hostedServiceCount = services.Count(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationFactory is not null);

        await Assert.That(hostedServiceCount).IsEqualTo(1);
    }

    [Test]
    public async Task configuration_with_service_provider_can_access_services() {
        var services = new ServiceCollection();
        services.AddSingleton(new TestSettings { ProcessorId = "configured-processor-id" });
        services.AddLogging();
        services.AddKurrentClient("kurrentdb://localhost:2113");

        string? capturedId = null;

        services.AddKurrentProcessor<NoOpProcessor>((config, sp) => {
            var settings = sp.GetRequiredService<TestSettings>();
            capturedId = settings.ProcessorId;
            config.WithProcessorId(capturedId);
        });

        // Build the service provider to trigger factory invocation
        var provider = services.BuildServiceProvider();

        // Get the hosted service to trigger factory
        _ = provider.GetServices<IHostedService>().ToList();

        // The processor ID should have been captured from TestSettings
        await Assert.That(capturedId).IsEqualTo("configured-processor-id");
    }

    record TestSettings {
        public string ProcessorId { get; init; } = string.Empty;
    }

    #endregion

    #region Configuration Binding Tests

    [Test]
    public async Task settings_loaded_from_json_configuration_by_processor_type_name() {
        const string json = """
            {
              "Kurrent": {
                "Processors": {
                  "NoOpProcessor": {
                    "ProcessorId": "config-processor",
                    "MaxParallelism": 8,
                    "Worker": {
                      "BufferCapacity": 2000
                    },
                    "Batching": {
                      "MaxSize": 100,
                      "Timeout": 500
                    },
                    "Checkpoint": {
                      "Interval": 15000
                    }
                  }
                }
              }
            }
            """;

        var configuration = BuildJsonConfiguration(json);
        var settings = ResolveProcessorSettings(configuration);

        await Assert.That(settings.ProcessorId).IsEqualTo("config-processor");
        await Assert.That(settings.MaxParallelism).IsEqualTo(8);
        await Assert.That(settings.Worker.BufferCapacity).IsEqualTo(2000);
        await Assert.That(settings.Batching.MaxSize).IsEqualTo(100);
        await Assert.That(settings.Batching.Timeout).IsEqualTo(500);
        await Assert.That(settings.Checkpoint.Interval).IsEqualTo(15000);
    }

    [Test]
    public async Task json_configuration_supports_camel_case_properties() {
        const string json = """
            {
              "Kurrent": {
                "Processors": {
                  "NoOpProcessor": {
                    "processorId": "camel-processor",
                    "maxParallelism": 4,
                    "worker": {
                      "bufferCapacity": 1500
                    },
                    "batching": {
                      "maxSize": 75,
                      "timeout": 300
                    },
                    "checkpoint": {
                      "interval": 20000
                    },
                    "subscription": {
                      "filterExpression": "stream_id LIKE 'test-%'",
                      "initialPosition": "Earliest",
                      "partitionKeySelector": "data.id"
                    }
                  }
                }
              }
            }
            """;

        var configuration = BuildJsonConfiguration(json);
        var settings = ResolveProcessorSettings(configuration);

        await Assert.That(settings.ProcessorId).IsEqualTo("camel-processor");
        await Assert.That(settings.MaxParallelism).IsEqualTo(4);
        await Assert.That(settings.Worker.BufferCapacity).IsEqualTo(1500);
        await Assert.That(settings.Batching.MaxSize).IsEqualTo(75);
        await Assert.That(settings.Batching.Timeout).IsEqualTo(300);
        await Assert.That(settings.Checkpoint.Interval).IsEqualTo(20000);
        await Assert.That(settings.Subscription.FilterExpression).IsEqualTo("stream_id LIKE 'test-%'");
        await Assert.That(settings.Subscription.InitialPosition).IsEqualTo(SubscriptionInitialPosition.Earliest);
        await Assert.That(settings.Subscription.PartitionKeySelector).IsEqualTo("data.id");
    }

    [Test]
    public async Task json_configuration_does_not_support_snake_case_properties() {
        // snake_case is NOT supported by default .NET configuration binding
        var json = """
            {
              "Kurrent": {
                "Processors": {
                  "NoOpProcessor": {
                    "processor_id": "snake-processor",
                    "max_parallelism": 4,
                    "worker": {
                      "buffer_capacity": 1500
                    }
                  }
                }
              }
            }
            """;

        var configuration = BuildJsonConfiguration(json);
        var settings = ResolveProcessorSettings(configuration, configure: c => c
            .WithProcessorId("fallback-processor")
            .WithMaxParallelism(1)
        );

        // snake_case keys are ignored - defaults/fluent values used instead
        await Assert.That(settings.ProcessorId).IsEqualTo("fallback-processor");
        await Assert.That(settings.MaxParallelism).IsEqualTo(1);
        await Assert.That(settings.Worker.BufferCapacity).IsEqualTo(1000); // default value
    }

    [Test]
    public async Task fluent_configuration_overrides_json_settings() {
        var json = """
            {
              "Kurrent": {
                "Processors": {
                  "NoOpProcessor": {
                    "ProcessorId": "config-processor",
                    "MaxParallelism": 8,
                    "Worker": {
                      "BufferCapacity": 2000
                    }
                  }
                }
              }
            }
            """;

        var configuration = BuildJsonConfiguration(json);
        var settings = ResolveProcessorSettings(configuration, configure: c => c
            .WithProcessorId("fluent-processor")
            .WithMaxParallelism(4)
        );

        // Fluent overrides config
        await Assert.That(settings.ProcessorId).IsEqualTo("fluent-processor");
        await Assert.That(settings.MaxParallelism).IsEqualTo(4);
        // Config value retained when no fluent override
        await Assert.That(settings.Worker.BufferCapacity).IsEqualTo(2000);
    }

    [Test]
    public async Task missing_json_section_uses_defaults() {
        var json = """
            {
              "SomeOther": {
                "Section": "value"
              }
            }
            """;

        var configuration = BuildJsonConfiguration(json);
        var settings = ResolveProcessorSettings(configuration, configure: c => c
            .WithProcessorId("test-processor")
            .WithMaxParallelism(1)
        );

        // Default values should be used
        await Assert.That(settings.Worker.BufferCapacity).IsEqualTo(1000);
        await Assert.That(settings.Batching.MaxSize).IsEqualTo(200);
        await Assert.That(settings.Batching.Timeout).IsEqualTo(250);
        await Assert.That(settings.Checkpoint.Interval).IsEqualTo(30000);
    }

    [Test]
    public async Task nested_subscription_settings_bind_from_json() {
        var json = """
            {
              "Kurrent": {
                "Processors": {
                  "NoOpProcessor": {
                    "ProcessorId": "test-processor",
                    "MaxParallelism": 1,
                    "Subscription": {
                      "FilterExpression": "stream_id LIKE 'orders-%'",
                      "InitialPosition": "Earliest",
                      "PartitionKeySelector": "data.customer_id"
                    }
                  }
                }
              }
            }
            """;

        var configuration = BuildJsonConfiguration(json);
        var settings = ResolveProcessorSettings(configuration);

        await Assert.That(settings.Subscription.FilterExpression).IsEqualTo("stream_id LIKE 'orders-%'");
        await Assert.That(settings.Subscription.InitialPosition).IsEqualTo(SubscriptionInitialPosition.Earliest);
        await Assert.That(settings.Subscription.PartitionKeySelector).IsEqualTo("data.customer_id");
    }

    [Test]
    public async Task configuration_binding_works_with_service_provider_overload() {
        var json = """
            {
              "Kurrent": {
                "Processors": {
                  "NoOpProcessor": {
                    "Worker": {
                      "BufferCapacity": 3000
                    }
                  }
                }
              }
            }
            """;

        var configuration = BuildJsonConfiguration(json);

        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddSingleton(new TestSettings { ProcessorId = "sp-processor" });
        services.AddLogging();
        services.AddKurrentClient("kurrentdb://localhost:2113");
        services.AddKurrentProcessor<NoOpProcessor>((config, sp) => {
            var testSettings = sp.GetRequiredService<TestSettings>();
            config
                .WithProcessorId(testSettings.ProcessorId)
                .WithMaxParallelism(1);
        });

        var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<KurrentProcessorHostedService<NoOpProcessor>>()
            .Single();

        var settings = hostedService.Worker.Settings;

        // From service provider overload
        await Assert.That(settings.ProcessorId).IsEqualTo("sp-processor");
        // From config
        await Assert.That(settings.Worker.BufferCapacity).IsEqualTo(3000);
    }

    [Test]
    public async Task no_configuration_registered_uses_defaults() {
        var services = new ServiceCollection();
        // Intentionally NOT registering IConfiguration
        services.AddLogging();
        services.AddKurrentClient("kurrentdb://localhost:2113");
        services.AddKurrentProcessor<NoOpProcessor>(config => config
            .WithProcessorId("test-processor")
            .WithMaxParallelism(1)
        );

        var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<KurrentProcessorHostedService<NoOpProcessor>>()
            .Single();

        var settings = hostedService.Worker.Settings;

        // Default values should be used
        await Assert.That(settings.Worker.BufferCapacity).IsEqualTo(1000);
        await Assert.That(settings.Batching.MaxSize).IsEqualTo(200);
    }

    static IConfiguration BuildJsonConfiguration(string json) {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();
    }

    static ProcessorSettings ResolveProcessorSettings(
        IConfiguration configuration,
        Action<KurrentProcessorHostBuilder>? configure = null) {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging();
        services.AddKurrentClient("kurrentdb://localhost:2113");
        services.AddKurrentProcessor<NoOpProcessor>(configure);

        var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<KurrentProcessorHostedService<NoOpProcessor>>()
            .Single();

        return hostedService.Worker.Settings;
    }

    [Test]
    public async Task processor_id_auto_generated_from_processor_type_name_when_not_in_config() {
        var json = """
            {
              "Kurrent": {
                "Processors": {
                  "NoOpProcessor": {
                    "MaxParallelism": 4,
                    "Worker": {
                      "BufferCapacity": 2000
                    }
                  }
                }
              }
            }
            """;

        var configuration = BuildJsonConfiguration(json);

        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging();
        services.AddKurrentClient("kurrentdb://localhost:2113");

        // Section name is automatically determined from processor type name
        services.AddKurrentProcessor<NoOpProcessor>(config => config
            .FromConfiguration(configuration)
        );

        var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<KurrentProcessorHostedService<NoOpProcessor>>()
            .Single();

        var settings = hostedService.Worker.Settings;

        // ProcessorId should be auto-generated from processor type name (section name)
        await Assert.That(settings.ProcessorId).IsEqualTo("no_op_processor");
        await Assert.That(settings.MaxParallelism).IsEqualTo(4);
        await Assert.That(settings.Worker.BufferCapacity).IsEqualTo(2000);
    }

    [Test]
    public async Task explicit_processor_id_in_config_overrides_auto_generation() {
        var json = """
            {
              "Kurrent": {
                "Processors": {
                  "NoOpProcessor": {
                    "ProcessorId": "my-custom-id",
                    "MaxParallelism": 4
                  }
                }
              }
            }
            """;

        var configuration = BuildJsonConfiguration(json);

        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging();
        services.AddKurrentClient("kurrentdb://localhost:2113");
        services.AddKurrentProcessor<NoOpProcessor>(config => config
            .FromConfiguration(configuration)
        );

        var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<KurrentProcessorHostedService<NoOpProcessor>>()
            .Single();

        var settings = hostedService.Worker.Settings;

        // Explicit ProcessorId in config should be used
        await Assert.That(settings.ProcessorId).IsEqualTo("my-custom-id");
    }

    [Test]
    public async Task standalone_builder_from_configuration_loads_settings() {
        var json = """
            {
              "Kurrent": {
                "Processors": {
                  "NoOpProcessor": {
                    "MaxParallelism": 2,
                    "Worker": {
                      "BufferCapacity": 3000
                    },
                    "Batching": {
                      "MaxSize": 50,
                      "Timeout": 200
                    }
                  }
                }
              }
            }
            """;

        var configuration = BuildJsonConfiguration(json);

        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .FromConfiguration(configuration)
            .Build();

        var settings = worker.Settings;

        // ProcessorId auto-generated from processor type name
        await Assert.That(settings.ProcessorId).IsEqualTo("no_op_processor");
        await Assert.That(settings.MaxParallelism).IsEqualTo(2);
        await Assert.That(settings.Worker.BufferCapacity).IsEqualTo(3000);
        await Assert.That(settings.Batching.MaxSize).IsEqualTo(50);
        await Assert.That(settings.Batching.Timeout).IsEqualTo(200);
    }

    [Test]
    public async Task standalone_builder_fluent_methods_override_configuration() {
        var json = """
            {
              "Kurrent": {
                "Processors": {
                  "NoOpProcessor": {
                    "MaxParallelism": 8,
                    "Worker": {
                      "BufferCapacity": 5000
                    }
                  }
                }
              }
            }
            """;

        var configuration = BuildJsonConfiguration(json);

        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .FromConfiguration(configuration)
            .WithMaxParallelism(2) // Override config value
            .WithProcessorId("custom-id") // Override auto-generated
            .Build();

        var settings = worker.Settings;

        await Assert.That(settings.ProcessorId).IsEqualTo("custom-id");
        await Assert.That(settings.MaxParallelism).IsEqualTo(2);
        await Assert.That(settings.Worker.BufferCapacity).IsEqualTo(5000); // From config
    }

    #endregion

    /// <summary>
    /// A minimal processor implementation for testing configuration.
    /// </summary>
    sealed class NoOpProcessor : IRecordProcessor {
        public ValueTask ProcessRecord(ProcessRecordContext context) => ValueTask.CompletedTask;
    }
}
