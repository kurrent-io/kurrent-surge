using Kurrent.Surge.Processors;
using System.Text;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Processors.Configuration;
using Kurrent.Surge.Processors.Hosting;
using Kurrent.Surge.Processors.Models;
using Kurrent.Surge.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kurrent.Surge.Streaming.Tests;

/// <summary>
/// Unit tests for processor configuration, validation, and drone creation.
/// </summary>
[Category("Processors")]
public class ConfigurationTests {
    const string TestConnectionString = "kurrentdb://localhost:2113";

    #region ->> Default Values Tests <<-

    [Test]
    public async ValueTask default_processor_id_is_empty() {
        var settings = new ProcessorOptions();
        await Assert.That(settings.ProcessorId).IsEqualTo("");
    }

    [Test]
    public async ValueTask default_drone_id_is_empty() {
        var settings = new ProcessorOptions();
        await Assert.That(settings.DroneId).IsEqualTo("");
    }

    [Test]
    public async ValueTask setting_processor_id_derives_drone_id() {
        var settings = new ProcessorOptions { ProcessorId = "order-processor" };
        await Assert.That(settings.DroneId).StartsWith("order-processor-");
    }

    [Test]
    public async ValueTask setting_processor_id_preserves_explicit_drone_id() {
        var settings = new ProcessorOptions { DroneId = "machine-01" };
        settings.ProcessorId = "order-processor";
        await Assert.That(settings.DroneId).IsEqualTo("machine-01");
    }

    [Test]
    public async ValueTask reassigning_processor_id_does_not_overwrite_derived_drone_id() {
        var settings = new ProcessorOptions { ProcessorId = "first" };
        var firstDroneId = settings.DroneId;
        settings.ProcessorId = "second";
        await Assert.That(settings.DroneId).IsEqualTo(firstDroneId);
    }

    [Test]
    public async ValueTask default_batching_is_size_500_window_100ms_multiplier_2() {
        var settings = new ProcessorOptions();
        await Assert.That(settings.Batching.MaxBatchSize).IsEqualTo(500);
        await Assert.That(settings.Batching.BatchWindow).IsEqualTo(TimeSpan.FromMilliseconds(100));
        await Assert.That(settings.Batching.BufferMultiplier).IsEqualTo(2);
    }

    [Test]
    public async ValueTask default_checkpoint_interval_is_30000() {
        var settings = new ProcessorOptions();
        await Assert.That(settings.Checkpoint.Interval).IsEqualTo(TimeSpan.FromMilliseconds(30000));
    }

    [Test]
    public async ValueTask default_initial_position_is_latest() {
        var settings = new ProcessorOptions();
        await Assert.That(settings.Subscription.InitialPosition).IsEqualTo(SubscriptionInitialPosition.Latest);
    }

    [Test]
    public async ValueTask default_skip_decoding_is_false() {
        var settings = new ProcessorOptions();
        await Assert.That(settings.SkipDecoding).IsFalse();
    }

    #endregion // Default Values Tests

    #region ->> Builder Configuration Tests <<-

    [Test]
    public async ValueTask with_processor_id_sets_settings_processor_id() {
        var processorId = "test-processor-id";

        var drone = KurrentProcessor.Create(new TestableRecordProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId = processorId;
        }).Build();

