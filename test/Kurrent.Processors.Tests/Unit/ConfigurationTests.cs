using System.Text;
using Kurrent.Client.Streams;
using Kurrent.Processors.Hosting;
using Kurrent.Processors.Models;
using Kurrent.Processors.Options;
using Kurrent.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kurrent.Processors.Tests;

/// <summary>
/// Unit tests for processor configuration, validation, and drone creation.
/// </summary>
[Category("Processors")]
public class ConfigurationTests {
    const string TestConnectionString = "kurrentdb://localhost:2113";

    #region Default Values Tests

    [Test]
    public async Task default_processor_id_is_empty() {
        var settings = new ProcessorOptions();
        await Assert.That(settings.ProcessorId).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task default_drone_id_is_empty() {
        var settings = new ProcessorOptions();
        await Assert.That(settings.DroneId).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task setting_processor_id_derives_drone_id() {
        var settings = new ProcessorOptions { ProcessorId = "order-processor" };
        await Assert.That(settings.DroneId).StartsWith("order-processor-");
    }

    [Test]
    public async Task setting_processor_id_preserves_explicit_drone_id() {
        var settings = new ProcessorOptions { DroneId = "machine-01" };
        settings.ProcessorId = "order-processor";
        await Assert.That(settings.DroneId).IsEqualTo("machine-01");
    }

    [Test]
    public async Task reassigning_processor_id_does_not_overwrite_derived_drone_id() {
        var settings = new ProcessorOptions { ProcessorId = "first" };
        var firstDroneId = settings.DroneId;
        settings.ProcessorId = "second";
        await Assert.That(settings.DroneId).IsEqualTo(firstDroneId);
    }

    [Test]
    public async Task default_buffer_capacity_is_1000() {
        var settings = new ProcessorOptions();
        await Assert.That(settings.BufferCapacity).IsEqualTo(1000);
    }

    [Test]
    public async Task default_checkpoint_interval_is_30000() {
        var settings = new ProcessorOptions();
        await Assert.That(settings.Checkpoint.Interval).IsEqualTo(TimeSpan.FromMilliseconds(30000));
    }

    [Test]
    public async Task default_initial_position_is_latest() {
        var settings = new ProcessorOptions();
        await Assert.That(settings.Subscription.InitialPosition).IsEqualTo(SubscriptionInitialPosition.Latest);
    }

    [Test]
    public async Task default_skip_decoding_is_false() {
        var settings = new ProcessorOptions();
        await Assert.That(settings.SkipDecoding).IsFalse();
    }

    #endregion

    #region Builder Configuration Tests

    [Test]
    public async Task with_processor_id_sets_settings_processor_id() {
        var processorId = "test-processor-id";

        var drone = KurrentProcessor.Create(new NoOpProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId = processorId;
        }).Build();

        await Assert.That(drone.Options.ProcessorId).IsEqualTo(processorId);
        await Assert.That(drone.ProcessorId).IsEqualTo(processorId);
    }

    [Test]
    public async Task with_drone_id_sets_settings_drone_id() {
        var droneId = "test-drone-id";

        var drone = KurrentProcessor.Create(new NoOpProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId = "test-processor";
            opts.DroneId     = droneId;
        }).Build();

        await Assert.That(drone.Options.DroneId).IsEqualTo(droneId);
        await Assert.That(drone.DroneId).IsEqualTo(droneId);
    }

    [Test]
    public async Task without_drone_id_derives_from_processor_id() {
        var drone = KurrentProcessor.Create(new NoOpProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId = "test-processor";
        }).Build();

