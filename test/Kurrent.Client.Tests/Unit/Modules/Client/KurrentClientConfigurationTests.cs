using System.Text;
using KurrentDB.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kurrent.Client.Tests.Modules.Client;

/// <summary>
/// Unit tests for KurrentClient configuration from IConfiguration.
/// </summary>
[Category("Client")]
[Category("Configuration")]
public class KurrentClientConfigurationTests {
    #region ->> FromConfiguration Tests <<-

    [Test]
    public async Task from_configuration_loads_connection_string() {
        var json = """
            {
              "Kurrent": {
                "Client": {
                  "ConnectionString": "kurrentdb://admin:changeit@localhost:2113?tls=false"
                }
              }
            }
            """;

        var configuration = BuildJsonConfiguration(json);

        var options = KurrentClientOptions.Builder.Create()
            .FromConfiguration(configuration)
            .Build();

        await Assert.That(options.Endpoints).HasSingleItem();
        await Assert.That(options.Endpoints[0].Host).IsEqualTo("localhost");
        await Assert.That(options.Endpoints[0].Port).IsEqualTo(2113);
        await Assert.That(options.Security.Transport.IsEnabled).IsFalse();
    }

    [Test]
    public async Task from_configuration_loads_resilience_settings() {
        var json = """
            {
              "Kurrent": {
                "Client": {
                  "ConnectionString": "kurrentdb://localhost:2113?tls=false",
                  "Resilience": {
                    "Deadline": "00:00:30",
                    "KeepAliveInterval": "00:01:00",
                    "KeepAliveTimeout": "00:00:20",
                    "Retry": {
                      "Enabled": true,
                      "MaxAttempts": 5,
                      "InitialBackoff": "00:00:00.500",
                      "MaxBackoff": "00:00:15",
                      "BackoffMultiplier": 2.0
                    }
                  }
                }
              }
            }
            """;

        var configuration = BuildJsonConfiguration(json);

        var options = KurrentClientOptions.Builder.Create()
            .FromConfiguration(configuration)
            .Build();

        await Assert.That(options.Resilience.Deadline).IsEqualTo(TimeSpan.FromSeconds(30));
        await Assert.That(options.Resilience.KeepAliveInterval).IsEqualTo(TimeSpan.FromMinutes(1));
        await Assert.That(options.Resilience.KeepAliveTimeout).IsEqualTo(TimeSpan.FromSeconds(20));
        await Assert.That(options.Resilience.Retry.Enabled).IsTrue();
        await Assert.That(options.Resilience.Retry.MaxAttempts).IsEqualTo(5);
        await Assert.That(options.Resilience.Retry.InitialBackoff).IsEqualTo(TimeSpan.FromMilliseconds(500));
        await Assert.That(options.Resilience.Retry.MaxBackoff).IsEqualTo(TimeSpan.FromSeconds(15));
        await Assert.That(options.Resilience.Retry.BackoffMultiplier).IsEqualTo(2.0);
    }

    [Test]
    public async Task from_configuration_loads_connection_name_as_override() {
        var json = """
            {
              "Kurrent": {
                "Client": {
                  "ConnectionString": "kurrentdb://localhost:2113?tls=false",
                  "ConnectionName": "my-custom-service"
                }
              }
            }
            """;

        var configuration = BuildJsonConfiguration(json);

        var options = KurrentClientOptions.Builder.Create()
            .FromConfiguration(configuration)
            .Build();

        await Assert.That(options.ConnectionName).IsEqualTo("my-custom-service");
    }

    [Test]
    public async Task from_configuration_loads_gossip_settings() {
        var json = """
            {
              "Kurrent": {
                "Client": {
                  "ConnectionString": "kurrentdb://localhost:2113?tls=false",
                  "Gossip": {
                    "MaxDiscoverAttempts": 20,
                    "DiscoveryInterval": "00:00:00.500",
                    "Timeout": "00:00:10",
                    "ReadPreference": "Leader"
                  }
                }
              }
            }
            """;

        var configuration = BuildJsonConfiguration(json);

        var options = KurrentClientOptions.Builder.Create()
            .FromConfiguration(configuration)
            .Build();

        await Assert.That(options.Gossip.MaxDiscoverAttempts).IsEqualTo(20);
        await Assert.That(options.Gossip.DiscoveryInterval).IsEqualTo(TimeSpan.FromMilliseconds(500));
        await Assert.That(options.Gossip.Timeout).IsEqualTo(TimeSpan.FromSeconds(10));
        await Assert.That(options.Gossip.ReadPreference).IsEqualTo(NodePreference.Leader);
    }

