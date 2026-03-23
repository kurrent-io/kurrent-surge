using System.Net;
using KurrentDB.Client;

namespace Kurrent.Client.Tests.Options;

public class KurrentClientOptionsTests {
    [Test]
    public async Task default_options_should_be_valid() {
        var options = new KurrentClientOptions();

        var validationResult = options.ValidateOptions();

        await Assert.That(validationResult.IsSuccess).IsTrue();
    }

    [Test]
    public async Task creates_default_options_with_single_endpoint() {
        var options = new KurrentClientOptions();

        await Assert.That(options.ConnectionScheme).IsEqualTo(KurrentConnectionScheme.Direct);
        await Assert.That(options.Endpoints).HasSingleItem();
        await Assert.That(options.Gossip).IsDeepEquivalentTo(KurrentClientGossipOptions.Default);
        await Assert.That(options.Security).IsDeepEquivalentTo(KurrentClientSecurityOptions.Default);
        await Assert.That(options.Resilience).IsDeepEquivalentTo(KurrentClientResilienceOptions.NoResilience);
        await Assert.That(options.Schema).IsDeepEquivalentTo(KurrentClientSchemaOptions.FullValidation);
        await Assert.That(options.Interceptors).IsEmpty();
    }

    [Test]
    public async Task creates_options_from_connection_string() {
        var connectionString = "kurrentdb://admin:changeit@localhost:2113?tls=false";
        var options          = KurrentClientOptions.Parse(connectionString);

        await Assert.That(options.HasOriginalConnectionString).IsTrue();
        await Assert.That(options.OriginalConnectionString).IsEqualTo(connectionString);
        await Assert.That(options.ConnectionScheme).IsEqualTo(KurrentConnectionScheme.Direct);
        await Assert.That(options.Endpoints.Length).IsEqualTo(1);
        await Assert.That(options.Endpoints[0].Host).IsEqualTo("localhost");
        await Assert.That(options.Endpoints[0].Port).IsEqualTo(2113);
        await Assert.That(options.Security.Transport.IsEnabled).IsFalse();
        await Assert.That(options.Security.Authentication.IsBasicCredentials).IsTrue();

        var credentials = options.Security.Authentication.AsBasicCredentials;
        await Assert.That(credentials.Username).IsEqualTo("admin");
        await Assert.That(credentials.Password).IsEqualTo("changeit");
    }

    [Test]
    public async Task creates_options_from_discovery_connection_string() {
        var connectionString = "kurrentdb+discover://admin:changeit@node1:2113,node2:2113?tls=true";
        var options          = KurrentClientOptions.Parse(connectionString);

        await Assert.That(options.ConnectionScheme).IsEqualTo(KurrentConnectionScheme.Discover);
        await Assert.That(options.Endpoints.Length).IsEqualTo(2);
        await Assert.That(options.Endpoints[0].Host).IsEqualTo("node1");
        await Assert.That(options.Endpoints[0].Port).IsEqualTo(2113);
        await Assert.That(options.Endpoints[1].Host).IsEqualTo("node2");
        await Assert.That(options.Endpoints[1].Port).IsEqualTo(2113);
        await Assert.That(options.Security.Transport.IsEnabled).IsTrue();
    }

    [Test]
    public async Task generates_connection_string_that_matches_original() {
        var originalString = "kurrentdb://admin:changeit@localhost:2113?tls=false";
        var options        = KurrentClientOptions.Parse(originalString);

        var generatedString = options.GenerateConnectionString();

        await Assert.That(generatedString).Contains("kurrentdb://");
        await Assert.That(generatedString).Contains("admin:changeit@");
        await Assert.That(generatedString).Contains("localhost:2113");
        await Assert.That(generatedString).Contains("tls=false");
    }

    [Test]
    public async Task handles_special_characters_in_credentials() {
        var connectionString = "kurrentdb://user%40domain:p%40ssw%3Ard@localhost:2113";
        var options          = KurrentClientOptions.Parse(connectionString);

        var credentials = options.Security.Authentication.AsBasicCredentials;
        await Assert.That(credentials.Username).IsEqualTo("user@domain");
        await Assert.That(credentials.Password).IsEqualTo("p@ssw:rd");

        var generatedString = options.GenerateConnectionString();
        await Assert.That(generatedString).Contains("user%40domain:p%40ssw%3Ard@");
    }

