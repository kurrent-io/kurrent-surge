namespace Kurrent.Surge.Tests.Legacy;

[Category("Legacy")]
public class NewConnectionStringTests {
    #region ->> Basic Parsing Tests <<-

    [Test]
    [DirectSchemes]
    public async ValueTask parses_direct_scheme_with_credentials_and_options(string scheme) {
        var connectionString = $"{scheme}://admin:changeit@localhost:2113?tls=false";

        var result = KurrentConnectionString.Parse(connectionString);

        await Assert.That(result.Scheme).IsEqualTo(scheme);
        await Assert.That(result.UserInfo).IsNotNull();
        await Assert.That(result.UserInfo!.Value.User).IsEqualTo("admin");
        await Assert.That(result.Hosts.Length).IsEqualTo(1);
        await Assert.That(result.Hosts[0].Port).IsEqualTo(2113);
        await Assert.That(result.Options["tls"]).IsEqualTo("false");
    }

    [Test]
    [DiscoverySchemes]
    public async ValueTask parses_discovery_scheme_with_multiple_hosts(string scheme) {
        var connectionString = $"{scheme}://admin:changeit@node1:2113,node2:2114?nodePreference=leader";

        var result = KurrentConnectionString.Parse(connectionString);

        await Assert.That(result.Scheme).IsEqualTo(scheme);
        await Assert.That(result.UserInfo).IsNotNull();
        await Assert.That(result.UserInfo!.Value.User).IsEqualTo("admin");
        await Assert.That(result.Hosts.Length).IsEqualTo(2);
        await Assert.That(result.Hosts[0].Host).IsEqualTo("node1");
        await Assert.That(result.Hosts[1].Host).IsEqualTo("node2");
        await Assert.That(result.Options["nodePreference"]).IsEqualTo("leader");
    }

    [Test]
    public async ValueTask parses_single_host_with_default_port() {
        var connectionString = "kurrentdb://localhost";

        var result = KurrentConnectionString.Parse(connectionString);

        await Assert.That(result.Hosts.Length).IsEqualTo(1);
        await Assert.That(result.Hosts[0].Host).IsEqualTo("localhost");
        await Assert.That(result.Hosts[0].Port).IsEqualTo(2113);
    }

    [Test]
    public async ValueTask parses_single_host_with_custom_port() {
        var connectionString = "kurrentdb://localhost:1234";

        var result = KurrentConnectionString.Parse(connectionString);

        await Assert.That(result.Hosts.Length).IsEqualTo(1);
        await Assert.That(result.Hosts[0].Host).IsEqualTo("localhost");
        await Assert.That(result.Hosts[0].Port).IsEqualTo(1234);
    }

    [Test]
    public async ValueTask parses_multiple_hosts_for_discovery() {
        var connectionString = "kurrentdb+discover://node1:2113,node2:2114,node3:2115";

        var result = KurrentConnectionString.Parse(connectionString);

        await Assert.That(result.Hosts.Length).IsEqualTo(3);
        await Assert.That(result.Hosts[0].Host).IsEqualTo("node1");
        await Assert.That(result.Hosts[0].Port).IsEqualTo(2113);
        await Assert.That(result.Hosts[1].Host).IsEqualTo("node2");
        await Assert.That(result.Hosts[1].Port).IsEqualTo(2114);
        await Assert.That(result.Hosts[2].Host).IsEqualTo("node3");
        await Assert.That(result.Hosts[2].Port).IsEqualTo(2115);
    }

    [Test]
    public async ValueTask parses_user_credentials() {
        var connectionString = "kurrentdb://admin:changeit@localhost:2113";

        var result = KurrentConnectionString.Parse(connectionString);

        await Assert.That(result.UserInfo).IsNotNull();
        await Assert.That(result.UserInfo!.Value.User).IsEqualTo("admin");
        await Assert.That(result.UserInfo!.Value.Password).IsEqualTo("changeit");
    }

