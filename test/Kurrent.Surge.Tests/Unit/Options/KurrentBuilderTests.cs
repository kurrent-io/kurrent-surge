using Microsoft.Extensions.DependencyInjection;

namespace Kurrent.Surge.Tests.Options;

/// <summary>
/// Unit tests for the <c>AddKurrent</c> entry point and the scope's default client registration.
/// </summary>
public class KurrentBuilderTests {
    const string TestConnectionString = "kurrentdb://localhost:2113";

    #region ->> AddKurrent Scope <<-

    [Test]
    public async ValueTask add_kurrent_exposes_underlying_service_collection() {
        var services = new ServiceCollection();

        IServiceCollection? captured = null;
        services.AddKurrent(kurrent => captured = kurrent.Services);

        await Assert.That(captured).IsSameReferenceAs(services);
    }

    [Test]
    public async ValueTask add_kurrent_without_client_registers_nothing() {
        var services = new ServiceCollection();

        services.AddKurrent(_ => { });

        await Assert.That(services.Count).IsEqualTo(0);
    }

    #endregion // AddKurrent Scope

    #region ->> Scope Connection <<-

    [Test]
    public async ValueTask connection_registers_client_options_and_client() {
        var services = new ServiceCollection();

        services.AddKurrent(kurrent => kurrent.Connection(TestConnectionString));

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

        services.AddKurrent(kurrent => kurrent.Connection(KurrentClientOptions.Parse(TestConnectionString)));

        var provider = services.BuildServiceProvider();
        var client   = provider.GetService<KurrentClient>();

        await Assert.That(client).IsNotNull();
    }

    [Test]
    public async ValueTask connection_with_configure_tunes_resolved_options() {
        var connectionName = "tuned-connection";
        var services       = new ServiceCollection();

        services.AddKurrent(kurrent =>
            kurrent.Connection(TestConnectionString, options => options.ConnectionName = connectionName));

        var provider = services.BuildServiceProvider();
        var options  = provider.GetRequiredService<KurrentClientOptions>();

        await Assert.That(options.ConnectionName).IsEqualTo(connectionName);
    }

    [Test]
    public async ValueTask connection_overrides_registrations_already_in_di() {
        var services = new ServiceCollection();
        services.AddSingleton(KurrentClientOptions.Parse("kurrentdb://other-host:2113"));
        services.AddKurrentClient("kurrentdb://other-host:2113");

        services.AddKurrent(kurrent => kurrent.Connection(TestConnectionString));

        var provider = services.BuildServiceProvider();
        var options  = provider.GetRequiredService<KurrentClientOptions>();

        await Assert.That(options.Endpoints[0].Host).IsEqualTo("localhost");
        await Assert.That(services.Count(d => d.ServiceType == typeof(KurrentClient))).IsEqualTo(1);
    }

    [Test]
    public async ValueTask last_connection_call_wins() {
        var services = new ServiceCollection();

        services.AddKurrent(kurrent => kurrent
            .Connection("kurrentdb://other-host:2113")
            .Connection(TestConnectionString));

        var provider = services.BuildServiceProvider();
        var options  = provider.GetRequiredService<KurrentClientOptions>();

        await Assert.That(options.Endpoints[0].Host).IsEqualTo("localhost");
        await Assert.That(services.Count(d => d.ServiceType == typeof(KurrentClientOptions))).IsEqualTo(1);
    }

    #endregion // Scope Connection

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
