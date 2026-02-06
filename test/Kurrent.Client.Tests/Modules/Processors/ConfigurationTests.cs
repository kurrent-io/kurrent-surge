using System.Text;
using Kurrent.Client.Streams;
using Kurrent.Processors;
using Kurrent.Processors.Models;
using Kurrent.Processors.Options;
using Kurrent.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kurrent.Client.Tests.Modules.Processors;

/// <summary>
/// Unit tests for processor configuration, validation, and worker creation.
/// </summary>
[Category("Processors")]
[Category("Configuration")]
public class ConfigurationTests {
    #region Default Values Tests

    [Test]
    public void default_processor_id_is_empty() {
        var settings = new ProcessorSettings();
        settings.ProcessorId.ShouldBe(string.Empty);
    }

    [Test]
    public void default_worker_id_is_guid() {
        var settings = new ProcessorSettings();
        settings.WorkerId.ShouldNotBeNullOrEmpty();
        Guid.TryParse(settings.WorkerId, out _).ShouldBeTrue();
    }

    [Test]
    public void default_max_parallelism_is_128() {
        var settings = new ProcessorSettings();
        settings.MaxParallelism.ShouldBe(128);
    }

    [Test]
    public void default_buffer_capacity_is_1000() {
        var settings = new ProcessorSettings();
        settings.Worker.BufferCapacity.ShouldBe(1000);
    }

    [Test]
    public void default_batching_max_size_is_200() {
        var settings = new ProcessorSettings();
        settings.Batching.MaxSize.ShouldBe(200);
    }

    [Test]
    public void default_batching_timeout_is_250() {
        var settings = new ProcessorSettings();
        settings.Batching.Timeout.ShouldBe(250);
    }

    [Test]
    public void default_checkpoint_interval_is_30000() {
        var settings = new ProcessorSettings();
        settings.Checkpoint.Interval.ShouldBe(30000);
    }

    [Test]
    public void default_checkpoint_timeout_is_30000() {
        var settings = new ProcessorSettings();
        settings.Checkpoint.Timeout.ShouldBe(30000);
    }

    [Test]
    public void default_initial_position_is_latest() {
        var settings = new ProcessorSettings();
        settings.Subscription.InitialPosition.ShouldBe(SubscriptionInitialPosition.Latest);
    }

    [Test]
    public void default_skip_decoding_is_false() {
        var settings = new ProcessorSettings();
        settings.Worker.SkipDecoding.ShouldBeFalse();
    }

    #endregion

    #region Builder Configuration Tests

    [Test]
    public void with_processor_id_sets_settings_processor_id() {
        var processorId = "test-processor-id";

        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId(processorId)
            .WithMaxParallelism(1)
            .Build();

        worker.Settings.ProcessorId.ShouldBe(processorId);
        worker.ProcessorId.ShouldBe(processorId);
    }

    [Test]
    public void with_worker_id_sets_settings_worker_id() {
        var workerId = "test-worker-id";

        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("test-processor")
            .WithWorkerId(workerId)
            .WithMaxParallelism(1)
            .Build();

        worker.Settings.WorkerId.ShouldBe(workerId);
        worker.WorkerId.ShouldBe(workerId);
    }

    [Test]
    public void without_worker_id_generates_guid() {
        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("test-processor")
            .WithMaxParallelism(1)
            .Build();

        worker.WorkerId.ShouldNotBeNullOrEmpty();
        Guid.TryParse(worker.WorkerId, out _).ShouldBeTrue();
    }

    [Test]
    public void with_max_parallelism_sets_settings() {
        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("test-processor")
            .WithMaxParallelism(1)
            .Build();

        worker.Settings.MaxParallelism.ShouldBe(1);
    }

    [Test]
    public void configure_worker_sets_buffer_capacity() {
        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("test-processor")
            .WithMaxParallelism(1)
            .ConfigureWorker(w => w.BufferCapacity(5000))
            .Build();

        worker.Settings.Worker.BufferCapacity.ShouldBe(5000);
    }