    [Test]
    public async ValueTask parses_url_encoded_credentials() {
        var connectionString = "kurrentdb://admin%40domain:pass%40word@localhost:2113";

        var result = KurrentConnectionString.Parse(connectionString);

        await Assert.That(result.UserInfo).IsNotNull();
        await Assert.That(result.UserInfo!.Value.User).IsEqualTo("admin@domain");
        await Assert.That(result.UserInfo!.Value.Password).IsEqualTo("pass@word");
    }

    [Test]
    public async ValueTask parses_options() {
        var connectionString = "kurrentdb://localhost:2113?tls=false&maxDiscoverAttempts=5";

        var result = KurrentConnectionString.Parse(connectionString);

        await Assert.That(result.Options.Count).IsEqualTo(2);
        await Assert.That(result.Options["tls"]).IsEqualTo("false");
        await Assert.That(result.Options["maxDiscoverAttempts"]).IsEqualTo("5");
    }

    [Test]
    public async ValueTask parses_case_insensitive_options() {
        var connectionString = "kurrentdb://localhost:2113?TLS=false&MaxDiscoverAttempts=5";

        var result = KurrentConnectionString.Parse(connectionString);

        await Assert.That(result.Options.Count).IsEqualTo(2);
        await Assert.That(result.Options["TLS"]).IsEqualTo("false");
        await Assert.That(result.Options["MaxDiscoverAttempts"]).IsEqualTo("5");
    }

    [Test]
    public async ValueTask parses_url_encoded_option_values() {
        var connectionString = "kurrentdb://localhost:2113?connectionName=My%20Connection";

        var result = KurrentConnectionString.Parse(connectionString);

        await Assert.That(result.Options["connectionName"]).IsEqualTo("My Connection");
    }

    #endregion

    #region ->> Error Condition Tests <<-

    [Test]
    public void throws_on_null_connection_string() {
        Assert.Throws<ArgumentNullException>(() => KurrentConnectionString.Parse(null!));
    }

    [Test]
    public void throws_on_empty_connection_string() {
        Assert.Throws<ArgumentException>(() => KurrentConnectionString.Parse(""));
    }

    [Test]
    public void throws_on_whitespace_connection_string() {
        Assert.Throws<ArgumentException>(() => KurrentConnectionString.Parse("   "));
    }

    [Test]
    public void throws_on_missing_scheme() {
        Assert.Throws<InvalidConnectionStringException>(() => KurrentConnectionString.Parse("localhost:2113"));
    }

    [Test]
    [InvalidSchemes]
    public void throws_on_invalid_scheme(string connectionString) {
        Assert.Throws<InvalidConnectionStringException>(() => KurrentConnectionString.Parse(connectionString));
    }

    [Test]
    [InvalidUserCredentials]
    public void throws_on_invalid_user_credentials(string connectionString) {
        Assert.Throws<InvalidConnectionStringException>(() => KurrentConnectionString.Parse(connectionString));
    }

    [Test]
    [InvalidHosts]
    public void throws_on_invalid_host(string connectionString) {
        Assert.Throws<InvalidConnectionStringException>(() => KurrentConnectionString.Parse(connectionString));
    }

    [Test]
    [InvalidPortRanges]
    public void throws_on_invalid_port_range(string connectionString) {
        Assert.Throws<InvalidConnectionStringException>(() => KurrentConnectionString.Parse(connectionString));
    }

    [Test]
    [Arguments("kurrentdb://localhost/path")]
    [Arguments("kurrentdb://localhost/test?tls=false")]
    public void throws_on_non_empty_path(string connectionString) {
        Assert.Throws<InvalidConnectionStringException>(() => KurrentConnectionString.Parse(connectionString));
    }

    [Test]
    [Arguments("kurrentdb://localhost?key")]
    [Arguments("kurrentdb://localhost?key=value=extra")]
    public void throws_on_invalid_key_value_pair(string connectionString) {
        Assert.Throws<InvalidConnectionStringException>(() => KurrentConnectionString.Parse(connectionString));
    }

    [Test]
    public void throws_on_duplicate_keys() {
        var connectionString = "kurrentdb://localhost?tls=true&TLS=false";

        Assert.Throws<InvalidConnectionStringException>(() => KurrentConnectionString.Parse(connectionString));
    }