        await Assert.That(drone.Options.ProcessorId).IsEqualTo(processorId);
        await Assert.That(drone.ProcessorId).IsEqualTo(processorId);
    }

    [Test]
    public async ValueTask with_drone_id_sets_settings_drone_id() {
        var droneId = "test-drone-id";

        var drone = KurrentProcessor.Create(new TestableRecordProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId = "test-processor";
            opts.DroneId     = droneId;
        }).Build();

        await Assert.That(drone.Options.DroneId).IsEqualTo(droneId);
        await Assert.That(drone.DroneId).IsEqualTo(droneId);
    }

    [Test]
    public async ValueTask without_drone_id_derives_from_processor_id() {
        var drone = KurrentProcessor.Create(new TestableRecordProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId = "test-processor";
        }).Build();

        await Assert.That(drone.DroneId).StartsWith("test-processor-");
    }

    [Test]
    public async ValueTask configure_drone_sets_batching() {
        var drone = KurrentProcessor.Create(new TestableRecordProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId               = "test-processor";
            opts.Batching.MaxBatchSize     = 5000;
            opts.Batching.BufferMultiplier = 4;
        }).Build();

        await Assert.That(drone.Options.Batching.MaxBatchSize).IsEqualTo(5000);
        await Assert.That(drone.Options.Batching.BufferMultiplier).IsEqualTo(4);
    }

    [Test]
    public async ValueTask configure_checkpointing_sets_settings() {
        var drone = KurrentProcessor.Create(new TestableRecordProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId = "test-processor";
            opts.Checkpointing(c => {
                c.IntervalFrom(60000L);
                c.RetentionCount = 5;
            });
        }).Build();

        await Assert.That(drone.Options.Checkpoint.Interval).IsEqualTo(TimeSpan.FromMilliseconds(60000));
        await Assert.That(drone.Options.Checkpoint.RetentionCount).IsEqualTo(5);
    }

    [Test]
    public async ValueTask with_initial_position_sets_settings() {
        var drone = KurrentProcessor.Create(new TestableRecordProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId                  = "test-processor";
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
        }).Build();

        await Assert.That(drone.Options.Subscription.InitialPosition).IsEqualTo(SubscriptionInitialPosition.Earliest);
    }

    [Test]
    public async ValueTask with_filter_sets_settings() {
        var filter = RecordFilter.ByStream("test-stream");

        var drone = KurrentProcessor.Create(new TestableRecordProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId         = "test-processor";
            opts.Subscription.Filter = filter;
        }).Build();

        await Assert.That(drone.Options.Subscription.Filter).IsEqualTo(filter);
    }

    [Test]
    public async ValueTask with_expression_filter_sets_filter() {
        var expression = "stream LIKE 'orders-%'";

        var drone = KurrentProcessor.Create(new TestableRecordProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId         = "test-processor";
            opts.Subscription.Filter = RecordFilter.ByExpression(expression);
        }).Build();

        await Assert.That(drone.Options.Subscription.Filter).IsNotNull();
        await Assert.That(drone.Options.Subscription.Filter).IsTypeOf<ExpressionFilter>();
    }

    [Test]
    public async ValueTask with_partition_key_selector_sets_settings() {
        var selector = "data.customer_id";

        var drone = KurrentProcessor.Create(new TestableRecordProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId                       = "test-processor";
            opts.Subscription.PartitionKeySelector = selector;
        }).Build();

        await Assert.That(drone.Options.Subscription.PartitionKeySelector).IsEqualTo(selector);
    }

    [Test]
    public async ValueTask configure_drone_sets_skip_decoding() {
        var drone = KurrentProcessor.Create(new TestableRecordProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId  = "test-processor";
            opts.SkipDecoding = true;
        }).Build();

        await Assert.That(drone.Options.SkipDecoding).IsTrue();
    }

    [Test]
    public async ValueTask configure_multiple_settings_applies_all() {
        var drone = KurrentProcessor.Create(new TestableRecordProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId           = "test-processor";
            opts.Batching.MaxBatchSize = 2000;
            opts.SkipDecoding          = true;
        }).Build();

        await Assert.That(drone.Options.Batching.MaxBatchSize).IsEqualTo(2000);
        await Assert.That(drone.Options.SkipDecoding).IsTrue();
    }

    #endregion // Builder Configuration Tests

    #region ->> Drone Creation Tests <<-

    [Test]
    public async ValueTask build_returns_drone_with_correct_settings() {
        var drone = KurrentProcessor.Create(new TestableRecordProcessor(), opts => {
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
    public async ValueTask drone_initial_status_is_starting() {
        var drone = KurrentProcessor.Create(new TestableRecordProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId = "test-processor";
        }).Build();

        await Assert.That(drone.Status).IsEqualTo(DroneStatus.Starting);
    }

    #endregion // Drone Creation Tests

    #region ->> Validation Tests <<-

    [Test]
    public async ValueTask valid_configuration_passes_validation() {
        var settings = new ProcessorOptions {
            ProcessorId    = "valid-processor",
            MaxParallelism = 1
        };
        settings.Connection(TestConnectionString);

        var result = settings.Validate();

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async ValueTask empty_processor_id_fails_validation() {
        var settings = new ProcessorOptions {
            ProcessorId    = "",
            MaxParallelism = 1
        };

        var result = settings.Validate();

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async ValueTask invalid_max_parallelism_zero_fails_validation() {
        var settings = new ProcessorOptions {
            ProcessorId    = "test-processor",
            MaxParallelism = 0
        };

        var result = settings.Validate();

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async ValueTask invalid_max_parallelism_negative_fails_validation() {
        var settings = new ProcessorOptions {
            ProcessorId    = "test-processor",
            MaxParallelism = -1
        };

        var result = settings.Validate();

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async ValueTask invalid_max_parallelism_exceeds_limit_fails_validation() {
        var settings = new ProcessorOptions {
            ProcessorId    = "test-processor",
            MaxParallelism = 32769
        };

        var result = settings.Validate();

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async ValueTask invalid_max_batch_size_zero_fails_validation() {
        var settings = new ProcessorOptions {
            ProcessorId    = "test-processor",
            MaxParallelism = 1,
            Batching       = new BatchOptions { MaxBatchSize = 0 }
        };

        var result = settings.Validate();

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async ValueTask invalid_max_batch_size_exceeds_limit_fails_validation() {
        var settings = new ProcessorOptions {
            ProcessorId    = "test-processor",
            MaxParallelism = 1,
            Batching       = new BatchOptions { MaxBatchSize = 10001 }
        };

        var result = settings.Validate();

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async ValueTask invalid_batch_window_zero_fails_validation() {
        var settings = new ProcessorOptions {
            ProcessorId    = "test-processor",
            MaxParallelism = 1,
            Batching       = new BatchOptions { BatchWindow = TimeSpan.Zero }
        };

        var result = settings.Validate();

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async ValueTask invalid_buffer_multiplier_zero_fails_validation() {
        var settings = new ProcessorOptions {
            ProcessorId    = "test-processor",
            MaxParallelism = 1,
            Batching       = new BatchOptions { BufferMultiplier = 0 }
        };

        var result = settings.Validate();

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async ValueTask invalid_buffer_multiplier_exceeds_limit_fails_validation() {
        var settings = new ProcessorOptions {
            ProcessorId    = "test-processor",
            MaxParallelism = 1,
            Batching       = new BatchOptions { BufferMultiplier = 101 }
        };

        var result = settings.Validate();

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async ValueTask invalid_checkpoint_interval_fails_validation() {
        var settings = new ProcessorOptions {
            ProcessorId    = "test-processor",
            MaxParallelism = 1,
            Checkpoint     = new CheckpointOptions { Interval = TimeSpan.Zero }
        };

        var result = settings.Validate();

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async ValueTask max_parallelism_without_partition_key_fails_validation() {
        var settings = new ProcessorOptions {
            ProcessorId    = "test-processor",
            MaxParallelism = 4,
            Subscription   = new SubscriptionOptions()
        };
        settings.Connection(TestConnectionString);

        var result = settings.Validate();

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async ValueTask max_parallelism_with_partition_key_passes_validation() {
        var settings = new ProcessorOptions {
            ProcessorId    = "test-processor",
            MaxParallelism = 4,
            Subscription   = new SubscriptionOptions { PartitionKeySelector = "stream" }
        };
        settings.Connection(TestConnectionString);

        var result = settings.Validate();

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async ValueTask build_with_invalid_max_batch_size_throws() {
        await Assert.That(() =>
            KurrentProcessor.Create(new TestableRecordProcessor(), opts => {
                opts.Connection(TestConnectionString);
                opts.ProcessorId           = "test-processor";
                opts.Batching.MaxBatchSize = 0;
            }).Build()
        ).Throws<ValidationException>();
    }

    [Test]
    public async ValueTask build_without_processor_id_auto_generates_from_type_name() {
        var drone = KurrentProcessor.Create(new TestableRecordProcessor(), opts => {
            opts.Connection(TestConnectionString);
        }).Build();

        await Assert.That(drone.ProcessorId).IsEqualTo("testable-record-processor");
    }

    #endregion // Validation Tests

    #region ->> Connection Resolution Tests <<-

    [Test]
    public async ValueTask standalone_processor_throws_when_no_connection_is_configured() {
        var options = new ProcessorOptions { ProcessorId = "test-processor" };

        var processor = new KurrentProcessor(options, new TestableRecordProcessor());

        await Assert.That(() => processor.Build()).Throws<InvalidOperationException>();
    }

    #endregion // Connection Resolution Tests

    #region ->> DI Registration Tests <<-

    [Test]
    public async ValueTask duplicate_registration_is_idempotent() {
        var services = new ServiceCollection();

        services.AddKurrentSurge(surge => surge.AddProcessor(new TestableRecordProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId = "first";
        }));
        services.AddKurrentSurge(surge => surge.AddProcessor(new TestableRecordProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId = "second";
        }));

        var hostedServiceCount = services.Count(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationFactory is not null);

        await Assert.That(hostedServiceCount).IsEqualTo(1);
    }

    [Test]
    public async ValueTask duplicate_registration_with_factory_is_idempotent() {
        var services = new ServiceCollection();

        services.AddKurrentSurge(surge => surge.AddProcessor<TestableRecordProcessor>(
            _ => new TestableRecordProcessor(),
            opts => {
                opts.Connection(TestConnectionString);
                opts.ProcessorId = "first";
            }));
        services.AddKurrentSurge(surge => surge.AddProcessor<TestableRecordProcessor>(
            _ => new TestableRecordProcessor(),
            opts => {
                opts.Connection(TestConnectionString);
                opts.ProcessorId = "second";
            }));

        var hostedServiceCount = services.Count(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationFactory is not null);

        await Assert.That(hostedServiceCount).IsEqualTo(1);
    }

    [Test]
    public async ValueTask configuration_with_service_provider_can_access_services() {
        var services = new ServiceCollection();
        services.AddSingleton(new TestSettings { ProcessorId = "configured-processor-id" });
        services.AddLogging();

        string? capturedId = null;

        services.AddKurrentSurge(surge => surge.AddProcessor(new TestableRecordProcessor(), (opts, sp) => {
            var settings = sp.GetRequiredService<TestSettings>();
            capturedId = settings.ProcessorId;
            opts.ProcessorId = capturedId;
            opts.Connection(TestConnectionString);
        }));

        var provider = services.BuildServiceProvider();

        // Force the hosted service to be materialized, triggering the configure delegate.
        _ = provider.GetServices<IHostedService>().ToList();

        await Assert.That(capturedId).IsEqualTo("configured-processor-id");
    }

    [Test]
    public async ValueTask di_resolves_connection_from_client_options_in_service_provider() {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(KurrentClientOptions.Parse(TestConnectionString));

        services.AddKurrentSurge(surge => surge.AddProcessor(new TestableRecordProcessor(), opts => {
            opts.ProcessorId = "sp-resolved";
        }));

        var provider     = services.BuildServiceProvider();
        var droneService = provider.GetServices<IHostedService>().OfType<DroneService<TestableRecordProcessor>>().Single();

        await Assert.That(droneService.Drone.Options.ClientOptions).IsNotNull();
        await Assert.That(((KurrentClientOptions)droneService.Drone.Options.ClientOptions!).Endpoints[0].Host).IsEqualTo("localhost");
    }

    [Test]
    public async ValueTask di_explicit_connection_wins_over_client_options_in_service_provider() {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(KurrentClientOptions.Parse("kurrentdb://other-host:2113"));

        services.AddKurrentSurge(surge => surge.AddProcessor(new TestableRecordProcessor(), opts => {
            opts.ProcessorId = "explicit-wins";
            opts.Connection(TestConnectionString);
        }));

        var provider     = services.BuildServiceProvider();
        var droneService = provider.GetServices<IHostedService>().OfType<DroneService<TestableRecordProcessor>>().Single();

        await Assert.That(((KurrentClientOptions)droneService.Drone.Options.ClientOptions!).Endpoints[0].Host).IsEqualTo("localhost");
    }

    [Test]
    public async ValueTask di_resolves_connection_from_configuration_by_processor_id() {
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

        services.AddKurrentSurge(surge => surge.AddProcessor(new TestableRecordProcessor(), opts => opts.ProcessorId = "by-id-processor"));

        var provider     = services.BuildServiceProvider();
        var droneService = provider.GetServices<IHostedService>().OfType<DroneService<TestableRecordProcessor>>().Single();

        await Assert.That(((KurrentClientOptions)droneService.Drone.Options.ClientOptions!).Endpoints[0].Host).IsEqualTo("localhost");
    }

    [Test]
    public async ValueTask di_resolves_connection_from_configuration_default_resource_name() {
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

        services.AddKurrentSurge(surge => surge.AddProcessor(new TestableRecordProcessor(), opts => opts.ProcessorId = "unknown-processor"));

        var provider     = services.BuildServiceProvider();
        var droneService = provider.GetServices<IHostedService>().OfType<DroneService<TestableRecordProcessor>>().Single();

        await Assert.That(((KurrentClientOptions)droneService.Drone.Options.ClientOptions!).Endpoints[0].Host).IsEqualTo("localhost");
    }

    record TestSettings {
        public string ProcessorId { get; init; } = "";
    }

    #endregion // DI Registration Tests

    #region ->> Configuration Binding Tests <<-

    [Test]
    public async ValueTask settings_loaded_from_json_configuration_by_processor_type_name() {
        var configuration = BuildJsonConfiguration("""
            {
              "Kurrent": {
                "Surge": {
                  "Processors": {
                    "TestableRecordProcessor": {
                      "ProcessorId": "config-processor",
                      "Batching": { "MaxBatchSize": 2000 },
                      "Checkpoint": {
                        "Interval": 15000
                      }
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
        await Assert.That(settings.Batching.MaxBatchSize).IsEqualTo(2000);
        await Assert.That(settings.Checkpoint.Interval).IsEqualTo(TimeSpan.FromMilliseconds(15000));
    }

    [Test]
    public async ValueTask json_configuration_supports_camel_case_properties() {
        var configuration = BuildJsonConfiguration("""
            {
              "Kurrent": {
                "Surge": {
                  "Processors": {
                    "TestableRecordProcessor": {
                      "processorId": "camel-processor",
                      "batching": { "maxBatchSize": 1500 },
                      "checkpoint": {
                        "interval": 20000
                      },
                      "subscription": {
                        "initialPosition": "Earliest"
                      }
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
        await Assert.That(settings.Batching.MaxBatchSize).IsEqualTo(1500);
        await Assert.That(settings.Checkpoint.Interval).IsEqualTo(TimeSpan.FromMilliseconds(20000));
        await Assert.That(settings.Subscription.InitialPosition).IsEqualTo(SubscriptionInitialPosition.Earliest);
    }

    [Test]
    public async ValueTask json_configuration_does_not_support_snake_case_properties() {
        // snake_case is NOT supported by default .NET configuration binding
        var configuration = BuildJsonConfiguration("""
            {
              "Kurrent": {
                "Surge": {
                  "Processors": {
                    "TestableRecordProcessor": {
                      "processor_id": "snake-processor",
                      "max_parallelism": 4,
                      "batching": { "max_batch_size": 1500 }
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
            opts.ProcessorId    = "fallback-processor";
            opts.MaxParallelism = 1;
        });

        await Assert.That(settings.ProcessorId).IsEqualTo("fallback-processor");
        await Assert.That(settings.MaxParallelism).IsEqualTo(1);
        await Assert.That(settings.Batching.MaxBatchSize).IsEqualTo(500);
    }

    [Test]
    public async ValueTask fluent_configuration_overrides_json_settings() {
        var configuration = BuildJsonConfiguration("""
            {
              "Kurrent": {
                "Surge": {
                  "Processors": {
                    "TestableRecordProcessor": {
                      "ProcessorId": "config-processor",
                      "Batching": { "MaxBatchSize": 2000 }
                    }
                  }
              
                }
              },
              "ConnectionStrings": {
                "kurrentdb": "kurrentdb://localhost:2113"
              }
            }
            """);

        var settings = ResolveProcessorOptions(configuration, configure: opts => opts.ProcessorId = "fluent-processor");

        await Assert.That(settings.ProcessorId).IsEqualTo("fluent-processor");
        await Assert.That(settings.Batching.MaxBatchSize).IsEqualTo(2000);
    }

    [Test]
    public async ValueTask missing_json_section_uses_defaults() {
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

        await Assert.That(settings.Batching.MaxBatchSize).IsEqualTo(500);
        await Assert.That(settings.Checkpoint.Interval).IsEqualTo(TimeSpan.FromMilliseconds(30000));
    }

    [Test]
    public async ValueTask nested_subscription_settings_bind_from_json() {
        var configuration = BuildJsonConfiguration("""
            {
              "Kurrent": {
                "Surge": {
                  "Processors": {
                    "TestableRecordProcessor": {
                      "ProcessorId": "test-processor",
                      "MaxParallelism": 1,
                      "Subscription": {
                        "InitialPosition": "Earliest"
                      }
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
    public async ValueTask configuration_binding_works_with_service_provider_overload() {
        var configuration = BuildJsonConfiguration("""
            {
              "Kurrent": {
                "Surge": {
                  "Processors": {
                    "TestableRecordProcessor": {
                      "Batching": { "MaxBatchSize": 3000 }
                    }
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
        services.AddKurrentSurge(surge => surge.AddProcessor(new TestableRecordProcessor(), (opts, sp) => {
            var testSettings = sp.GetRequiredService<TestSettings>();
            opts.ProcessorId    = testSettings.ProcessorId;
            opts.MaxParallelism = 1;
        }));

        var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<DroneService<TestableRecordProcessor>>()
            .Single();

        var settings = hostedService.Drone.Options;

        await Assert.That(settings.ProcessorId).IsEqualTo("sp-processor");
        await Assert.That(settings.Batching.MaxBatchSize).IsEqualTo(3000);
    }

    [Test]
    public async ValueTask no_configuration_registered_uses_defaults() {
        var services = new ServiceCollection();
        // Intentionally NOT registering IConfiguration
        services.AddLogging();
        services.AddKurrentSurge(surge => surge.AddProcessor(new TestableRecordProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId    = "test-processor";
            opts.MaxParallelism = 1;
        }));

        var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<DroneService<TestableRecordProcessor>>()
            .Single();

        var settings = hostedService.Drone.Options;

        await Assert.That(settings.Batching.MaxBatchSize).IsEqualTo(500);
        await Assert.That(settings.Checkpoint.Interval).IsEqualTo(TimeSpan.FromMilliseconds(30000));
    }

    [Test]
    public async ValueTask processor_id_auto_generated_from_processor_type_name_when_not_in_config() {
        var configuration = BuildJsonConfiguration("""
            {
              "Kurrent": {
                "Surge": {
                  "Processors": {
                    "TestableRecordProcessor": {
                      "Batching": { "MaxBatchSize": 2000 }
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

        await Assert.That(settings.ProcessorId).IsEqualTo("testable-record-processor");
        await Assert.That(settings.Batching.MaxBatchSize).IsEqualTo(2000);
    }

    [Test]
    public async ValueTask explicit_processor_id_in_config_overrides_auto_generation() {
        var configuration = BuildJsonConfiguration("""
            {
              "Kurrent": {
                "Surge": {
                  "Processors": {
                    "TestableRecordProcessor": {
                      "ProcessorId": "my-custom-id"
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

    [Test]
    public async ValueTask unusable_configuration_value_fails_drone_materialization() {
        // Arrange
        var configuration = BuildJsonConfiguration("""
            {
              "Kurrent": {
                "Surge": {
                  "Processors": {
                    "TestableRecordProcessor": {
                      "Checkpoint": { "Interval": "not-a-duration" }
                    }
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
        services.AddLogging();
        services.AddKurrentSurge(surge => surge.AddProcessor(new TestableRecordProcessor()));

        var provider = services.BuildServiceProvider();

        // Act & Assert
        await Assert.That(() => provider.GetServices<IHostedService>().ToList()).Throws<InvalidOperationException>();
    }

    [Test]
    public async ValueTask id_named_section_applies_as_deployment_override_when_configure_sets_custom_processor_id() {
        // Arrange
        var configuration = BuildJsonConfiguration("""
            {
              "Kurrent": {
                "Surge": {
                  "Processors": {
                    "fraud-eu": {
                      "Batching": { "MaxBatchSize": 2500 }
                    }
                  }
                }
              },
              "ConnectionStrings": {
                "kurrentdb": "kurrentdb://localhost:2113"
              }
            }
            """);

        var expectedBatchSize = 2500;

        // Act
        var settings = ResolveProcessorOptions(configuration, configure: opts => {
            opts.ProcessorId           = "fraud-eu";
            opts.Batching.MaxBatchSize = 100;
        });

        // Assert
        await Assert.That(settings.ProcessorId).IsEqualTo("fraud-eu");
        await Assert.That(settings.Batching.MaxBatchSize).IsEqualTo(expectedBatchSize);
    }

    [Test]
    public async ValueTask id_named_section_connection_does_not_override_connection_set_in_code() {
        // Arrange
        var configuration = BuildJsonConfiguration("""
            {
              "Kurrent": {
                "Surge": {
                  "Processors": {
                    "fraud-eu": {
                      "ConnectionString": "kurrentdb://other-host:2113"
                    }
                  }
                }
              }
            }
            """);

        // Act
        var settings = ResolveProcessorOptions(configuration, configure: opts => {
            opts.ProcessorId = "fraud-eu";
            opts.Connection(TestConnectionString);
        });

        // Assert
        await Assert.That(((KurrentClientOptions)settings.ClientOptions!).Endpoints[0].Host).IsEqualTo("localhost");
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
        services.AddKurrentSurge(surge => surge.AddProcessor(new TestableRecordProcessor(), opts => configure?.Invoke(opts)));

        var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<DroneService<TestableRecordProcessor>>()
            .Single();

        return hostedService.Drone.Options;
    }

    #endregion // Configuration Binding Tests

    #region ->> Batch Processor DI Registration Tests <<-

    [Test]
    public async ValueTask batch_processor_duplicate_registration_is_idempotent() {
        var services = new ServiceCollection();

        services.AddKurrentSurge(surge => surge.AddBatchProcessor(new TestableBatchProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId = "first";
        }));
        services.AddKurrentSurge(surge => surge.AddBatchProcessor(new TestableBatchProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId = "second";
        }));

        var hostedServiceCount = services.Count(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationFactory is not null);

        await Assert.That(hostedServiceCount).IsEqualTo(1);
    }

    [Test]
    public async ValueTask batch_processor_duplicate_registration_with_factory_is_idempotent() {
        var services = new ServiceCollection();

        services.AddKurrentSurge(surge => surge.AddBatchProcessor<TestableBatchProcessor>(
            _ => new TestableBatchProcessor(),
            opts => {
                opts.Connection(TestConnectionString);
                opts.ProcessorId = "first";
            }));
        services.AddKurrentSurge(surge => surge.AddBatchProcessor<TestableBatchProcessor>(
            _ => new TestableBatchProcessor(),
            opts => {
                opts.Connection(TestConnectionString);
                opts.ProcessorId = "second";
            }));

        var hostedServiceCount = services.Count(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationFactory is not null);

        await Assert.That(hostedServiceCount).IsEqualTo(1);
    }

    [Test]
    public async ValueTask batch_processor_configuration_with_service_provider_can_access_services() {
        var services = new ServiceCollection();
        services.AddSingleton(new TestSettings { ProcessorId = "batch-configured-id" });
        services.AddLogging();

        string? capturedId = null;

        services.AddKurrentSurge(surge => surge.AddBatchProcessor(new TestableBatchProcessor(), (opts, sp) => {
            var settings = sp.GetRequiredService<TestSettings>();
            capturedId = settings.ProcessorId;
            opts.ProcessorId = capturedId;
            opts.Connection(TestConnectionString);
        }));

        var provider = services.BuildServiceProvider();
        _ = provider.GetServices<IHostedService>().ToList();

        await Assert.That(capturedId).IsEqualTo("batch-configured-id");
    }

    [Test]
    public async ValueTask batch_processor_di_resolves_connection_from_client_options_in_service_provider() {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(KurrentClientOptions.Parse(TestConnectionString));

        services.AddKurrentSurge(surge => surge.AddBatchProcessor(new TestableBatchProcessor(), opts => {
            opts.ProcessorId = "batch-sp-resolved";
        }));

        var provider     = services.BuildServiceProvider();
        var droneService = provider.GetServices<IHostedService>().OfType<DroneService<TestableBatchProcessor>>().Single();

        await Assert.That(droneService.Drone.Options.ClientOptions).IsNotNull();
        await Assert.That(((KurrentClientOptions)droneService.Drone.Options.ClientOptions!).Endpoints[0].Host).IsEqualTo("localhost");
    }

    [Test]
    public async ValueTask batch_processor_di_explicit_connection_wins_over_client_options() {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(KurrentClientOptions.Parse("kurrentdb://other-host:2113"));

        services.AddKurrentSurge(surge => surge.AddBatchProcessor(new TestableBatchProcessor(), opts => {
            opts.ProcessorId = "batch-explicit-wins";
            opts.Connection(TestConnectionString);
        }));

        var provider     = services.BuildServiceProvider();
        var droneService = provider.GetServices<IHostedService>().OfType<DroneService<TestableBatchProcessor>>().Single();

        await Assert.That(((KurrentClientOptions)droneService.Drone.Options.ClientOptions!).Endpoints[0].Host).IsEqualTo("localhost");
    }

    [Test]
    public async ValueTask batch_processor_auto_generates_processor_id_from_type_name() {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddKurrentSurge(surge => surge.AddBatchProcessor(new TestableBatchProcessor(), opts => {
            opts.Connection(TestConnectionString);
        }));

        var provider     = services.BuildServiceProvider();
        var droneService = provider.GetServices<IHostedService>().OfType<DroneService<TestableBatchProcessor>>().Single();

        await Assert.That(droneService.Drone.Options.ProcessorId).IsEqualTo("testable-batch-processor");
    }

    [Test]
    public async ValueTask batch_and_record_processors_register_independently() {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddKurrentSurge(surge => surge.AddProcessor(new TestableRecordProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId = "record-processor";
        }));
        services.AddKurrentSurge(surge => surge.AddBatchProcessor(new TestableBatchProcessor(), opts => {
            opts.Connection(TestConnectionString);
            opts.ProcessorId = "batch-processor";
        }));

        var provider = services.BuildServiceProvider();
        var hosted   = provider.GetServices<IHostedService>().ToList();

        var expectedRecordService = hosted.OfType<DroneService<TestableRecordProcessor>>().SingleOrDefault();
        var expectedBatchService  = hosted.OfType<DroneService<TestableBatchProcessor>>().SingleOrDefault();

        await Assert.That(expectedRecordService).IsNotNull();
        await Assert.That(expectedBatchService).IsNotNull();
    }

    [Test]
    public async ValueTask batch_processor_configuration_binding_from_json() {
        var configuration = BuildJsonConfiguration("""
            {
              "Kurrent": {
                "Surge": {
                  "Processors": {
                    "TestableBatchProcessor": {
                      "ProcessorId": "batch-config-processor",
                      "Batching": { "MaxBatchSize": 2000 }
                    }
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
        services.AddLogging();
        services.AddKurrentSurge(surge => surge.AddBatchProcessor(new TestableBatchProcessor()));

        var provider     = services.BuildServiceProvider();
        var droneService = provider.GetServices<IHostedService>().OfType<DroneService<TestableBatchProcessor>>().Single();

        await Assert.That(droneService.Drone.Options.ProcessorId).IsEqualTo("batch-config-processor");
        await Assert.That(droneService.Drone.Options.Batching.MaxBatchSize).IsEqualTo(2000);
    }

    sealed class TestableBatchProcessor : RecordBatchProcessor {
        public override ValueTask ProcessBatch(BatchContext context) => ValueTask.CompletedTask;
    }

    #endregion // Batch Processor DI Registration Tests

}