    [Test]
    public async Task generates_proper_http_uri_based_on_transport_security() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Direct,
            Endpoints        = [new("localhost", 2113)],
            Security         = KurrentClientSecurityOptions.Default
        };

        await Assert.That(options.HttpUriScheme).IsEqualTo(Uri.UriSchemeHttps);

        options = options with { Security = KurrentClientSecurityOptions.Insecure };
        await Assert.That(options.HttpUriScheme).IsEqualTo(Uri.UriSchemeHttp);
    }

    [Test]
    public async Task generates_address_correctly_for_direct_connection() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Direct,
            Endpoints        = [new("localhost", 2113)],
            Security         = KurrentClientSecurityOptions.Default
        };

        await Assert.That(options.Address).IsNotNull();
        await Assert.That(options.Address.Scheme).IsEqualTo("https");
        await Assert.That(options.Address.Host).IsEqualTo("localhost");
        await Assert.That(options.Address.Port).IsEqualTo(2113);
    }

    [Test]
    public async Task generates_guid_address_for_discovery_connection() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Discover,
            Endpoints        = [new("node1", 2113), new("node2", 2113)]
        };

        await Assert.That(options.Address).IsNotNull();
        await Assert.That(options.Address.Scheme).IsEqualTo("kurrentdb+discover");
        await Assert.That(options.Address.Host).IsNotNullOrEmpty();

        await Assert.That(Guid.TryParse(options.Address.Host, out _)).IsTrue();
    }

    [Test]
    public async Task preserves_gossip_options_in_connection_string() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Discover,
            Endpoints        = [new("localhost", 2113)],
            Gossip = new() {
                MaxDiscoverAttempts = 15,
                DiscoveryInterval   = TimeSpan.FromMilliseconds(200),
                Timeout             = TimeSpan.FromSeconds(10),
                ReadPreference      = NodePreference.Leader
            }
        };

        var connectionString = options.GenerateConnectionString();

        await Assert.That(connectionString).Contains("maxDiscoverAttempts=15");
        await Assert.That(connectionString).Contains("discoveryInterval=200");
        await Assert.That(connectionString).Contains("gossipTimeout=10");
        await Assert.That(connectionString).Contains("nodePreference=leader");
    }

    [Test]
    public async Task preserves_resilience_options_in_connection_string() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Direct,
            Endpoints        = [new("localhost", 2113)],
            Resilience = new() {
                KeepAliveInterval = TimeSpan.FromSeconds(120),
                KeepAliveTimeout  = TimeSpan.FromSeconds(60),
                Deadline          = TimeSpan.FromSeconds(45)
            }
        };

        var connectionString = options.GenerateConnectionString();

        await Assert.That(connectionString).Contains("keepAliveInterval=120");
        await Assert.That(connectionString).Contains("keepAliveTimeout=60");
        await Assert.That(connectionString).Contains("defaultDeadline=45000");
    }

    [Test]
    public async Task direct_connection_with_multiple_hosts_is_not_valid() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Direct,
            Endpoints = [
                new DnsEndPoint("first.example.com", 2113),
                new DnsEndPoint("second.example.com", 2113),
                new DnsEndPoint("third.example.com", 2113)
            ]
        };

        var result = options.ValidateOptions();

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task discovery_connection_with_many_hosts_preserves_all_hosts() {
        var endpoints = new[] {
            new DnsEndPoint("node1.example.com", 2113),
            new DnsEndPoint("node2.example.com", 2113),
            new DnsEndPoint("node3.example.com", 2113),
            new DnsEndPoint("node4.example.com", 2113),
            new DnsEndPoint("node5.example.com", 2113)
        };

        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Discover,
            Endpoints        = endpoints
        };

        var connectionString = options.GenerateConnectionString();

        foreach (var endpoint in endpoints)
            await Assert.That(connectionString).Contains($"{endpoint.Host}:{endpoint.Port}");
    }

    [Test]
    public async Task discovery_connection_with_different_node_preferences() {
        var nodePreferences = new[] {
            NodePreference.Leader,
            NodePreference.Follower,
            NodePreference.Random,
            NodePreference.ReadOnlyReplica
        };

        foreach (var preference in nodePreferences) {
            var options = new KurrentClientOptions {
                ConnectionScheme = KurrentConnectionScheme.Discover,
                Endpoints        = [new DnsEndPoint("node1.example.com", 2113)],
                Gossip = new KurrentClientGossipOptions {
                    ReadPreference = preference
                }
            };

            var connectionString = options.GenerateConnectionString();

            if (preference != NodePreference.Random)
                await Assert.That(connectionString).Contains($"nodePreference={preference.ToString().ToLowerInvariant()}");
        }
    }

    [Test]
    public async Task connection_string_with_custom_connection_name() {
        var options = new KurrentClientOptions {
            ConnectionName   = "custom-connection-1",
            ConnectionScheme = KurrentConnectionScheme.Direct,
            Endpoints        = [new DnsEndPoint("localhost", 2113)]
        };

        var connectionString = options.GenerateConnectionString();

        await Assert.That(connectionString).Contains("connectionName=custom-connection-1");
    }

    [Test]
    public async Task connection_string_with_auto_generated_connection_name_omits_parameter() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Direct,
            Endpoints        = [new DnsEndPoint("localhost", 2113)]
        };

        await Assert.That(options.ConnectionName).StartsWith("conn-");

        var connectionString = options.GenerateConnectionString();

        await Assert.That(connectionString).DoesNotContain("connectionName=");
    }

    [Test]
    public async Task parses_connection_string_with_empty_query_parameters() {
        var connectionString = "kurrentdb://localhost:2113?tlsVerifyCert=";
        var options          = KurrentClientOptions.Parse(connectionString);

        await Assert.That(options.Security.Transport.VerifyServerCertificate).IsTrue();
    }

    [Test]
    public async Task client_certificate_auth_is_preserved_in_connection_string() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Direct,
            Endpoints        = [new DnsEndPoint("localhost", 2113)],
            Security = KurrentClientSecurityOptions.MutualTls(
                Uri.EscapeDataString("/path/to/client.crt"),
                Uri.EscapeDataString("/path/to/client.key")
            )
        };

        var connectionString = options.GenerateConnectionString();

        await Assert.That(connectionString).Contains($"{Uri.EscapeDataString("/path/to/client.crt")}");
        await Assert.That(connectionString).Contains($"{Uri.EscapeDataString("/path/to/client.key")}");
    }

    [Test]
    public async Task resilience_with_infinite_timeout_values_creates_valid_connection_string() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Direct,
            Endpoints        = [new DnsEndPoint("localhost", 2113)],
            Resilience = new KurrentClientResilienceOptions {
                KeepAliveInterval = Timeout.InfiniteTimeSpan,
                KeepAliveTimeout  = Timeout.InfiniteTimeSpan,
                Deadline          = Timeout.InfiniteTimeSpan
            }
        };

        var connectionString = options.GenerateConnectionString();

        await Assert.That(connectionString).IsNotNullOrEmpty();
    }

    #region Error Handling and Edge Cases

    [Test]
    public async Task throws_when_parsing_connection_string_with_invalid_node_preference() {
        var connectionString = "kurrentdb+discover://localhost?nodePreference=invalidPreference";

        await Assert.That(() => KurrentClientOptions.Parse(connectionString)).ThrowsException();
    }

    [Test]
    public async Task throws_when_parsing_connection_string_with_invalid_integer_parameters() {
        var connectionString = "kurrentdb://localhost?maxDiscoverAttempts=notAnInteger";

        await Assert.That(() => KurrentClientOptions.Parse(connectionString)).ThrowsException();
    }

    [Test]
    public async Task handles_negative_timeout_values_in_connection_string() {
        var connectionString = "kurrentdb://localhost?keepAliveInterval=-500";

        var options = KurrentClientOptions.Parse(connectionString);

        await Assert.That(options.Resilience.KeepAliveInterval.TotalMilliseconds).IsEqualTo(-500);
    }

    [Test]
    public async Task handles_connection_string_with_zero_timeout_values() {
        var connectionString = "kurrentdb://localhost?keepAliveInterval=0&keepAliveTimeout=0";

        var options = KurrentClientOptions.Parse(connectionString);

        await Assert.That(options.Resilience.KeepAliveInterval).IsEqualTo(TimeSpan.Zero);
        await Assert.That(options.Resilience.KeepAliveTimeout).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task handles_connection_string_with_invalid_boolean_parameters() {
        var connectionString = "kurrentdb://localhost?tls=maybe&tlsVerifyCert=perhaps";

        var options = KurrentClientOptions.Parse(connectionString);

        await Assert.That(options.Security.Transport.IsEnabled).IsTrue();
        await Assert.That(options.Security.Transport.VerifyServerCertificate).IsTrue();
    }

    #endregion

    #region Builder Functionality Tests

    [Test]
    public async Task get_builder_returns_functional_builder() {
        var originalOptions = new KurrentClientOptions {
            ConnectionName   = "test-connection",
            ConnectionScheme = KurrentConnectionScheme.Discover,
            Endpoints        = [new DnsEndPoint("test-host", 2113)]
        };

        var builder = originalOptions.GetBuilder();

        await Assert.That(builder).IsNotNull();

        var newOptions = builder.Build();
        await Assert.That(newOptions.ConnectionName).IsEqualTo(originalOptions.ConnectionName);
        await Assert.That(newOptions.ConnectionScheme).IsEqualTo(originalOptions.ConnectionScheme);
        await Assert.That(newOptions.Endpoints.Length).IsEqualTo(originalOptions.Endpoints.Length);
    }

    [Test]
    public async Task static_build_property_returns_builder_instance() {
        var builder = KurrentClientOptions.Build;

        await Assert.That(builder).IsNotNull();

        var options = builder.Build();
        await Assert.That(options).IsNotNull();
        await Assert.That(options.ConnectionScheme).IsEqualTo(KurrentConnectionScheme.Direct);
    }

    #endregion

    #region Complex Property Logic Tests

    [Test]
    public async Task address_property_handles_discovery_scheme_correctly() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Discover,
            Endpoints        = [new DnsEndPoint("node1", 2113), new DnsEndPoint("node2", 2113)]
        };

        var address = options.Address;
        await Assert.That(address.Scheme).IsEqualTo("kurrentdb+discover");

        await Assert.That(Guid.TryParse(address.Host, out _)).IsTrue();
    }

    [Test]
    public async Task address_property_uses_https_for_secure_transport() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Direct,
            Endpoints        = [new DnsEndPoint("secure-host", 2113)],
            Security         = KurrentClientSecurityOptions.Default
        };

        var address = options.Address;
        await Assert.That(address.Scheme).IsEqualTo("https");
        await Assert.That(address.Host).IsEqualTo("secure-host");
        await Assert.That(address.Port).IsEqualTo(2113);
    }

    [Test]
    public async Task address_property_uses_http_for_insecure_transport() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Direct,
            Endpoints        = [new DnsEndPoint("insecure-host", 2113)],
            Security         = KurrentClientSecurityOptions.Insecure
        };

        var address = options.Address;
        await Assert.That(address.Scheme).IsEqualTo("http");
        await Assert.That(address.Host).IsEqualTo("insecure-host");
        await Assert.That(address.Port).IsEqualTo(2113);
    }

    [Test]
    public async Task has_original_connection_string_returns_false_for_manually_created_options() {
        var options = new KurrentClientOptions {
            ConnectionName = "manual-connection"
        };

        await Assert.That(options.HasOriginalConnectionString).IsFalse();
        await Assert.That(options.OriginalConnectionString).IsEmpty();
    }

    [Test]
    public async Task has_original_connection_string_returns_true_for_parsed_options() {
        var connectionString = "kurrentdb://localhost";
        var options = KurrentClientOptions.Parse(connectionString);

        await Assert.That(options.HasOriginalConnectionString).IsTrue();
        await Assert.That(options.OriginalConnectionString).IsEqualTo(connectionString);
    }

    [Test]
    public async Task to_string_calls_generate_connection_string() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Direct,
            Endpoints        = [new DnsEndPoint("localhost", 2113)]
        };

        var toString = options.ToString();
        var generated = options.GenerateConnectionString();

        await Assert.That(toString).IsEqualTo(generated);
        await Assert.That(toString).Contains("kurrentdb://");
        await Assert.That(toString).Contains("localhost:2113");
    }

    #endregion

    #region Authentication Configuration Edge Cases

    [Test]
    public async Task handles_mixed_authentication_configuration_priority() {
        var connectionString = "kurrentdb://user:pass@localhost?userCertFile=/cert.crt&userKeyFile=/key.key";

        var options = KurrentClientOptions.Parse(connectionString);

        await Assert.That(options.Security.Authentication.IsFileCertificateCredentials).IsTrue();
        await Assert.That(options.Security.Authentication.IsBasicCredentials).IsFalse();
    }

    [Test]
    public async Task handles_basic_authentication_from_connection_string() {
        var connectionString = "kurrentdb://testuser:testpass@localhost";

        var options = KurrentClientOptions.Parse(connectionString);

        await Assert.That(options.Security.Authentication.IsBasicCredentials).IsTrue();
        var credentials = options.Security.Authentication.AsBasicCredentials;
        await Assert.That(credentials.Username).IsEqualTo("testuser");
        await Assert.That(credentials.Password).IsEqualTo("testpass");
    }

    [Test]
    public async Task handles_no_authentication_configuration() {
        var connectionString = "kurrentdb://localhost";

        var options = KurrentClientOptions.Parse(connectionString);

        await Assert.That(options.Security.Authentication.IsNoCredentials).IsTrue();
    }

    #endregion

    #region Transport Security Combinations

    [Test]
    public async Task handles_tls_enabled_with_certificate_verification_disabled() {
        var connectionString = "kurrentdb://localhost?tls=true&tlsVerifyCert=false";

        var options = KurrentClientOptions.Parse(connectionString);

        await Assert.That(options.Security.Transport.IsEnabled).IsTrue();
        await Assert.That(options.Security.Transport.VerifyServerCertificate).IsFalse();
    }

    [Test]
    public async Task handles_tls_disabled_configuration() {
        var connectionString = "kurrentdb://localhost?tls=false";

        var options = KurrentClientOptions.Parse(connectionString);

        await Assert.That(options.Security.Transport.IsEnabled).IsFalse();
    }

    [Test]
    public async Task handles_custom_ca_certificate_configuration() {
        var connectionString = "kurrentdb://localhost?tls=true&tlsCaFile=/path/to/ca.crt";

        var options = KurrentClientOptions.Parse(connectionString);

        await Assert.That(options.Security.Transport.IsEnabled).IsTrue();
        await Assert.That(options.Security.Transport.IsFileCertificateTls).IsTrue();
    }

    #endregion

    #region Validation Integration Tests

    [Test]
    public async Task validation_fails_for_direct_connection_with_multiple_endpoints() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Direct,
            Endpoints = [
                new DnsEndPoint("host1", 2113),
                new DnsEndPoint("host2", 2113)
            ]
        };

        var result = options.ValidateOptions();
        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task validation_succeeds_for_discovery_connection_with_multiple_endpoints() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Discover,
            Endpoints = [
                new DnsEndPoint("host1", 2113),
                new DnsEndPoint("host2", 2113)
            ]
        };

        var result = options.ValidateOptions();
        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task validation_fails_for_empty_endpoints_array() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Direct,
            Endpoints        = []
        };

        var result = options.ValidateOptions();
        await Assert.That(result.IsSuccess).IsFalse();
    }

    #endregion

    #region Connection String Generation Edge Cases

    [Test]
    public async Task generates_minimal_connection_string_for_default_options() {
        var options = new KurrentClientOptions();

        var connectionString = options.GenerateConnectionString();

        await Assert.That(connectionString).Contains("kurrentdb://");
        await Assert.That(connectionString).Contains("localhost:2113");
        await Assert.That(connectionString).DoesNotContain("connectionName=conn-");
    }

    [Test]
    public async Task generates_connection_string_with_all_custom_options() {
        var options = new KurrentClientOptions {
            ConnectionName   = "custom-conn",
            ConnectionScheme = KurrentConnectionScheme.Discover,
            Endpoints        = [new DnsEndPoint("custom-host", 2113)],
            Security         = KurrentClientSecurityOptions.Insecure,
            Gossip = new KurrentClientGossipOptions {
                MaxDiscoverAttempts = 20,
                DiscoveryInterval   = TimeSpan.FromMilliseconds(300),
                Timeout             = TimeSpan.FromSeconds(15),
                ReadPreference      = NodePreference.Leader
            },
            Resilience = new KurrentClientResilienceOptions {
                KeepAliveInterval = TimeSpan.FromMinutes(2),
                KeepAliveTimeout  = TimeSpan.FromMinutes(1),
                Deadline          = TimeSpan.FromSeconds(45)
            }
        };

        var connectionString = options.GenerateConnectionString();

        await Assert.That(connectionString).IsNotNullOrEmpty();
        await Assert.That(connectionString).Contains("kurrentdb+discover://");
        await Assert.That(connectionString).Contains("custom-host:2113");
        await Assert.That(connectionString).Contains("tls=false");
        await Assert.That(connectionString).Contains("connectionName=custom-conn");
        await Assert.That(connectionString).Contains("nodePreference=leader");
        await Assert.That(connectionString).Contains("maxDiscoverAttempts=20");
        await Assert.That(connectionString).Contains("discoveryInterval=300");
        await Assert.That(connectionString).Contains("gossipTimeout=15");
        await Assert.That(connectionString).Contains("keepAliveInterval=120");
        await Assert.That(connectionString).Contains("keepAliveTimeout=60");
        await Assert.That(connectionString).Contains("defaultDeadline=45000");
    }

    #endregion
}