    #endregion

    #region ->> Discovery vs Direct Connection Tests <<-

    [Test]
    public async ValueTask allows_single_host_for_direct_scheme() {
        var connectionString = "kurrentdb://localhost:2113";

        var result = KurrentConnectionString.Parse(connectionString);

        await Assert.That(result.Hosts.Length).IsEqualTo(1);
    }

    [Test]
    [DirectSchemes]
    public void throws_on_multiple_hosts_for_direct_scheme(string scheme) {
        Assert.Throws<InvalidConnectionStringException>(
            () => KurrentConnectionString.Parse($"{scheme}://node1:2113,node2:2113")
        );
    }

    [Test]
    public async ValueTask allows_multiple_hosts_for_discovery_scheme() {
        var connectionString = "kurrentdb+discover://node1:2113,node2:2113,node3:2113";

        var result = KurrentConnectionString.Parse(connectionString);

        await Assert.That(result.Hosts.Length).IsEqualTo(3);
    }

    #endregion

    #region ->> Comprehensive Parameter Parsing Tests <<-

    [Test]
    [TlsBooleanValues]
    public async ValueTask parses_tls_parameter(string value) {
        var connectionString = $"kurrentdb://localhost?tls={value}";

        var result = KurrentConnectionString.Parse(connectionString);

        await Assert.That(result.Options["tls"]).IsEqualTo(value);
    }

    [Test]
    [KnownParameters]
    public async ValueTask parses_known_parameters(string key, string value) {
        var connectionString = $"kurrentdb://localhost?{key}={value}";

        var result = KurrentConnectionString.Parse(connectionString);

        await Assert.That(result.Options[key]).IsEqualTo(value);
    }

    [Test]
    public async ValueTask parses_multiple_parameters() {
        var connectionString = "kurrentdb://localhost?tls=false&connectionName=Test&maxDiscoverAttempts=5";

        var result = KurrentConnectionString.Parse(connectionString);

        await Assert.That(result.Options.Count).IsEqualTo(3);
        await Assert.That(result.Options["tls"]).IsEqualTo("false");
        await Assert.That(result.Options["connectionName"]).IsEqualTo("Test");
        await Assert.That(result.Options["maxDiscoverAttempts"]).IsEqualTo("5");
    }

    [Test]
    [NodePreferenceValues]
    public async ValueTask parses_node_preference_values(string value) {
        var connectionString = $"kurrentdb://localhost?nodePreference={value}";

        var result = KurrentConnectionString.Parse(connectionString);

        await Assert.That(result.Options["nodePreference"]).IsEqualTo(value);
    }

    [Test]
    public async ValueTask options_lookup_is_case_insensitive() {
        var connectionString = "kurrentdb://localhost?TLS=false";

        var result = KurrentConnectionString.Parse(connectionString);

        await Assert.That(result.Options["tls"]).IsEqualTo("false");
        await Assert.That(result.Options["Tls"]).IsEqualTo("false");
        await Assert.That(result.Options["TLS"]).IsEqualTo("false");
    }

    #endregion

    #region ->> Edge Case Tests <<-

    [Test]
    public async ValueTask handles_empty_path() {
        var connectionString = "kurrentdb://localhost/";

        var result = KurrentConnectionString.Parse(connectionString);

        await Assert.That(result.Hosts.Length).IsEqualTo(1);
        await Assert.That(result.Hosts[0].Host).IsEqualTo("localhost");
    }

    [Test]
    public async ValueTask handles_no_options() {
        var connectionString = "kurrentdb://localhost";

        var result = KurrentConnectionString.Parse(connectionString);

        await Assert.That(result.Options.Count).IsEqualTo(0);
    }

    [Test]
    public void throws_on_empty_query_parameter() {
        var connectionString = "kurrentdb://localhost?";

        Assert.Throws<InvalidConnectionStringException>(() => KurrentConnectionString.Parse(connectionString));
    }

    [Test]
    public async ValueTask handles_host_with_underscores() {
        var connectionString = "kurrentdb://my_host:2113";

        var result = KurrentConnectionString.Parse(connectionString);

        await Assert.That(result.Hosts[0].Host).IsEqualTo("my_host");
    }