    [Test]
    public void configure_batching_sets_settings() {
        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("test-processor")
            .WithMaxParallelism(1)
            .ConfigureBatching(b => b.MaxSize(200).Timeout(500))
            .Build();

        worker.Settings.Batching.MaxSize.ShouldBe(200);
        worker.Settings.Batching.Timeout.ShouldBe(500);
    }

    [Test]
    public void configure_checkpointing_sets_settings() {
        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("test-processor")
            .WithMaxParallelism(1)
            .ConfigureCheckpointing(c => c.Interval(60000).Timeout(30000).RetentionCount(5))
            .Build();

        worker.Settings.Checkpoint.Interval.ShouldBe(60000);
        worker.Settings.Checkpoint.Timeout.ShouldBe(30000);
        worker.Settings.Checkpoint.RetentionCount.ShouldBe(5);
    }

    [Test]
    public void with_initial_position_sets_settings() {
        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("test-processor")
            .WithMaxParallelism(1)
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .Build();

        worker.Settings.Subscription.InitialPosition.ShouldBe(SubscriptionInitialPosition.Earliest);
    }

    [Test]
    public void with_filter_sets_settings() {
        var filter = RecordFilter.ByStream("test-stream");

        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("test-processor")
            .WithMaxParallelism(1)
            .WithFilter(filter)
            .Build();

        worker.Settings.Subscription.Filter.ShouldBe(filter);
    }

    [Test]
    public void with_filter_expression_sets_both_expression_and_filter() {
        var expression = "stream_id LIKE 'orders-%'";

        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("test-processor")
            .WithMaxParallelism(1)
            .WithFilterExpression(expression)
            .Build();

        worker.Settings.Subscription.FilterExpression.ShouldBe(expression);
        worker.Settings.Subscription.Filter.ShouldNotBeNull();
        worker.Settings.Subscription.Filter.ShouldBeOfType<ExpressionFilter>();
    }

    [Test]
    public void with_partition_key_selector_sets_settings() {
        var selector = "data.customer_id";

        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("test-processor")
            .WithMaxParallelism(1)
            .WithPartitionKeySelector(selector)
            .Build();

        worker.Settings.Subscription.PartitionKeySelector.ShouldBe(selector);
    }

    [Test]
    public void configure_worker_sets_skip_decoding() {
        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("test-processor")
            .WithMaxParallelism(1)
            .ConfigureWorker(w => w.SkipDecoding())
            .Build();

        worker.Settings.Worker.SkipDecoding.ShouldBeTrue();
    }

    [Test]
    public void configure_multiple_settings_applies_all() {
        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("test-processor")
            .WithMaxParallelism(1)
            .ConfigureWorker(w => w.BufferCapacity(2000))
            .ConfigureBatching(b => b.MaxSize(50).Timeout(200))
            .Build();

        worker.Settings.MaxParallelism.ShouldBe(1);
        worker.Settings.Worker.BufferCapacity.ShouldBe(2000);
        worker.Settings.Batching.MaxSize.ShouldBe(50);
        worker.Settings.Batching.Timeout.ShouldBe(200);
    }

    #endregion

    #region Worker Creation Tests

    [Test]
    public void build_returns_worker_with_correct_settings() {
        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("test-processor")
            .WithWorkerId("test-worker")
            .WithMaxParallelism(1)
            .Build();

        worker.ShouldNotBeNull();
        worker.ProcessorId.ShouldBe("test-processor");
        worker.WorkerId.ShouldBe("test-worker");
        worker.Settings.ShouldNotBeNull();
    }

    [Test]
    public void worker_initial_status_is_starting() {
        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("test-processor")
            .WithMaxParallelism(1)
            .Build();

        worker.Status.ShouldBe(ProcessorWorkerStatus.Starting);
    }

    #endregion

    #region Validation Tests

    [Test]
    public void valid_configuration_passes_validation() {
        var settings = new ProcessorSettings {
            ProcessorId    = "valid-processor",
            MaxParallelism = 1,
            Worker         = new WorkerSettings { BufferCapacity = 1000 }
        };

        var result = settings.ValidateOptions();

        result.IsSuccess.ShouldBeTrue();
    }

