using Kurrent.Surge.Client.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kurrent.Surge.Tests.Options;

/// <summary>
/// Unit tests for the <c>AddKurrentSurge</c> entry point and the scope's connection registration.
/// </summary>
public class KurrentSurgeBuilderTests {
    const string TestConnectionString = "kurrentdb://localhost:2113";

    #region ->> AddKurrentSurge Scope <<-

    [Test]
    public async ValueTask add_kurrent_surge_exposes_underlying_service_collection() {
        var services = new ServiceCollection();

        IServiceCollection? captured = null;
        services.AddKurrentSurge(surge => captured = surge.Services);

        await Assert.That(captured).IsSameReferenceAs(services);
    }

    [Test]
    public async ValueTask add_kurrent_surge_without_connection_defers_the_failure_to_first_resolve() {
        var services = new ServiceCollection();

        services.AddKurrentSurge(_ => { });
        var provider = services.BuildServiceProvider();

        // Registration always succeeds; a missing connection surfaces when the client is first used,
        // so a scope that only registers processors is not forced to declare one up front.
        await Assert.That(() => provider.GetRequiredService<KurrentClient>())
            .ThrowsExactly<KurrentClientNotConfiguredException>();
    }

    [Test]
    public async ValueTask add_kurrent_surge_falls_back_to_the_configuration_chain() {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["ConnectionStrings:KurrentSurge"] = "kurrentdb://from-config:2113?tls=false"
            })
            .Build());

        services.AddKurrentSurge(_ => { });

        var provider = services.BuildServiceProvider();
        var client   = provider.GetRequiredService<KurrentClient>();

        await Assert.That(client.Options.Endpoints[0].Host).IsEqualTo("from-config");
    }

    [Test]
    [Arguments("ConnectionStrings:KurrentSurge")]
    [Arguments("ConnectionStrings:kurrent-surge")]
    [Arguments("ConnectionStrings:kurrent_surge")]
    public async ValueTask configuration_chain_accepts_every_spelling_of_a_key(string key) {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                [key] = "kurrentdb://spelled:2113?tls=false"
            })
            .Build());

        services.AddKurrentSurge(_ => { });

        var provider = services.BuildServiceProvider();
        var client   = provider.GetRequiredService<KurrentClient>();

        await Assert.That(client.Options.Endpoints[0].Host).IsEqualTo("spelled");
    }

    [Test]
    public async ValueTask connection_strings_default_is_never_consulted() {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["ConnectionStrings:Default"] = "kurrentdb://never:2113?tls=false"
            })
            .Build());

        services.AddKurrentSurge(_ => { });
        var provider = services.BuildServiceProvider();

        await Assert.That(() => provider.GetRequiredService<KurrentClient>())
            .ThrowsExactly<KurrentClientNotConfiguredException>();
    }

    [Test]
    public async ValueTask configuration_chain_prefers_kurrent_surge_over_kurrentdb() {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["ConnectionStrings:KurrentSurge"] = "kurrentdb://preferred:2113?tls=false",
                ["ConnectionStrings:KurrentDB"]    = "kurrentdb://fallback:2113?tls=false"
            })
            .Build());

        services.AddKurrentSurge(_ => { });

        var provider = services.BuildServiceProvider();
        var client   = provider.GetRequiredService<KurrentClient>();

        await Assert.That(client.Options.Endpoints[0].Host).IsEqualTo("preferred");
    }

    [Test]
    public async ValueTask explicit_connection_beats_the_configuration_chain() {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["ConnectionStrings:KurrentSurge"] = "kurrentdb://from-config:2113?tls=false"
            })
            .Build());

        services.AddKurrentSurge(surge => surge.Connection(TestConnectionString));

        var provider = services.BuildServiceProvider();
        var options  = provider.GetRequiredService<KurrentClientOptions>();

        await Assert.That(options.Endpoints[0].Host).IsEqualTo("localhost");
    }

    #endregion // AddKurrentSurge Scope

    #region ->> Scope Connection <<-

    [Test]
    public async ValueTask connection_registers_client_options_and_client() {
        var services = new ServiceCollection();

        services.AddKurrentSurge(surge => surge.Connection(TestConnectionString));

        var provider = services.BuildServiceProvider();
        var options  = provider.GetRequiredService<KurrentClientOptions>();
        var client   = provider.GetService<KurrentClient>();

        await Assert.That(options.Endpoints[0].Host).IsEqualTo("localhost");
        await Assert.That(options.Endpoints[0].Port).IsEqualTo(2113);
        await Assert.That(client).IsNotNull();
    }

    [Test]
    public async ValueTask connection_from_options_instance_registers_client() {
        var services = new ServiceCollection();

        services.AddKurrentSurge(surge => surge.Connection(KurrentClientOptions.Parse(TestConnectionString)));

        var provider = services.BuildServiceProvider();
        var client   = provider.GetService<KurrentClient>();

        await Assert.That(client).IsNotNull();
    }

    [Test]
    public async ValueTask connection_with_configure_tunes_resolved_options() {
        var connectionName = "tuned-connection";
        var services       = new ServiceCollection();

        services.AddKurrentSurge(surge =>
            surge.Connection(TestConnectionString, options => options.ConnectionName = connectionName));

        var provider = services.BuildServiceProvider();
        var options  = provider.GetRequiredService<KurrentClientOptions>();

        await Assert.That(options.ConnectionName).IsEqualTo(connectionName);
    }

    [Test]
    public async ValueTask connection_overrides_options_already_in_di() {
        var services = new ServiceCollection();
        services.AddSingleton(KurrentClientOptions.Parse("kurrentdb://other-host:2113"));

        services.AddKurrentSurge(surge => surge.Connection(TestConnectionString));

        var provider = services.BuildServiceProvider();
        var options  = provider.GetRequiredService<KurrentClientOptions>();

        await Assert.That(options.Endpoints[0].Host).IsEqualTo("localhost");
        await Assert.That(services.Count(d => d.ServiceType == typeof(KurrentClientOptions))).IsEqualTo(1);
    }

    [Test]
    public async ValueTask last_connection_call_wins() {
        var services = new ServiceCollection();

        services.AddKurrentSurge(surge => surge
            .Connection("kurrentdb://other-host:2113")
            .Connection(TestConnectionString));

        var provider = services.BuildServiceProvider();
        var options  = provider.GetRequiredService<KurrentClientOptions>();

        await Assert.That(options.Endpoints[0].Host).IsEqualTo("localhost");
        await Assert.That(services.Count(d => d.ServiceType == typeof(KurrentClientOptions))).IsEqualTo(1);
    }

    #endregion // Scope Connection

    #region ->> Host Application Builder <<-

    [Test]
    public async ValueTask host_builder_resolves_connection_string_by_name() {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:kurrentdb"] = TestConnectionString
        });

        builder.AddKurrentSurge("kurrentdb");

        var provider = builder.Services.BuildServiceProvider();
        var options  = provider.GetRequiredService<KurrentClientOptions>();

        await Assert.That(options.Endpoints[0].Host).IsEqualTo("localhost");
    }

    [Test]
    public async ValueTask host_builder_rejects_unknown_connection_name() {
        var builder = Host.CreateApplicationBuilder();

        await Assert.That(() => builder.AddKurrentSurge("missing"))
            .Throws<KurrentClientNotConfiguredException>();
    }

    #endregion // Host Application Builder

    #region ->> Connection String Application <<-

    [Test]
    public async ValueTask connection_applies_parsed_settings_in_place() {
        var options = new KurrentClientOptions();

        options.Connection("kurrentdb://some-host:1113?tls=false");

        await Assert.That(options.Endpoints[0].Host).IsEqualTo("some-host");
        await Assert.That(options.Endpoints[0].Port).IsEqualTo(1113);
        await Assert.That(options.Security.Transport.IsEnabled).IsFalse();
        await Assert.That(options.OriginalConnectionString).IsEqualTo("kurrentdb://some-host:1113?tls=false");
    }

    [Test]
    public async ValueTask connection_preserves_pre_set_connection_name() {
        var connectionName = "my-explicit-name";
        var options        = new KurrentClientOptions { ConnectionName = connectionName };

        options.Connection(TestConnectionString);

        await Assert.That(options.ConnectionName).IsEqualTo(connectionName);
    }

    [Test]
    public async ValueTask connection_adopts_explicit_name_from_connection_string() {
        var options = new KurrentClientOptions();

        options.Connection($"{TestConnectionString}?connectionName=from-string");

        await Assert.That(options.ConnectionName).IsEqualTo("from-string");
    }

    #endregion // Connection String Application
}