    [Test]
    public async ValueTask handles_host_with_hyphens() {
        var connectionString = "kurrentdb://my-host:2113";

        var result = KurrentConnectionString.Parse(connectionString);

        await Assert.That(result.Hosts[0].Host).IsEqualTo("my-host");
    }

    [Test]
    public async ValueTask handles_numeric_host() {
        var connectionString = "kurrentdb://192.168.1.100:2113";

        var result = KurrentConnectionString.Parse(connectionString);

        await Assert.That(result.Hosts[0].Host).IsEqualTo("192.168.1.100");
    }

    [Test]
    public async ValueTask preserves_original_connection_string() {
        var original = "kurrentdb://admin:changeit@localhost:2113?tls=false";

        var result = KurrentConnectionString.Parse(original);

        await Assert.That(result.ConnectionString).IsEqualTo(original);
    }

    [Test]
    public void throws_on_mixed_case_schemes() {
        var connectionString = "KurrentDB://localhost";

        Assert.Throws<InvalidConnectionStringException>(() => KurrentConnectionString.Parse(connectionString));
    }

    #endregion

    #region ->> Real-world Example Tests <<-

    [Test]
    public async ValueTask parses_local_insecure_connection() {
        var connectionString = "kurrentdb://localhost:2113?tls=false&tlsVerifyCert=false";

        var result = KurrentConnectionString.Parse(connectionString);

        await Assert.That(result.Scheme).IsEqualTo("kurrentdb");
        await Assert.That(result.Hosts.Length).IsEqualTo(1);
        await Assert.That(result.Hosts[0].Host).IsEqualTo("localhost");
        await Assert.That(result.Hosts[0].Port).IsEqualTo(2113);
        await Assert.That(result.Options["tls"]).IsEqualTo("false");
        await Assert.That(result.Options["tlsVerifyCert"]).IsEqualTo("false");
    }

    [Test]
    public async ValueTask parses_production_cluster_connection() {
        var connectionString =
            "kurrentdb+discover://admin:changeit@node1.cluster.local:2113,node2.cluster.local:2113,node3.cluster.local:2113?nodePreference=leader";

        var result = KurrentConnectionString.Parse(connectionString);

        await Assert.That(result.Scheme).IsEqualTo("kurrentdb+discover");
        await Assert.That(result.UserInfo).IsNotNull();
        await Assert.That(result.UserInfo!.Value.User).IsEqualTo("admin");
        await Assert.That(result.UserInfo!.Value.Password).IsEqualTo("changeit");
        await Assert.That(result.Hosts.Length).IsEqualTo(3);
        await Assert.That(result.Hosts[0].Host).IsEqualTo("node1.cluster.local");
        await Assert.That(result.Hosts[1].Host).IsEqualTo("node2.cluster.local");
        await Assert.That(result.Hosts[2].Host).IsEqualTo("node3.cluster.local");
        await Assert.That(result.Options["nodePreference"]).IsEqualTo("leader");
    }

    [Test]
    public async ValueTask parses_secure_single_node_connection() {
        var connectionString = "kurrentdb://admin:changeit@kurrentdb.example.com:2113?tls=true&tlsVerifyCert=true";

        var result = KurrentConnectionString.Parse(connectionString);

        await Assert.That(result.Scheme).IsEqualTo("kurrentdb");
        await Assert.That(result.UserInfo).IsNotNull();
        await Assert.That(result.UserInfo!.Value.User).IsEqualTo("admin");
        await Assert.That(result.UserInfo!.Value.Password).IsEqualTo("changeit");
        await Assert.That(result.Hosts.Length).IsEqualTo(1);
        await Assert.That(result.Hosts[0].Host).IsEqualTo("kurrentdb.example.com");
        await Assert.That(result.Options["tls"]).IsEqualTo("true");
        await Assert.That(result.Options["tlsVerifyCert"]).IsEqualTo("true");
    }

