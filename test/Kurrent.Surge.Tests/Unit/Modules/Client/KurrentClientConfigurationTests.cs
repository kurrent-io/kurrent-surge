using System.Text;
using Kurrent.Surge.Schema.Catalogs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kurrent.Surge.Tests.Modules.Client;

/// <summary>
/// Unit tests for KurrentClient configuration and DI registration.
/// </summary>
[Category("Client")]
[Category("Configuration")]
public class KurrentClientConfigurationTests {
    #region ->> KurrentClient.Create Tests <<-

    [Test]
    public async ValueTask create_with_connection_string_only_parses_and_constructs() {
        // Arrange & Act
        await using var client = KurrentClient.Create("kurrentdb://localhost:2118?tls=false");
        
        // Assert
        await Assert.That(client.Options.Endpoints[0].Host).IsEqualTo("localhost");
        await Assert.That(client.Options.Endpoints[0].Port).IsEqualTo(2118);
        await Assert.That(client.Options.Security.Transport.IsEnabled).IsFalse();
    }

    [Test]
    public async ValueTask create_with_connection_string_and_configure_runs_configure_after_parse() {
        // Arrange
        var customName = "custom-connection-name";

        // Act
        await using var client = KurrentClient.Create(
            "kurrentdb://localhost:2119?tls=false",
            opts => {
                opts.ConnectionName       = customName;
                opts.Resilience.Deadline  = TimeSpan.FromSeconds(45);
            });

        // Assert — parsed endpoint preserved, configure's mutations applied on top
        await Assert.That(client.Options.Endpoints[0].Host).IsEqualTo("localhost");
        await Assert.That(client.Options.Endpoints[0].Port).IsEqualTo(2119);
        await Assert.That(client.Options.ConnectionName).IsEqualTo(customName);
        await Assert.That(client.Options.Resilience.Deadline).IsEqualTo(TimeSpan.FromSeconds(45));
    }

    #endregion

    #region ->> AddKurrentSurge (DI) Tests <<-

    [Test]
    public async ValueTask scope_without_connection_uses_connection_strings_from_configuration() {
        // Arrange — standard .NET ConnectionStrings section, key "KurrentDB"
        var json = """
            {
              "ConnectionStrings": {
                "KurrentDB": "kurrentdb://config-host:2115?tls=false"
              }
            }
            """;

        var configuration = BuildJsonConfiguration(json);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddKurrentSurge(_ => { });

        // Act
        var provider = services.BuildServiceProvider();
        var client   = provider.GetRequiredService<KurrentClient>();

        // Assert
        await Assert.That(client.Options.Endpoints[0].Host).IsEqualTo("config-host");
        await Assert.That(client.Options.Endpoints[0].Port).IsEqualTo(2115);
    }

    [Test]
    public async ValueTask connection_with_options_instance_registers_verbatim() {
        // Arrange
        var options = KurrentClientOptions.Parse("kurrentdb://prebuilt:2116?tls=false");
        options.ConnectionName = "pre-built-name";

        // Act
        var services = new ServiceCollection();
        services.AddKurrentSurge(surge => surge.Connection(options));

        var provider = services.BuildServiceProvider();
        var client   = provider.GetRequiredService<KurrentClient>();

        // Assert
        await Assert.That(client.Options.Endpoints[0].Host).IsEqualTo("prebuilt");
        await Assert.That(client.Options.Endpoints[0].Port).IsEqualTo(2116);
        await Assert.That(client.Options.ConnectionName).IsEqualTo("pre-built-name");
    }

    [Test]
    public async ValueTask connection_without_a_string_tunes_the_configured_connection() {
        // Arrange — no connection string is supplied, so the delegate tunes whatever configuration holds
        var customName    = "configured-via-action";
        var configuration = BuildJsonConfiguration("""
            {
              "ConnectionStrings": {
                "KurrentDB": "kurrentdb://from-config:2117?tls=false"
              }
            }
            """);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddKurrentSurge(surge => surge.Connection(opts => opts.ConnectionName = customName));

        // Act
        var provider = services.BuildServiceProvider();
        var client   = provider.GetRequiredService<KurrentClient>();

        // Assert — configured endpoint preserved, the delegate's mutation applied on top
        await Assert.That(client.Options.Endpoints[0].Host).IsEqualTo("from-config");
        await Assert.That(client.Options.ConnectionName).IsEqualTo(customName);
    }

    [Test]
    public async ValueTask used_catalogs_join_the_clients_selection() {
        // Arrange — a hand-built catalog reaches the client through UseCatalog
        var catalog = new MessageCatalog("hand-built").Map<SampleMessage>("sample-v1");

        var services = new ServiceCollection();
        services.AddKurrentSurge(surge => surge.Connection(
            "kurrentdb://localhost:2113?tls=false",
            opts => opts.Messages.UseCatalog(catalog)));

        // Act
        var provider = services.BuildServiceProvider();
        var client   = provider.GetRequiredService<KurrentClient>();

        var (resolver, _) = client.Options.Messages.BuildResolver();

        // Assert — the catalog's mapping resolves on the client
        await Assert.That(resolver.ResolveSchemaName(typeof(SampleMessage)).Value).IsEqualTo("sample-v1");
    }

    [Test]
    public async ValueTask conflicting_catalog_mappings_fail_client_construction() {
        // Arrange — catalogs COMPOSE (no override semantics): the same type pinned to two
        // different names is a contract conflict and must fail loudly at construction.
        var usedCatalog = new MessageCatalog("used").Map<SampleMessage>("used");

        var services = new ServiceCollection();
        services.AddKurrentSurge(surge => surge.Connection(
            "kurrentdb://localhost:2113?tls=false",
            opts => opts.Messages
                .UseCatalog(usedCatalog)
                .MapMessage<SampleMessage>("user-override")));

        var provider = services.BuildServiceProvider();

        // Act
        var resolving = () => provider.GetRequiredService<KurrentClient>();

        // Assert
        await Assert.That(resolving).ThrowsException();
    }

    [Test]
    public async ValueTask scope_uses_registered_options_verbatim() {
        // Arrange
        var preBuilt = KurrentClientOptions.Parse("kurrentdb://preconfigured:2116?tls=false");
        preBuilt.ConnectionName = "pre-built-name";

        var services = new ServiceCollection();
        services.AddSingleton(preBuilt);
        services.AddKurrentSurge(_ => { });

        // Act
        var provider = services.BuildServiceProvider();
        var client   = provider.GetRequiredService<KurrentClient>();

        // Assert — options already in the container are not replaced by the scope's fallback
        await Assert.That(client.Options).IsSameReferenceAs(preBuilt);
        await Assert.That(client.Options.Endpoints[0].Host).IsEqualTo("preconfigured");
        await Assert.That(client.Options.ConnectionName).IsEqualTo("pre-built-name");
    }

    #endregion

    #region ->> Helpers <<-

    static IConfiguration BuildJsonConfiguration(string json) {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();
    }

    record SampleMessage(string Value);

    #endregion
}