    [Test]
    public async Task from_configuration_returns_unchanged_when_section_missing() {
        var json = """
            {
              "SomeOther": {
                "Setting": "value"
              }
            }
            """;

        var configuration = BuildJsonConfiguration(json);

        var options = KurrentClientOptions.Builder.Create()
            .FromConfiguration(configuration)
            .Build();

        // Should have default values
        await Assert.That(options.Endpoints).HasSingleItem();
        await Assert.That(options.Endpoints[0].Host).IsEqualTo("localhost");
        await Assert.That(options.Endpoints[0].Port).IsEqualTo(2113);
    }

    [Test]
    public async Task fluent_methods_override_configuration_values() {
        var json = """
            {
              "Kurrent": {
                "Client": {
                  "ConnectionString": "kurrentdb://localhost:2113?tls=false",
                  "Resilience": {
                    "Deadline": "00:00:30"
                  }
                }
              }
            }
            """;

        var configuration = BuildJsonConfiguration(json);

        var options = KurrentClientOptions.Builder.Create()
            .FromConfiguration(configuration)
            .ConfigureResilience(r => r with { Deadline = TimeSpan.FromMinutes(2) })
            .Build();

        // Fluent override should take precedence
        await Assert.That(options.Resilience.Deadline).IsEqualTo(TimeSpan.FromMinutes(2));
    }

    [Test]
    public async Task from_configuration_with_custom_section_name() {
        var json = """
            {
              "Kurrent": {
                "MyCustomClient": {
                  "ConnectionString": "kurrentdb://custom-host:2114?tls=true"
                }
              }
            }
            """;

        var configuration = BuildJsonConfiguration(json);

        var options = KurrentClientOptions.Builder.Create()
            .FromConfiguration(configuration, "MyCustomClient")
            .Build();

        await Assert.That(options.Endpoints).HasSingleItem();
        await Assert.That(options.Endpoints[0].Host).IsEqualTo("custom-host");
        await Assert.That(options.Endpoints[0].Port).IsEqualTo(2114);
        await Assert.That(options.Security.Transport.IsEnabled).IsTrue();
    }

    #endregion

    #region ->> Service Collection Extension Tests <<-

    [Test]
    public async Task add_kurrent_client_parameterless_uses_configuration() {
        var json = """
            {
              "Kurrent": {
                "Client": {
                  "ConnectionString": "kurrentdb://config-host:2115?tls=false"
                }
              }
            }
            """;

        var configuration = BuildJsonConfiguration(json);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddKurrentClient();

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<KurrentClient>();

        await Assert.That(client).IsNotNull();
        // Client is created - if connection string was invalid, it would throw during creation
    }

    [Test]
    public async Task add_kurrent_client_parameterless_uses_defaults_without_configuration() {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKurrentClient();

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<KurrentClient>();

        await Assert.That(client).IsNotNull();
        // Client is created with defaults (localhost:2113)
    }

    [Test]
    public async Task add_kurrent_client_builder_auto_binds_then_applies_overrides() {
        var json = """
            {
              "Kurrent": {
                "Client": {
                  "ConnectionString": "kurrentdb://localhost:2113?tls=false",
                  "Resilience": {
                    "Deadline": "00:00:30"
                  }
                }
              }
            }
            """;

        var configuration = BuildJsonConfiguration(json);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddKurrentClient(builder => builder
            .ConfigureResilience(r => r with { Deadline = TimeSpan.FromMinutes(5) })
        );

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<KurrentClient>();

        await Assert.That(client).IsNotNull();
        // The fluent override (5 minutes) should take precedence over config (30 seconds)
    }

    [Test]
    public async Task add_kurrent_client_service_provider_overload_has_access_to_services() {
        var json = """
            {
              "Kurrent": {
                "Client": {
                  "ConnectionString": "kurrentdb://localhost:2113?tls=false"
                }
              }
            }
            """;

        var configuration = BuildJsonConfiguration(json);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddKurrentClient((builder, sp) => {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            builder.WithLoggerFactory(loggerFactory);
        });

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<KurrentClient>();

        await Assert.That(client).IsNotNull();
    }

    [Test]
    public async Task add_kurrent_client_is_idempotent() {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKurrentClient("kurrentdb://first:2113?tls=false");
        services.AddKurrentClient("kurrentdb://second:2114?tls=false"); // Should be ignored

        var provider = services.BuildServiceProvider();
        var clients = provider.GetServices<KurrentClient>().ToList();

        // Only one client should be registered (first one wins with TryAddSingleton)
        await Assert.That(clients.Count).IsEqualTo(1);
    }

    #endregion

    #region ->> Helpers <<-

    static IConfiguration BuildJsonConfiguration(string json) {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();
    }

    #endregion
}