    [Test]
    public void empty_processor_id_fails_validation() {
        var settings = new ProcessorSettings {
            ProcessorId    = string.Empty,
            MaxParallelism = 1
        };

        var result = settings.ValidateOptions();

        result.IsSuccess.ShouldBeFalse();
    }

    [Test]
    public void invalid_max_parallelism_zero_fails_validation() {
        var settings = new ProcessorSettings {
            ProcessorId    = "test-processor",
            MaxParallelism = 0
        };

        var result = settings.ValidateOptions();

        result.IsSuccess.ShouldBeFalse();
    }

    [Test]
    public void invalid_max_parallelism_negative_fails_validation() {
        var settings = new ProcessorSettings {
            ProcessorId    = "test-processor",
            MaxParallelism = -1
        };

        var result = settings.ValidateOptions();

        result.IsSuccess.ShouldBeFalse();
    }

    [Test]
    public void invalid_max_parallelism_exceeds_limit_fails_validation() {
        var settings = new ProcessorSettings {
            ProcessorId    = "test-processor",
            MaxParallelism = 32769
        };

        var result = settings.ValidateOptions();

        result.IsSuccess.ShouldBeFalse();
    }

    [Test]
    public void invalid_buffer_capacity_zero_fails_validation() {
        var settings = new ProcessorSettings {
            ProcessorId    = "test-processor",
            MaxParallelism = 1,
            Worker         = new WorkerSettings { BufferCapacity = 0 }
        };

        var result = settings.ValidateOptions();

        result.IsSuccess.ShouldBeFalse();
    }

    [Test]
    public void invalid_buffer_capacity_exceeds_limit_fails_validation() {
        var settings = new ProcessorSettings {
            ProcessorId    = "test-processor",
            MaxParallelism = 1,
            Worker         = new WorkerSettings { BufferCapacity = 10001 }
        };

        var result = settings.ValidateOptions();

        result.IsSuccess.ShouldBeFalse();
    }

    [Test]
    public void invalid_batching_max_size_fails_validation() {
        var settings = new ProcessorSettings {
            ProcessorId    = "test-processor",
            MaxParallelism = 1,
            Batching       = new BatchingSettings { MaxSize = 0, Timeout = 100 }
        };

        var result = settings.ValidateOptions();

        result.IsSuccess.ShouldBeFalse();
    }

    [Test]
    public void invalid_batching_timeout_fails_validation() {
        var settings = new ProcessorSettings {
            ProcessorId    = "test-processor",
            MaxParallelism = 1,
            Batching       = new BatchingSettings { MaxSize = 100, Timeout = 0 }
        };

        var result = settings.ValidateOptions();

        result.IsSuccess.ShouldBeFalse();
    }

    [Test]
    public void invalid_checkpoint_interval_fails_validation() {
        var settings = new ProcessorSettings {
            ProcessorId    = "test-processor",
            MaxParallelism = 1,
            Checkpoint     = new CheckpointSettings { Interval = 0 }
        };

        var result = settings.ValidateOptions();

        result.IsSuccess.ShouldBeFalse();
    }

    [Test]
    public void max_parallelism_without_partition_key_passes_with_warning() {
        // PartitionKeySelector is a warning, not an error, for backward compatibility
        var settings = new ProcessorSettings {
            ProcessorId    = "test-processor",
            MaxParallelism = 4,
            Subscription   = new SubscriptionSettings { PartitionKeySelector = string.Empty }
        };

        var result = settings.ValidateOptions();

        // Validation passes (warnings are ignored), but the warning is generated
        result.IsSuccess.ShouldBeTrue();
    }

    [Test]
    public void max_parallelism_with_partition_key_passes_validation() {
        var settings = new ProcessorSettings {
            ProcessorId    = "test-processor",
            MaxParallelism = 4,
            Subscription   = new SubscriptionSettings { PartitionKeySelector = "stream_id" }
        };

        var result = settings.ValidateOptions();

        result.IsSuccess.ShouldBeTrue();
    }

