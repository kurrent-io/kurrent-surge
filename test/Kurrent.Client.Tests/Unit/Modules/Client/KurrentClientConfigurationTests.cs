using System.Text;
using Kurrent.Client.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kurrent.Client.Tests.Modules.Client;

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

    #region ->> AddKurrentClient (DI) Tests <<-

    [Test]
    public async ValueTask add_kurrent_client_uses_connection_strings_kurrentdb_from_configuration() {
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
        services.AddKurrentClient();

        var provider = services.BuildServiceProvider();
        var client   = provider.GetRequiredService<KurrentClient>();

        // Assert
        await Assert.That(client.Options.Endpoints[0].Host).IsEqualTo("config-host");
        await Assert.That(client.Options.Endpoints[0].Port).IsEqualTo(2115);
    }

    [Test]
    public async ValueTask add_kurrent_client_uses_defaults_without_configuration() {
        // Arrange & Act
        var services = new ServiceCollection();
        services.AddKurrentClient();

        var provider = services.BuildServiceProvider();
        var client   = provider.GetRequiredService<KurrentClient>();

        // Assert — default endpoint (localhost:2113)
        await Assert.That(client).IsNotNull();
        await Assert.That(client.Options.Endpoints[0].Host).IsEqualTo("localhost");
        await Assert.That(client.Options.Endpoints[0].Port).IsEqualTo(2113);
    }

    [Test]
    public async ValueTask add_kurrent_client_is_idempotent() {
        // Arrange
        var services = new ServiceCollection();
        services.AddKurrentClient("kurrentdb://first:2113?tls=false");
        services.AddKurrentClient("kurrentdb://second:2114?tls=false"); // Should be ignored

        // Act
        var provider = services.BuildServiceProvider();
        var clients  = provider.GetServices<KurrentClient>().ToList();

        // Assert — only one registration wins (TryAddSingleton semantics)
        await Assert.That(clients.Count).IsEqualTo(1);
        await Assert.That(clients[0].Options.Endpoints[0].Host).IsEqualTo("first");
    }

    [Test]
    public async ValueTask add_kurrent_client_with_options_overload_registers_verbatim() {
        // Arrange
        var options = KurrentClientOptions.Parse("kurrentdb://prebuilt:2116?tls=false");
        options.ConnectionName = "pre-built-name";

        // Act
        var services = new ServiceCollection();
        services.AddKurrentClient(options);

        var provider = services.BuildServiceProvider();
        var client   = provider.GetRequiredService<KurrentClient>();

        // Assert
        await Assert.That(client.Options.Endpoints[0].Host).IsEqualTo("prebuilt");
        await Assert.That(client.Options.Endpoints[0].Port).IsEqualTo(2116);
        await Assert.That(client.Options.ConnectionName).IsEqualTo("pre-built-name");
    }

    [Test]
    public async ValueTask add_kurrent_client_configure_action_runs_after_sp_defaults() {
        // Arrange
        var customName = "configured-via-action";

        var services = new ServiceCollection();
        services.AddKurrentClient(opts => opts.ConnectionName = customName);

        // Act
        var provider = services.BuildServiceProvider();
        var client   = provider.GetRequiredService<KurrentClient>();

        // Assert
        await Assert.That(client.Options.ConnectionName).IsEqualTo(customName);
    }

    [Test]
    public async ValueTask add_kurrent_client_applies_registered_message_type_mapper() {
        // Arrange
        var mapper = MessageTypeMapper.Create(m => m.Map<SampleMessage>("sample-v1"));

        var services = new ServiceCollection();
        services.AddSingleton(mapper);
        services.AddKurrentClient();

        // Act
        var provider = services.BuildServiceProvider();
        var client   = provider.GetRequiredService<KurrentClient>();

        // Assert — SP-registered mapper is applied onto opts.Schema.Mapper
        await Assert.That(client.Options.Schema.Mapper).IsSameReferenceAs(mapper);
        await Assert.That(client.Options.Schema.Mapper.TryGetSchemaName(typeof(SampleMessage), out var name)).IsTrue();
        await Assert.That(name.Value).IsEqualTo("sample-v1");
    }

    [Test]
    public async ValueTask add_kurrent_client_configure_action_can_override_registered_mapper() {
        // Arrange
        var registeredMapper = MessageTypeMapper.Create(m => m.Map<SampleMessage>("registered"));
        var userMapper       = MessageTypeMapper.Create(m => m.Map<SampleMessage>("user-override"));

        var services = new ServiceCollection();
        services.AddSingleton(registeredMapper);
        services.AddKurrentClient(opts => opts.Schema.Mapper = userMapper);

        // Act
        var provider = services.BuildServiceProvider();
        var client   = provider.GetRequiredService<KurrentClient>();

        // Assert — user's configure wins over SP-resolved mapper
        await Assert.That(client.Options.Schema.Mapper).IsSameReferenceAs(userMapper);
        await Assert.That(client.Options.Schema.Mapper).IsNotSameReferenceAs(registeredMapper);
    }

    [Test]
    public async ValueTask add_kurrent_client_uses_registered_options_verbatim() {
        // Arrange
        var preBuilt = KurrentClientOptions.Parse("kurrentdb://preconfigured:2116?tls=false");
        preBuilt.ConnectionName = "pre-built-name";

        var services = new ServiceCollection();
        services.AddSingleton(preBuilt);
        services.AddKurrentClient();

        // Act
        var provider = services.BuildServiceProvider();
        var client   = provider.GetRequiredService<KurrentClient>();

        // Assert — registered options used as-is, SP-resolved defaults still get applied on top (mapper/logger/decoder)
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
