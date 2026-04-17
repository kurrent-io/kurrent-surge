using System.Net;

namespace Kurrent.Client.Tests.Options;

[Category("Options")]
public class ConnectionBuilderTests {
    #region ->> Default Values <<-

    [Test]
    public async ValueTask default_builder_produces_valid_options() {
        // Arrange & Act
        var options = new KurrentClientOptions.ConnectionBuilder().Build();

        // Assert
        await Assert.That(options.ConnectionScheme).IsEqualTo(KurrentConnectionScheme.Direct);
        await Assert.That(options.Endpoints).HasSingleItem();
        await Assert.That(options.Security).IsDeepEquivalentTo(KurrentClientSecurityOptions.Default);
    }

    #endregion

    #region ->> WithConnectionName <<-

    [Test]
    public async ValueTask with_connection_name_sets_name() {
        // Arrange
        const string name = "my-connection";

        // Act
        var options = new KurrentClientOptions.ConnectionBuilder()
            .WithConnectionName(name)
            .Build();

        // Assert
        await Assert.That(options.ConnectionName).IsEqualTo(name);
    }

    [Test]
    public void with_connection_name_throws_when_empty() {
        // Arrange
        var builder = new KurrentClientOptions.ConnectionBuilder();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => builder.WithConnectionName(""));
    }

    #endregion

    #region ->> WithDirectConnection <<-

    [Test]
    public async ValueTask with_direct_connection_sets_single_endpoint_and_direct_scheme() {
        // Arrange
        const string host = "myserver.example.com";
        const int    port = 2113;

        // Act
        var options = new KurrentClientOptions.ConnectionBuilder()
            .WithDirectConnection(host, port, useTls: false)
            .Build();

        // Assert
        await Assert.That(options.ConnectionScheme).IsEqualTo(KurrentConnectionScheme.Direct);
        await Assert.That(options.Endpoints).HasSingleItem();
        await Assert.That(options.Endpoints[0].Host).IsEqualTo(host);
        await Assert.That(options.Endpoints[0].Port).IsEqualTo(port);
    }

    [Test]
    public async ValueTask with_direct_connection_disables_tls_when_requested() {
        // Arrange & Act
        var options = new KurrentClientOptions.ConnectionBuilder()
            .WithDirectConnection("localhost", 2113, useTls: false)
            .Build();

        // Assert
        await Assert.That(options.Security.Transport.IsEnabled).IsFalse();
    }

    [Test]
    public async ValueTask with_direct_connection_uri_uses_scheme_for_tls() {
        // Arrange
        var uri = new Uri("https://secure-host:2113");

        // Act
        var options = new KurrentClientOptions.ConnectionBuilder()
            .WithDirectConnection(uri)
            .Build();

        // Assert
        await Assert.That(options.ConnectionScheme).IsEqualTo(KurrentConnectionScheme.Direct);
        await Assert.That(options.Endpoints[0].Host).IsEqualTo("secure-host");
        await Assert.That(options.Security.Transport.IsEnabled).IsTrue();
    }

    #endregion

    #region ->> WithGossipEndpoint <<-

    [Test]
    public async ValueTask with_gossip_endpoint_sets_discover_scheme() {
        // Arrange & Act
        var options = new KurrentClientOptions.ConnectionBuilder()
            .WithGossipEndpoint("node1", 2113, useTls: false)
            .Build();

        // Assert
        await Assert.That(options.ConnectionScheme).IsEqualTo(KurrentConnectionScheme.Discover);
    }

    [Test]
    public async ValueTask with_gossip_endpoint_appends_to_existing_endpoints() {
        // Arrange & Act
        var options = new KurrentClientOptions.ConnectionBuilder()
            .WithGossipEndpoint(new DnsEndPoint("node1", 2113))
            .WithGossipEndpoint(new DnsEndPoint("node2", 2113))
            .Build();

        // Assert — default localhost + 2 gossip nodes
        await Assert.That(options.Endpoints.Length).IsGreaterThanOrEqualTo(2);
        await Assert.That(options.Endpoints.Any(ep => ep.Host == "node1")).IsTrue();
        await Assert.That(options.Endpoints.Any(ep => ep.Host == "node2")).IsTrue();
    }

    #endregion

    #region ->> WithInsecureTransport <<-

    [Test]
    public async ValueTask with_insecure_transport_sets_transport_to_insecure() {
        // Arrange & Act
        var options = new KurrentClientOptions.ConnectionBuilder()
            .WithDirectConnection("localhost", 2113, useTls: false)
            .WithInsecureTransport()
            .Build();

        // Assert
        await Assert.That(options.Security.Transport).IsEqualTo(TransportSecurity.Insecure);
    }

    #endregion

    #region ->> WithBasicAuthentication <<-

    [Test]
    public async ValueTask with_basic_authentication_sets_credentials() {
        // Arrange
        const string username = "admin";
        const string password = "changeit";

        // Act
        var options = new KurrentClientOptions.ConnectionBuilder()
            .WithBasicAuthentication(username, password)
            .Build();

        // Assert
        await Assert.That(options.Security.Authentication.IsBasicCredentials).IsTrue();
        var credentials = options.Security.Authentication.AsBasicCredentials;
        await Assert.That(credentials.Username).IsEqualTo(username);
        await Assert.That(credentials.Password).IsEqualTo(password);
    }

    [Test]
    public async ValueTask with_basic_authentication_credentials_overload_sets_credentials() {
        // Arrange
        var credentials = ClientCredentials.Basic("user", "pass");

        // Act
        var options = new KurrentClientOptions.ConnectionBuilder()
            .WithBasicAuthentication(credentials)
            .Build();

        // Assert
        await Assert.That(options.Security.Authentication.IsBasicCredentials).IsTrue();
    }

    #endregion

    #region ->> ConfigureSecurity <<-

    [Test]
    public async ValueTask configure_security_with_options_replaces_security() {
        // Arrange
        var securityOptions = KurrentClientSecurityOptions.Insecure;

        // Act
        var options = new KurrentClientOptions.ConnectionBuilder()
            .WithDirectConnection("localhost", 2113, useTls: false)
            .ConfigureSecurity(securityOptions)
            .Build();

        // Assert
        await Assert.That(options.Security).IsDeepEquivalentTo(securityOptions);
    }

    [Test]
    public async ValueTask configure_security_with_func_modifies_existing_security() {
        // Arrange & Act
        var options = new KurrentClientOptions.ConnectionBuilder()
            .ConfigureSecurity(sec => sec with { Authentication = ClientCredentials.Basic("admin", "pass") })
            .Build();

        // Assert
        await Assert.That(options.Security.Authentication.IsBasicCredentials).IsTrue();
        await Assert.That(options.Security.Transport).IsDeepEquivalentTo(TransportSecurity.Standard);
    }

    #endregion

    #region ->> ConfigureGossip <<-

    [Test]
    public async ValueTask configure_gossip_with_options_replaces_gossip() {
        // Arrange
        var gossipOptions = new KurrentClientGossipOptions { MaxDiscoverAttempts = 5 };

        // Act
        var options = new KurrentClientOptions.ConnectionBuilder()
            .WithGossipEndpoint("node1", 2113, useTls: false)
            .ConfigureGossip(gossipOptions)
            .Build();

        // Assert
        await Assert.That(options.Gossip.MaxDiscoverAttempts).IsEqualTo(5);
    }

    [Test]
    public async ValueTask configure_gossip_with_func_preserves_other_gossip_properties() {
        // Arrange
        var original = KurrentClientGossipOptions.Default;

        // Act
        var options = new KurrentClientOptions.ConnectionBuilder()
            .WithGossipEndpoint("node1", 2113, useTls: false)
            .ConfigureGossip(g => g with { MaxDiscoverAttempts = 3 })
            .Build();

        // Assert
        await Assert.That(options.Gossip.MaxDiscoverAttempts).IsEqualTo(3);
        await Assert.That(options.Gossip.DiscoveryInterval).IsEqualTo(original.DiscoveryInterval);
        await Assert.That(options.Gossip.Timeout).IsEqualTo(original.Timeout);
    }

    #endregion

    #region ->> Method Chaining <<-

    [Test]
    public async ValueTask method_chaining_applies_all_configurations() {
        // Arrange & Act
        var options = new KurrentClientOptions.ConnectionBuilder()
            .WithConnectionName("chained-conn")
            .WithDirectConnection("myhost", 2113, useTls: false)
            .WithBasicAuthentication("user", "secret")
            .Build();

        // Assert
        await Assert.That(options.ConnectionName).IsEqualTo("chained-conn");
        await Assert.That(options.Endpoints[0].Host).IsEqualTo("myhost");
        await Assert.That(options.Security.Authentication.IsBasicCredentials).IsTrue();
    }

    #endregion
}