        await Assert.That(drone.DroneId).StartsWith("test-processor-");
    }

    [Test]
    public async Task configure_drone_sets_buffer_capacity() {
        var drone = KurrentProcessor.Create(new NoOpProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId    = "test-processor";
            opts.BufferCapacity = 5000;
        }).Build();

        await Assert.That(drone.Options.BufferCapacity).IsEqualTo(5000);
    }

    [Test]
    public async Task configure_checkpointing_sets_settings() {
        var drone = KurrentProcessor.Create(new NoOpProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId = "test-processor";
            opts.Checkpointing(c => c
                .Interval(60000L)
                .RetentionCount(5)
            );
        }).Build();

        await Assert.That(drone.Options.Checkpoint.Interval).IsEqualTo(TimeSpan.FromMilliseconds(60000));
        await Assert.That(drone.Options.Checkpoint.RetentionCount).IsEqualTo(5);
    }

    [Test]
    public async Task with_initial_position_sets_settings() {
        var drone = KurrentProcessor.Create(new NoOpProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId                  = "test-processor";
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
        }).Build();

        await Assert.That(drone.Options.Subscription.InitialPosition).IsEqualTo(SubscriptionInitialPosition.Earliest);
    }

    [Test]
    public async Task with_filter_sets_settings() {
        var filter = RecordFilter.ByStream("test-stream");

        var drone = KurrentProcessor.Create(new NoOpProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId         = "test-processor";
            opts.Subscription.Filter = filter;
        }).Build();

        await Assert.That(drone.Options.Subscription.Filter).IsEqualTo(filter);
    }

    [Test]
    public async Task with_expression_filter_sets_filter() {
        var expression = "stream_id LIKE 'orders-%'";

        var drone = KurrentProcessor.Create(new NoOpProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId         = "test-processor";
            opts.Subscription.Filter = RecordFilter.ByExpression(expression);
        }).Build();

        await Assert.That(drone.Options.Subscription.Filter).IsNotNull();
        await Assert.That(drone.Options.Subscription.Filter).IsTypeOf<ExpressionFilter>();
    }

    [Test]
    public async Task with_partition_key_selector_sets_settings() {
        var selector = "data.customer_id";

        var drone = KurrentProcessor.Create(new NoOpProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId                       = "test-processor";
            opts.Subscription.PartitionKeySelector = selector;
        }).Build();

        await Assert.That(drone.Options.Subscription.PartitionKeySelector).IsEqualTo(selector);
    }

    [Test]
    public async Task configure_drone_sets_skip_decoding() {
        var drone = KurrentProcessor.Create(new NoOpProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId  = "test-processor";
            opts.SkipDecoding = true;
        }).Build();

        await Assert.That(drone.Options.SkipDecoding).IsTrue();
    }

    [Test]
    public async Task configure_multiple_settings_applies_all() {
        var drone = KurrentProcessor.Create(new NoOpProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId    = "test-processor";
            opts.BufferCapacity = 2000;
            opts.SkipDecoding   = true;
        }).Build();

        await Assert.That(drone.Options.BufferCapacity).IsEqualTo(2000);
        await Assert.That(drone.Options.SkipDecoding).IsTrue();
    }

    #endregion

    #region Drone Creation Tests

    [Test]
    public async Task build_returns_drone_with_correct_settings() {
        var drone = KurrentProcessor.Create(new NoOpProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId = "test-processor";
            opts.DroneId     = "test-drone";
        }).Build();

        await Assert.That(drone).IsNotNull();
        await Assert.That(drone.ProcessorId).IsEqualTo("test-processor");
        await Assert.That(drone.DroneId).IsEqualTo("test-drone");
        await Assert.That(drone.Options).IsNotNull();
    }

    [Test]
    public async Task drone_initial_status_is_starting() {
        var drone = KurrentProcessor.Create(new NoOpProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId = "test-processor";
        }).Build();

        await Assert.That(drone.Status).IsEqualTo(DroneStatus.Starting);
    }

    #endregion

    #region Validation Tests

    [Test]
    public async Task valid_configuration_passes_validation() {
        var settings = new ProcessorOptions {
            ProcessorId    = "valid-processor",
            MaxParallelism = 1,
            BufferCapacity = 1000
        };
        settings.Connection(TestConnectionString);

        var result = settings.RunValidation();

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task empty_processor_id_fails_validation() {
        var settings = new ProcessorOptions {
            ProcessorId    = string.Empty,
            MaxParallelism = 1
        };

        var result = settings.RunValidation();

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task invalid_max_parallelism_zero_fails_validation() {
        var settings = new ProcessorOptions {
            ProcessorId    = "test-processor",
            MaxParallelism = 0
        };

        var result = settings.RunValidation();

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task invalid_max_parallelism_negative_fails_validation() {
        var settings = new ProcessorOptions {
            ProcessorId    = "test-processor",
            MaxParallelism = -1
        };

        var result = settings.RunValidation();

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task invalid_max_parallelism_exceeds_limit_fails_validation() {
        var settings = new ProcessorOptions {
            ProcessorId    = "test-processor",
            MaxParallelism = 32769
        };

        var result = settings.RunValidation();

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task invalid_buffer_capacity_zero_fails_validation() {
        var settings = new ProcessorOptions {
            ProcessorId    = "test-processor",
            MaxParallelism = 1,
            BufferCapacity = 0
        };

        var result = settings.RunValidation();

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task invalid_buffer_capacity_exceeds_limit_fails_validation() {
        var settings = new ProcessorOptions {
            ProcessorId    = "test-processor",
            MaxParallelism = 1,
            BufferCapacity = 10001
        };

        var result = settings.RunValidation();

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task invalid_checkpoint_interval_fails_validation() {
        var settings = new ProcessorOptions {
            ProcessorId    = "test-processor",
            MaxParallelism = 1,
            Checkpoint     = new CheckpointOptions { Interval = TimeSpan.Zero }
        };

        var result = settings.RunValidation();

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task max_parallelism_without_partition_key_fails_validation() {
        var settings = new ProcessorOptions {
            ProcessorId    = "test-processor",
            MaxParallelism = 4,
            Subscription   = new SubscriptionOptions()
        };
        settings.Connection(TestConnectionString);

        var result = settings.RunValidation();

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task max_parallelism_with_partition_key_passes_validation() {
        var settings = new ProcessorOptions {
            ProcessorId    = "test-processor",
            MaxParallelism = 4,
            Subscription   = new SubscriptionOptions { PartitionKeySelector = "stream_id" }
        };
        settings.Connection(TestConnectionString);

        var result = settings.RunValidation();

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task build_with_invalid_buffer_capacity_throws() {
        await Assert.That(() =>
            KurrentProcessor.Create(new NoOpProcessor(), opts => {
                opts.Connection(TestConnectionString);
                opts.ProcessorId    = "test-processor";
                opts.BufferCapacity = 0;
            }).Build()
        ).Throws<ValidationException>();
    }

    [Test]
    public async Task build_without_processor_id_auto_generates_from_type_name() {
        var drone = KurrentProcessor.Create(new NoOpProcessor(), opts => {
            opts.Connection(TestConnectionString);
        }).Build();

        await Assert.That(drone.ProcessorId).IsEqualTo("no_op_processor");
    }

    #endregion

    #region Connection Resolution Tests

    [Test]
    public async Task standalone_processor_throws_when_no_connection_is_configured() {
        var options = new ProcessorOptions { ProcessorId = "test-processor" };

        var processor = new KurrentProcessor(options, new NoOpProcessor());

        await Assert.That(() => processor.Build()).Throws<InvalidOperationException>();
    }

    #endregion

    #region DI Registration Tests

    [Test]
    public async Task duplicate_registration_is_idempotent() {
        var services = new ServiceCollection();

        services.AddKurrentProcessor(new NoOpProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId = "first";
        });
        services.AddKurrentProcessor(new NoOpProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId = "second";
        });

        var hostedServiceCount = services.Count(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationFactory is not null);

        await Assert.That(hostedServiceCount).IsEqualTo(1);
    }

    [Test]
    public async Task duplicate_registration_with_factory_is_idempotent() {
        var services = new ServiceCollection();

        services.AddKurrentProcessor<NoOpProcessor>(
            _ => new NoOpProcessor(),
            opts => {
                opts.Connection(TestConnectionString);
                opts.ProcessorId = "first";
            });
        services.AddKurrentProcessor<NoOpProcessor>(
            _ => new NoOpProcessor(),
            opts => {
                opts.Connection(TestConnectionString);
                opts.ProcessorId = "second";
            });

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

        string? capturedId = null;

        services.AddKurrentProcessor(new NoOpProcessor(), (opts, sp) => {
            var settings = sp.GetRequiredService<TestSettings>();
            capturedId = settings.ProcessorId;
            opts.ProcessorId = capturedId;
            opts.Connection(TestConnectionString);
        });

        var provider = services.BuildServiceProvider();

        // Force the hosted service to be materialized, triggering the configure delegate.
        _ = provider.GetServices<IHostedService>().ToList();

        await Assert.That(capturedId).IsEqualTo("configured-processor-id");
    }

    [Test]
    public async Task di_resolves_connection_from_configuration_by_processor_id() {
        var configuration = BuildJsonConfiguration("""
            {
              "ConnectionStrings": {
                "by-id-processor": "kurrentdb://localhost:2113"
              }
            }
            """);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddKurrentProcessor(new NoOpProcessor(), opts => opts.ProcessorId = "by-id-processor");

        var provider     = services.BuildServiceProvider();
        var droneService = provider.GetServices<IHostedService>().OfType<DroneService<NoOpProcessor>>().Single();

        await Assert.That(droneService.Drone.Options.ClientOptions!.Endpoints[0].Host).IsEqualTo("localhost");
    }

    [Test]
    public async Task di_resolves_connection_from_configuration_default_resource_name() {
        var configuration = BuildJsonConfiguration("""
            {
              "ConnectionStrings": {
                "kurrentdb": "kurrentdb://localhost:2113"
              }
            }
            """);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddKurrentProcessor(new NoOpProcessor(), opts => opts.ProcessorId = "unknown-processor");

        var provider     = services.BuildServiceProvider();
        var droneService = provider.GetServices<IHostedService>().OfType<DroneService<NoOpProcessor>>().Single();

        await Assert.That(droneService.Drone.Options.ClientOptions!.Endpoints[0].Host).IsEqualTo("localhost");
    }

    record TestSettings {
        public string ProcessorId { get; init; } = string.Empty;
    }

    #endregion

    #region Configuration Binding Tests

    [Test]
    public async Task settings_loaded_from_json_configuration_by_processor_type_name() {
        var configuration = BuildJsonConfiguration("""
            {
              "Kurrent": {
                "Processors": {
                  "NoOpProcessor": {
                    "ProcessorId": "config-processor",
                    "MaxParallelism": 8,
                    "BufferCapacity": 2000,
                    "Checkpoint": {
                      "Interval": 15000
                    },
                    "Subscription": {
                      "PartitionKeySelector": "stream_id"
                    }
                  }
                }
              },
              "ConnectionStrings": {
                "kurrentdb": "kurrentdb://localhost:2113"
              }
            }
            """);

        var settings = ResolveProcessorOptions(configuration);

        await Assert.That(settings.ProcessorId).IsEqualTo("config-processor");
        await Assert.That(settings.MaxParallelism).IsEqualTo(8);
        await Assert.That(settings.BufferCapacity).IsEqualTo(2000);
        await Assert.That(settings.Checkpoint.Interval).IsEqualTo(TimeSpan.FromMilliseconds(15000));
    }

    [Test]
    public async Task json_configuration_supports_camel_case_properties() {
        var configuration = BuildJsonConfiguration("""
            {
              "Kurrent": {
                "Processors": {
                  "NoOpProcessor": {
                    "processorId": "camel-processor",
                    "maxParallelism": 4,
                    "bufferCapacity": 1500,
                    "checkpoint": {
                      "interval": 20000
                    },
                    "subscription": {
                      "initialPosition": "Earliest",
                      "partitionKeySelector": "stream_id"
                    }
                  }
                }
              },
              "ConnectionStrings": {
                "kurrentdb": "kurrentdb://localhost:2113"
              }
            }
            """);

        var settings = ResolveProcessorOptions(configuration);

        await Assert.That(settings.ProcessorId).IsEqualTo("camel-processor");
        await Assert.That(settings.MaxParallelism).IsEqualTo(4);
        await Assert.That(settings.BufferCapacity).IsEqualTo(1500);
        await Assert.That(settings.Checkpoint.Interval).IsEqualTo(TimeSpan.FromMilliseconds(20000));
        await Assert.That(settings.Subscription.InitialPosition).IsEqualTo(SubscriptionInitialPosition.Earliest);
    }

    [Test]
    public async Task json_configuration_does_not_support_snake_case_properties() {
        // snake_case is NOT supported by default .NET configuration binding
        var configuration = BuildJsonConfiguration("""
            {
              "Kurrent": {
                "Processors": {
                  "NoOpProcessor": {
                    "processor_id": "snake-processor",
                    "max_parallelism": 4,
                    "buffer_capacity": 1500
                  }
                }
              },
              "ConnectionStrings": {
                "kurrentdb": "kurrentdb://localhost:2113"
              }
            }
            """);

        var settings = ResolveProcessorOptions(configuration, configure: opts => {
            opts.ProcessorId    = "fallback-processor";
            opts.MaxParallelism = 1;
        });

        await Assert.That(settings.ProcessorId).IsEqualTo("fallback-processor");
        await Assert.That(settings.MaxParallelism).IsEqualTo(1);
        await Assert.That(settings.BufferCapacity).IsEqualTo(1000);
    }

    [Test]
    public async Task fluent_configuration_overrides_json_settings() {
        var configuration = BuildJsonConfiguration("""
            {
              "Kurrent": {
                "Processors": {
                  "NoOpProcessor": {
                    "ProcessorId": "config-processor",
                    "MaxParallelism": 8,
                    "BufferCapacity": 2000,
                    "Subscription": {
                      "PartitionKeySelector": "stream_id"
                    }
                  }
                }
              },
              "ConnectionStrings": {
                "kurrentdb": "kurrentdb://localhost:2113"
              }
            }
            """);

        var settings = ResolveProcessorOptions(configuration, configure: opts => {
            opts.ProcessorId    = "fluent-processor";
            opts.MaxParallelism = 4;
        });

        await Assert.That(settings.ProcessorId).IsEqualTo("fluent-processor");
        await Assert.That(settings.MaxParallelism).IsEqualTo(4);
        await Assert.That(settings.BufferCapacity).IsEqualTo(2000);
    }

    [Test]
    public async Task missing_json_section_uses_defaults() {
        var configuration = BuildJsonConfiguration("""
            {
              "SomeOther": {
                "Section": "value"
              },
              "ConnectionStrings": {
                "kurrentdb": "kurrentdb://localhost:2113"
              }
            }
            """);

        var settings = ResolveProcessorOptions(configuration, configure: opts => {
            opts.ProcessorId    = "test-processor";
            opts.MaxParallelism = 1;
        });

        await Assert.That(settings.BufferCapacity).IsEqualTo(1000);
        await Assert.That(settings.Checkpoint.Interval).IsEqualTo(TimeSpan.FromMilliseconds(30000));
    }

    [Test]
    public async Task nested_subscription_settings_bind_from_json() {
        var configuration = BuildJsonConfiguration("""
            {
              "Kurrent": {
                "Processors": {
                  "NoOpProcessor": {
                    "ProcessorId": "test-processor",
                    "MaxParallelism": 1,
                    "Subscription": {
                      "InitialPosition": "Earliest"
                    }
                  }
                }
              },
              "ConnectionStrings": {
                "kurrentdb": "kurrentdb://localhost:2113"
              }
            }
            """);

        var settings = ResolveProcessorOptions(configuration);

        await Assert.That(settings.Subscription.InitialPosition).IsEqualTo(SubscriptionInitialPosition.Earliest);
    }

    [Test]
    public async Task configuration_binding_works_with_service_provider_overload() {
        var configuration = BuildJsonConfiguration("""
            {
              "Kurrent": {
                "Processors": {
                  "NoOpProcessor": {
                    "BufferCapacity": 3000
                  }
                }
              },
              "ConnectionStrings": {
                "kurrentdb": "kurrentdb://localhost:2113"
              }
            }
            """);

        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddSingleton(new TestSettings { ProcessorId = "sp-processor" });
        services.AddLogging();
        services.AddKurrentProcessor(new NoOpProcessor(), (opts, sp) => {
            var testSettings = sp.GetRequiredService<TestSettings>();
            opts.ProcessorId    = testSettings.ProcessorId;
            opts.MaxParallelism = 1;
        });

        var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<DroneService<NoOpProcessor>>()
            .Single();

        var settings = hostedService.Drone.Options;

        await Assert.That(settings.ProcessorId).IsEqualTo("sp-processor");
        await Assert.That(settings.BufferCapacity).IsEqualTo(3000);
    }

    [Test]
    public async Task no_configuration_registered_uses_defaults() {
        var services = new ServiceCollection();
        // Intentionally NOT registering IConfiguration
        services.AddLogging();
        services.AddKurrentProcessor(new NoOpProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId    = "test-processor";
            opts.MaxParallelism = 1;
        });

        var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<DroneService<NoOpProcessor>>()
            .Single();

        var settings = hostedService.Drone.Options;

        await Assert.That(settings.BufferCapacity).IsEqualTo(1000);
        await Assert.That(settings.Checkpoint.Interval).IsEqualTo(TimeSpan.FromMilliseconds(30000));
    }

    [Test]
    public async Task processor_id_auto_generated_from_processor_type_name_when_not_in_config() {
        var configuration = BuildJsonConfiguration("""
            {
              "Kurrent": {
                "Processors": {
                  "NoOpProcessor": {
                    "MaxParallelism": 4,
                    "BufferCapacity": 2000,
                    "Subscription": {
                      "PartitionKeySelector": "stream_id"
                    }
                  }
                }
              },
              "ConnectionStrings": {
                "kurrentdb": "kurrentdb://localhost:2113"
              }
            }
            """);

        var settings = ResolveProcessorOptions(configuration);

        await Assert.That(settings.ProcessorId).IsEqualTo("no_op_processor");
        await Assert.That(settings.MaxParallelism).IsEqualTo(4);
        await Assert.That(settings.BufferCapacity).IsEqualTo(2000);
    }

    [Test]
    public async Task explicit_processor_id_in_config_overrides_auto_generation() {
        var configuration = BuildJsonConfiguration("""
            {
              "Kurrent": {
                "Processors": {
                  "NoOpProcessor": {
                    "ProcessorId": "my-custom-id",
                    "MaxParallelism": 4,
                    "Subscription": {
                      "PartitionKeySelector": "stream_id"
                    }
                  }
                }
              },
              "ConnectionStrings": {
                "kurrentdb": "kurrentdb://localhost:2113"
              }
            }
            """);

        var settings = ResolveProcessorOptions(configuration);

        await Assert.That(settings.ProcessorId).IsEqualTo("my-custom-id");
    }

    static IConfiguration BuildJsonConfiguration(string json) {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();
    }

    static ProcessorOptions ResolveProcessorOptions(
        IConfiguration configuration,
        Action<ProcessorOptions>? configure = null) {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging();
        services.AddKurrentProcessor(new NoOpProcessor(), opts => configure?.Invoke(opts));

        var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<DroneService<NoOpProcessor>>()
            .Single();

        return hostedService.Drone.Options;
    }

    #endregion

    /// <summary>
    /// A minimal processor implementation for testing configuration.
    /// </summary>
    sealed class NoOpProcessor : IRecordProcessor {
        public ValueTask ProcessRecord(ProcessRecordContext context) => ValueTask.CompletedTask;
    }
}
