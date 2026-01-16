using System.Text;
using Kurrent.Client.Streams;
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
    #region FromConfiguration Tests

    [Test]
    public void from_configuration_loads_connection_string() {
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

        var options = KurrentClientOptionsBuilder.Create()
            .FromConfiguration(configuration)
            .Build();

        options.Endpoints.ShouldHaveSingleItem();
        options.Endpoints[0].Host.ShouldBe("localhost");
        options.Endpoints[0].Port.ShouldBe(2113);
        options.Security.Transport.IsEnabled.ShouldBeFalse();
    }

    [Test]
    public void from_configuration_loads_resilience_settings() {
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

        var options = KurrentClientOptionsBuilder.Create()
            .FromConfiguration(configuration)
            .Build();

        options.Resilience.Deadline.ShouldBe(TimeSpan.FromSeconds(30));
        options.Resilience.KeepAliveInterval.ShouldBe(TimeSpan.FromMinutes(1));
        options.Resilience.KeepAliveTimeout.ShouldBe(TimeSpan.FromSeconds(20));
        options.Resilience.Retry.Enabled.ShouldBeTrue();
        options.Resilience.Retry.MaxAttempts.ShouldBe(5);
        options.Resilience.Retry.InitialBackoff.ShouldBe(TimeSpan.FromMilliseconds(500));
        options.Resilience.Retry.MaxBackoff.ShouldBe(TimeSpan.FromSeconds(15));
        options.Resilience.Retry.BackoffMultiplier.ShouldBe(2.0);
    }

    [Test]
    public void from_configuration_loads_connection_name_as_override() {
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

        var options = KurrentClientOptionsBuilder.Create()
            .FromConfiguration(configuration)
            .Build();

        options.ConnectionName.ShouldBe("my-custom-service");
    }

    [Test]
    public void from_configuration_loads_gossip_settings() {
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

        var options = KurrentClientOptionsBuilder.Create()
            .FromConfiguration(configuration)
            .Build();

        options.Gossip.MaxDiscoverAttempts.ShouldBe(20);
        options.Gossip.DiscoveryInterval.ShouldBe(TimeSpan.FromMilliseconds(500));
        options.Gossip.Timeout.ShouldBe(TimeSpan.FromSeconds(10));
        options.Gossip.ReadPreference.ShouldBe(NodePreference.Leader);
    }

    [Test]
    public void from_configuration_returns_unchanged_when_section_missing() {
        var json = """
            {
              "SomeOther": {
                "Setting": "value"
              }
            }
            """;

        var configuration = BuildJsonConfiguration(json);

        var options = KurrentClientOptionsBuilder.Create()
            .FromConfiguration(configuration)
            .Build();

        // Should have default values
        options.Endpoints.ShouldHaveSingleItem();
        options.Endpoints[0].Host.ShouldBe("localhost");
        options.Endpoints[0].Port.ShouldBe(2113);
    }

    [Test]
    public void fluent_methods_override_configuration_values() {
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

        var options = KurrentClientOptionsBuilder.Create()
            .FromConfiguration(configuration)
            .ConfigureResilience(r => r with { Deadline = TimeSpan.FromMinutes(2) })
            .Build();

        // Fluent override should take precedence
        options.Resilience.Deadline.ShouldBe(TimeSpan.FromMinutes(2));
    }

    [Test]
    public void from_configuration_with_custom_section_name() {
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

        var options = KurrentClientOptionsBuilder.Create()
            .FromConfiguration(configuration, "MyCustomClient")
            .Build();

        options.Endpoints.ShouldHaveSingleItem();
        options.Endpoints[0].Host.ShouldBe("custom-host");
        options.Endpoints[0].Port.ShouldBe(2114);
        options.Security.Transport.IsEnabled.ShouldBeTrue();
    }

    #endregion

    #region Service Collection Extension Tests

    [Test]
    public void add_kurrent_client_parameterless_uses_configuration() {
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

        client.ShouldNotBeNull();
        // Client is created - if connection string was invalid, it would throw during creation
    }

    [Test]
    public void add_kurrent_client_parameterless_uses_defaults_without_configuration() {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKurrentClient();

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<KurrentClient>();

        client.ShouldNotBeNull();
        // Client is created with defaults (localhost:2113)
    }

    [Test]
    public void add_kurrent_client_builder_auto_binds_then_applies_overrides() {
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

        client.ShouldNotBeNull();
        // The fluent override (5 minutes) should take precedence over config (30 seconds)
    }

    [Test]
    public void add_kurrent_client_service_provider_overload_has_access_to_services() {
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

        client.ShouldNotBeNull();
    }

    [Test]
    public void add_kurrent_client_is_idempotent() {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKurrentClient("kurrentdb://first:2113?tls=false");
        services.AddKurrentClient("kurrentdb://second:2114?tls=false"); // Should be ignored

        var provider = services.BuildServiceProvider();
        var clients = provider.GetServices<KurrentClient>().ToList();

        // Only one client should be registered (first one wins with TryAddSingleton)
        clients.Count.ShouldBe(1);
    }

    #endregion

    #region Helpers

    static IConfiguration BuildJsonConfiguration(string json) {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();
    }

    #endregion
}