    [Test]
    public void build_with_invalid_buffer_capacity_throws() {
        Should.Throw<ValidationException>(() =>
            KurrentProcessors.Create(new NoOpProcessor())
                .ConnectTo("kurrentdb://localhost:2113")
                .WithProcessorId("test-processor")
                .WithMaxParallelism(1)
                .ConfigureWorker(w => w.BufferCapacity(0))
                .Build()
        );
    }

    [Test]
    public void build_without_processor_id_auto_generates_from_type_name() {
        var worker = KurrentProcessors.Create(new NoOpProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithMaxParallelism(1)
            .Build();

        worker.ProcessorId.ShouldBe("no_op_processor");
    }

    [Test]
    public void build_with_invalid_max_parallelism_throws() {
        Should.Throw<ValidationException>(() =>
            KurrentProcessors.Create(new NoOpProcessor())
                .ConnectTo("kurrentdb://localhost:2113")
                .WithProcessorId("test-processor")
                .WithMaxParallelism(0)
                .Build()
        );
    }

    #endregion

    #region DI Registration Tests

    [Test]
    public void duplicate_registration_is_idempotent() {
        var services = new ServiceCollection();
        services.AddKurrentClient("kurrentdb://localhost:2113");

        // Register the same processor type twice
        services.AddKurrentProcessor<NoOpProcessor>(c => c.WithProcessorId("first"));
        services.AddKurrentProcessor<NoOpProcessor>(c => c.WithProcessorId("second"));

        // Should only have one hosted service registered
        var hostedServiceCount = services.Count(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationFactory is not null);

        hostedServiceCount.ShouldBe(1);
    }

    [Test]
    public void duplicate_registration_with_factory_is_idempotent() {
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

        hostedServiceCount.ShouldBe(1);
    }

    [Test]
    public void configuration_with_service_provider_can_access_services() {
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
        capturedId.ShouldBe("configured-processor-id");
    }

    record TestSettings {
        public string ProcessorId { get; init; } = string.Empty;
    }

    #endregion

    #region Configuration Binding Tests

    [Test]
    public void settings_loaded_from_json_configuration_by_processor_type_name() {
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

        settings.ProcessorId.ShouldBe("config-processor");
        settings.MaxParallelism.ShouldBe(8);
        settings.Worker.BufferCapacity.ShouldBe(2000);
        settings.Batching.MaxSize.ShouldBe(100);
        settings.Batching.Timeout.ShouldBe(500);
        settings.Checkpoint.Interval.ShouldBe(15000);
    }

    [Test]
    public void json_configuration_supports_camel_case_properties() {
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

        settings.ProcessorId.ShouldBe("camel-processor");
        settings.MaxParallelism.ShouldBe(4);
        settings.Worker.BufferCapacity.ShouldBe(1500);
        settings.Batching.MaxSize.ShouldBe(75);
        settings.Batching.Timeout.ShouldBe(300);
        settings.Checkpoint.Interval.ShouldBe(20000);
        settings.Subscription.FilterExpression.ShouldBe("stream_id LIKE 'test-%'");
        settings.Subscription.InitialPosition.ShouldBe(SubscriptionInitialPosition.Earliest);
        settings.Subscription.PartitionKeySelector.ShouldBe("data.id");
    }

    [Test]
    public void json_configuration_does_not_support_snake_case_properties() {
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
        settings.ProcessorId.ShouldBe("fallback-processor");
        settings.MaxParallelism.ShouldBe(1);
        settings.Worker.BufferCapacity.ShouldBe(1000); // default value
    }

    [Test]
    public void fluent_configuration_overrides_json_settings() {
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
        settings.ProcessorId.ShouldBe("fluent-processor");
        settings.MaxParallelism.ShouldBe(4);
        // Config value retained when no fluent override
        settings.Worker.BufferCapacity.ShouldBe(2000);
    }

    [Test]
    public void missing_json_section_uses_defaults() {
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
        settings.Worker.BufferCapacity.ShouldBe(1000);
        settings.Batching.MaxSize.ShouldBe(200);
        settings.Batching.Timeout.ShouldBe(250);
        settings.Checkpoint.Interval.ShouldBe(30000);
    }

    [Test]
    public void nested_subscription_settings_bind_from_json() {
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

        settings.Subscription.FilterExpression.ShouldBe("stream_id LIKE 'orders-%'");
        settings.Subscription.InitialPosition.ShouldBe(SubscriptionInitialPosition.Earliest);
        settings.Subscription.PartitionKeySelector.ShouldBe("data.customer_id");
    }

    [Test]
    public void configuration_binding_works_with_service_provider_overload() {
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
        services.AddSingleton<IConfiguration>(configuration);
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
            .OfType<Kurrent.Processors.Options.KurrentProcessorHostedService<NoOpProcessor>>()
            .Single();

        var settings = hostedService.Worker.Settings;

        // From service provider overload
        settings.ProcessorId.ShouldBe("sp-processor");
        // From config
        settings.Worker.BufferCapacity.ShouldBe(3000);
    }

    [Test]
    public void no_configuration_registered_uses_defaults() {
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
            .OfType<Kurrent.Processors.Options.KurrentProcessorHostedService<NoOpProcessor>>()
            .Single();

        var settings = hostedService.Worker.Settings;

        // Default values should be used
        settings.Worker.BufferCapacity.ShouldBe(1000);
        settings.Batching.MaxSize.ShouldBe(200);
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
            .OfType<Kurrent.Processors.Options.KurrentProcessorHostedService<NoOpProcessor>>()
            .Single();

        return hostedService.Worker.Settings;
    }

    [Test]
    public void processor_id_auto_generated_from_processor_type_name_when_not_in_config() {
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
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddKurrentClient("kurrentdb://localhost:2113");

        // Section name is automatically determined from processor type name
        services.AddKurrentProcessor<NoOpProcessor>(config => config
            .FromConfiguration(configuration)
        );

        var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<Kurrent.Processors.Options.KurrentProcessorHostedService<NoOpProcessor>>()
            .Single();

        var settings = hostedService.Worker.Settings;

        // ProcessorId should be auto-generated from processor type name (section name)
        settings.ProcessorId.ShouldBe("no_op_processor");
        settings.MaxParallelism.ShouldBe(4);
        settings.Worker.BufferCapacity.ShouldBe(2000);
    }

    [Test]
    public void explicit_processor_id_in_config_overrides_auto_generation() {
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
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddKurrentClient("kurrentdb://localhost:2113");
        services.AddKurrentProcessor<NoOpProcessor>(config => config
            .FromConfiguration(configuration)
        );

        var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<Kurrent.Processors.Options.KurrentProcessorHostedService<NoOpProcessor>>()
            .Single();

        var settings = hostedService.Worker.Settings;

        // Explicit ProcessorId in config should be used
        settings.ProcessorId.ShouldBe("my-custom-id");
    }

    [Test]
    public void standalone_builder_from_configuration_loads_settings() {
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
        settings.ProcessorId.ShouldBe("no_op_processor");
        settings.MaxParallelism.ShouldBe(2);
        settings.Worker.BufferCapacity.ShouldBe(3000);
        settings.Batching.MaxSize.ShouldBe(50);
        settings.Batching.Timeout.ShouldBe(200);
    }

    [Test]
    public void standalone_builder_fluent_methods_override_configuration() {
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

        settings.ProcessorId.ShouldBe("custom-id");
        settings.MaxParallelism.ShouldBe(2);
        settings.Worker.BufferCapacity.ShouldBe(5000); // From config
    }

    #endregion

    /// <summary>
    /// A minimal processor implementation for testing configuration.
    /// </summary>
    sealed class NoOpProcessor : IRecordProcessor {
        public ValueTask ProcessRecords(ProcessRecordsContext context) => ValueTask.CompletedTask;
    }
}