    [Test]
    public async ValueTask parses_connection_with_custom_certificates() {
        var connectionString =
            "kurrentdb://admin:changeit@localhost:2113?tls=true&tlsVerifyCert=true&tlsCaFile=/certs/ca.crt&userCertFile=/certs/client.crt&userKeyFile=/certs/client.key";

        var result = KurrentConnectionString.Parse(connectionString);

        await Assert.That(result.Options["tlsCaFile"]).IsEqualTo("/certs/ca.crt");
        await Assert.That(result.Options["userCertFile"]).IsEqualTo("/certs/client.crt");
        await Assert.That(result.Options["userKeyFile"]).IsEqualTo("/certs/client.key");
    }

    [Test]
    public async ValueTask parses_connection_with_tuned_timeouts() {
        var connectionString =
            "kurrentdb://localhost:2113?discoveryInterval=500&gossipTimeout=3000&keepAliveInterval=15000&keepAliveTimeout=30000&defaultDeadline=60000";

        var result = KurrentConnectionString.Parse(connectionString);

        await Assert.That(result.Options["discoveryInterval"]).IsEqualTo("500");
        await Assert.That(result.Options["gossipTimeout"]).IsEqualTo("3000");
        await Assert.That(result.Options["keepAliveInterval"]).IsEqualTo("15000");
        await Assert.That(result.Options["keepAliveTimeout"]).IsEqualTo("30000");
        await Assert.That(result.Options["defaultDeadline"]).IsEqualTo("60000");
    }

    #endregion

    #region ->> Test Case Generators <<-

    public class DirectSchemes : TestCaseGenerator<string> {
        protected override IEnumerable<string> Data() =>
            KurrentConnectionString.AllSchemes.Except(KurrentConnectionString.DiscoverySchemes);
    }

    public class DiscoverySchemes : TestCaseGenerator<string> {
        protected override IEnumerable<string> Data() =>
            KurrentConnectionString.DiscoverySchemes;
    }

    public class InvalidSchemes : TestCaseGenerator<string> {
        protected override IEnumerable<string> Data() => [
            "invalid://localhost",
            "kurrentdbwrong://localhost",
            "http://localhost"
        ];
    }

    public class InvalidUserCredentials : TestCaseGenerator<string> {
        protected override IEnumerable<string> Data() => [
            "kurrentdb://user@localhost",
            "kurrentdb://user:pass:extra@localhost",
            "kurrentdb://:password@localhost"
        ];
    }

    public class InvalidHosts : TestCaseGenerator<string> {
        protected override IEnumerable<string> Data() => [
            "kurrentdb://localhost:abc",
            "kurrentdb://localhost:",
            "kurrentdb://localhost:123:456",
            "kurrentdb://"
        ];
    }

    public class InvalidPortRanges : TestCaseGenerator<string> {
        protected override IEnumerable<string> Data() => [
            "kurrentdb://localhost:-1",
            "kurrentdb://localhost:0",
            "kurrentdb://localhost:65536",
            "kurrentdb://localhost:99999"
        ];
    }

    public class TlsBooleanValues : TestCaseGenerator<string> {
        protected override IEnumerable<string> Data() => ["true", "false", "True", "False"];
    }

    public class KnownParameters : TestCaseGenerator<string, string> {
        protected override IEnumerable<(string, string)> Data() => [
            ("connectionName", "MyConnection"),
            ("maxDiscoverAttempts", "10"),
            ("discoveryInterval", "100"),
            ("gossipTimeout", "5000"),
            ("nodePreference", "leader"),
            ("tlsVerifyCert", "false"),
            ("tlsCaFile", "/path/to/ca.crt"),
            ("defaultDeadline", "30000"),
            ("throwOnAppendFailure", "true"),
            ("keepAliveInterval", "10000"),
            ("keepAliveTimeout", "20000"),
            ("userCertFile", "/path/to/user.crt"),
            ("userKeyFile", "/path/to/user.key")
        ];
    }

    public class NodePreferenceValues : TestCaseGenerator<string> {
        protected override IEnumerable<string> Data() => ["leader", "follower", "random", "readOnlyReplica"];
    }

    #endregion
}
