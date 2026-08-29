using System.Net;
using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Tests.Options;

public class KurrentClientOptionsTests {
    [Test]
    public async ValueTask default_options_should_be_valid() {
        var options = new KurrentClientOptions();

        var validationResult = options.Validate();

        await Assert.That(validationResult.IsValid).IsTrue();
    }

    [Test]
    public async ValueTask creates_default_options_with_single_endpoint() {
        var options = new KurrentClientOptions();

        await Assert.That(options.ConnectionScheme).IsEqualTo(KurrentConnectionScheme.Direct);
        await Assert.That(options.Endpoints).HasSingleItem();
        await Assert.That(options.Gossip).IsDeepEquivalentTo(KurrentClientGossipOptions.Default);
        await Assert.That(options.Security).IsDeepEquivalentTo(KurrentClientSecurityOptions.Tls);
        await Assert.That(options.Resilience).IsDeepEquivalentTo(KurrentClientResilienceOptions.Default);
        await Assert.That(options.Schema).IsDeepEquivalentTo(KurrentClientSchemaOptions.Disabled);
        await Assert.That(options.Interceptors).IsEmpty();
    }

    [Test]
    public async ValueTask creates_options_from_connection_string() {
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
    public async ValueTask creates_options_from_discovery_connection_string() {
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
    public async ValueTask generates_connection_string_that_matches_original() {
        var options = KurrentClientOptions.Parse("kurrentdb://admin:changeit@localhost:2113?tls=false");
        var generated = options.ToString();
        await Assert.That(generated).IsEqualTo(generated);
    }

    [Test]
    public async ValueTask handles_special_characters_in_credentials() {
        var connectionString = "kurrentdb://user%40domain:p%40ssw%3Ard@localhost:2113";
        var options          = KurrentClientOptions.Parse(connectionString);

        var credentials = options.Security.Authentication.AsBasicCredentials;
        await Assert.That(credentials.Username).IsEqualTo("user@domain");
        await Assert.That(credentials.Password).IsEqualTo("p@ssw:rd");

        var generatedString = options.ToString();
        await Assert.That(generatedString).Contains("user%40domain:p%40ssw%3Ard@");
    }

    [Test]
    public async ValueTask generates_proper_http_uri_based_on_transport_security() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Direct,
            Endpoints        = [new("localhost", 2113)],
            Security         = KurrentClientSecurityOptions.Tls
        };

        await Assert.That(options.HttpUriScheme).IsEqualTo(Uri.UriSchemeHttps);

        options = options with { Security = KurrentClientSecurityOptions.Insecure };
        await Assert.That(options.HttpUriScheme).IsEqualTo(Uri.UriSchemeHttp);
    }

    [Test]
    public async ValueTask generates_address_correctly_for_direct_connection() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Direct,
            Endpoints        = [new("localhost", 2113)],
            Security         = KurrentClientSecurityOptions.Tls
        };

        await Assert.That(options.Address).IsNotNull();
        await Assert.That(options.Address.Scheme).IsEqualTo("https");
        await Assert.That(options.Address.Host).IsEqualTo("localhost");
        await Assert.That(options.Address.Port).IsEqualTo(2113);
    }

    [Test]
    public async ValueTask generates_guid_address_for_discovery_connection() {
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
    public async ValueTask preserves_gossip_options_in_connection_string() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Discover,
            Endpoints        = [new("localhost", 2113)],
            Gossip = new() {
                MaxDiscoverAttempts = 15,
                DiscoveryInterval   = TimeSpan.FromMilliseconds(200),
                Timeout             = TimeSpan.FromSeconds(10),
                // Follower, not Leader: only a non-default preference is written out.
                ReadPreference      = NodeReadPreference.Follower
            }
        };

        var connectionString = options.ToString();

        await Assert.That(connectionString).Contains("maxDiscoverAttempts=15");
        await Assert.That(connectionString).Contains("discoveryInterval=200ms");
        await Assert.That(connectionString).Contains("gossipTimeout=10s");
        await Assert.That(connectionString).Contains("nodePreference=follower");
    }

    [Test]
    public async ValueTask preserves_resilience_options_in_connection_string() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Direct,
            Endpoints        = [new("localhost", 2113)],
            Resilience = new() {
                KeepAliveInterval = TimeSpan.FromSeconds(120),
                KeepAliveTimeout  = TimeSpan.FromSeconds(60),
                Deadline          = TimeSpan.FromSeconds(45)
            }
        };

        var connectionString = options.ToString();

        await Assert.That(connectionString).Contains("keepAliveInterval=120s");
        await Assert.That(connectionString).Contains("keepAliveTimeout=60s");
        await Assert.That(connectionString).Contains("defaultDeadline=45s");
    }

    [Test]
    public async ValueTask direct_connection_with_multiple_hosts_is_not_valid() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Direct,
            Endpoints = [
                new DnsEndPoint("first.example.com", 2113),
                new DnsEndPoint("second.example.com", 2113),
                new DnsEndPoint("third.example.com", 2113)
            ]
        };

        var result = options.Validate();

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async ValueTask duplicate_initialization_task_names_fail_validation() {
        var options = new KurrentClientOptions();

        options.Initialization
            .AddTask("same-name", (_, _) => ValueTask.CompletedTask)
            .AddTask("same-name", (_, _) => ValueTask.CompletedTask);

        var result = options.Validate();

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async ValueTask distinct_initialization_task_names_pass_validation() {
        var options = new KurrentClientOptions();

        options.Initialization
            .AddTask("first-task", (_, _) => ValueTask.CompletedTask)
            .AddTask("second-task", (_, _) => ValueTask.CompletedTask);

        var result = options.Validate();

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async ValueTask discovery_connection_with_many_hosts_preserves_all_hosts() {
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

        var connectionString = options.ToString();

        foreach (var endpoint in endpoints)
            await Assert.That(connectionString).Contains($"{endpoint.Host}:{endpoint.Port}");
    }

    [Test]
    public async ValueTask discovery_connection_with_different_node_preferences() {
        var nodePreferences = new[] {
            NodeReadPreference.Leader,
            NodeReadPreference.Follower,
            NodeReadPreference.Random,
            NodeReadPreference.ReadOnlyReplica
        };

        foreach (var preference in nodePreferences) {
            var options = new KurrentClientOptions {
                ConnectionScheme = KurrentConnectionScheme.Discover,
                Endpoints        = [new DnsEndPoint("node1.example.com", 2113)],
                Gossip = new KurrentClientGossipOptions {
                    ReadPreference = preference
                }
            };

            var connectionString = options.ToString();

            // The default preference is omitted from the connection string.
            if (preference != KurrentClientGossipOptions.Default.ReadPreference)
                await Assert.That(connectionString).Contains($"nodePreference={preference.ToString().ToLowerInvariant()}");
        }
    }

    [Test]
    public async ValueTask connection_string_with_custom_connection_name() {
        var options = new KurrentClientOptions {
            ConnectionName   = "custom-connection-1",
            ConnectionScheme = KurrentConnectionScheme.Direct,
            Endpoints        = [new DnsEndPoint("localhost", 2113)]
        };

        var connectionString = options.ToString();

        await Assert.That(connectionString).Contains("connectionName=custom-connection-1");
    }

    [Test]
    public async ValueTask connection_string_with_auto_generated_connection_name_omits_parameter() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Direct,
            Endpoints        = [new DnsEndPoint("localhost", 2113)]
        };

        await Assert.That(options.ConnectionName).StartsWith("conn-");

        var connectionString = options.ToString();

        await Assert.That(connectionString).DoesNotContain("connectionName=");
    }

    [Test]
    public async ValueTask parses_connection_string_with_empty_query_parameters() {
        var connectionString = "kurrentdb://localhost:2113?tlsVerifyCert=";
        var options          = KurrentClientOptions.Parse(connectionString);

        await Assert.That(options.Security.Transport.VerifyServerCertificate).IsTrue();
    }

    [Test]
    public async ValueTask client_certificate_auth_is_preserved_in_connection_string() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Direct,
            Endpoints        = [new DnsEndPoint("localhost", 2113)],
            Security = KurrentClientSecurityOptions.MutualTls(
                Uri.EscapeDataString("/path/to/client.crt"),
                Uri.EscapeDataString("/path/to/client.key")
            )
        };

        var connectionString = options.ToString();

        await Assert.That(connectionString).Contains($"{Uri.EscapeDataString("/path/to/client.crt")}");
        await Assert.That(connectionString).Contains($"{Uri.EscapeDataString("/path/to/client.key")}");
    }

    [Test]
    public async ValueTask resilience_with_infinite_timeout_values_creates_valid_connection_string() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Direct,
            Endpoints        = [new DnsEndPoint("localhost", 2113)],
            Resilience = new KurrentClientResilienceOptions {
                KeepAliveInterval = Timeout.InfiniteTimeSpan,
                KeepAliveTimeout  = Timeout.InfiniteTimeSpan,
                Deadline          = Timeout.InfiniteTimeSpan
            }
        };

        var connectionString = options.ToString();

        await Assert.That(connectionString).IsNotNullOrEmpty();
    }

    #region Error Handling and Edge Cases

    [Test]
    public async ValueTask throws_when_parsing_connection_string_with_invalid_node_preference() {
        var connectionString = "kurrentdb+discover://localhost?nodePreference=invalidPreference";

        await Assert.That(() => KurrentClientOptions.Parse(connectionString)).ThrowsException();
    }

    [Test]
    public async ValueTask parses_node_preference_from_the_legacy_cross_sdk_parameter_name() {
        var connectionString = "kurrentdb+discover://localhost?nodePreference=follower";

        var options = KurrentClientOptions.Parse(connectionString);

        await Assert.That(options.Gossip.ReadPreference).IsEqualTo(NodeReadPreference.Follower);
    }

    [Test]
    public async ValueTask parses_node_preference_from_the_read_preference_parameter_name() {
        var connectionString = "kurrentdb+discover://localhost?readPreference=follower";

        var options = KurrentClientOptions.Parse(connectionString);

        await Assert.That(options.Gossip.ReadPreference).IsEqualTo(NodeReadPreference.Follower);
    }

    [Test]
    public async ValueTask read_preference_parameter_wins_when_both_preference_parameters_are_present() {
        var connectionString = "kurrentdb+discover://localhost?nodePreference=leader&readPreference=follower";

        var options = KurrentClientOptions.Parse(connectionString);

        await Assert.That(options.Gossip.ReadPreference).IsEqualTo(NodeReadPreference.Follower);
    }

    [Test]
    public async ValueTask parses_certificate_credentials_with_key_password() {
        var connectionString = "kurrentdb://localhost?userCertFile=/certs/client.crt&userKeyFile=/certs/client.key&userKeyPassword=changeit";

        var options = KurrentClientOptions.Parse(connectionString);

        await Assert.That(options.Security.Authentication.IsFileCertificateCredentials).IsTrue();

        var credentials = options.Security.Authentication.AsFileCertificateCredentials;
        await Assert.That(credentials.CertificatePath).IsEqualTo("/certs/client.crt");
        await Assert.That(credentials.KeyPath).IsEqualTo("/certs/client.key");
        await Assert.That(credentials.Password).IsEqualTo("changeit");
    }

    [Test]
    public async ValueTask throws_when_parsing_connection_string_with_invalid_integer_parameters() {
        var connectionString = "kurrentdb://localhost?maxDiscoverAttempts=notAnInteger";

        await Assert.That(() => KurrentClientOptions.Parse(connectionString)).ThrowsException();
    }

    [Test]
    public async ValueTask rejects_negative_timeout_values_in_connection_string() {
        var connectionString = "kurrentdb://localhost?keepAliveInterval=-500";

        await Assert.That(() => KurrentClientOptions.Parse(connectionString)).ThrowsException();
    }

    [Test]
    public async ValueTask rejects_zero_timeout_values_in_connection_string() {
        var connectionString = "kurrentdb://localhost?keepAliveInterval=0&keepAliveTimeout=0";

        await Assert.That(() => KurrentClientOptions.Parse(connectionString)).ThrowsException();
    }

    [Test]
    public async ValueTask treats_minus_one_as_an_infinite_keep_alive_interval() {
        var connectionString = "kurrentdb://localhost?keepAliveInterval=-1";

        var options = KurrentClientOptions.Parse(connectionString);

        await Assert.That(options.Resilience.KeepAliveInterval).IsEqualTo(Timeout.InfiniteTimeSpan);
    }

    [Test]
    public async ValueTask handles_connection_string_with_invalid_boolean_parameters() {
        var connectionString = "kurrentdb://localhost?tls=maybe&tlsVerifyCert=perhaps";

        var options = KurrentClientOptions.Parse(connectionString);

        await Assert.That(options.Security.Transport.IsEnabled).IsTrue();
        await Assert.That(options.Security.Transport.VerifyServerCertificate).IsTrue();
    }

    #endregion

    #region Complex Property Logic Tests

    [Test]
    public async ValueTask address_property_handles_discovery_scheme_correctly() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Discover,
            Endpoints        = [new DnsEndPoint("node1", 2113), new DnsEndPoint("node2", 2113)]
        };

        var address = options.Address;
        await Assert.That(address.Scheme).IsEqualTo("kurrentdb+discover");

        await Assert.That(Guid.TryParse(address.Host, out _)).IsTrue();
    }

    [Test]
    public async ValueTask address_property_uses_https_for_secure_transport() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Direct,
            Endpoints        = [new DnsEndPoint("secure-host", 2113)],
            Security         = KurrentClientSecurityOptions.Tls
        };

        var address = options.Address;
        await Assert.That(address.Scheme).IsEqualTo("https");
        await Assert.That(address.Host).IsEqualTo("secure-host");
        await Assert.That(address.Port).IsEqualTo(2113);
    }

    [Test]
    public async ValueTask address_property_uses_http_for_insecure_transport() {
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
    public async ValueTask has_original_connection_string_returns_false_for_manually_created_options() {
        var options = new KurrentClientOptions {
            ConnectionName = "manual-connection"
        };

        await Assert.That(options.HasOriginalConnectionString).IsFalse();
        await Assert.That(options.OriginalConnectionString).IsEmpty();
    }

    [Test]
    public async ValueTask has_original_connection_string_returns_true_for_parsed_options() {
        var connectionString = "kurrentdb://localhost";
        var options = KurrentClientOptions.Parse(connectionString);

        await Assert.That(options.HasOriginalConnectionString).IsTrue();
        await Assert.That(options.OriginalConnectionString).IsEqualTo(connectionString);
    }

    [Test]
    public async ValueTask to_string_calls_generate_connection_string() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Direct,
            Endpoints        = [new DnsEndPoint("localhost", 2113)]
        };

        var toString  = options.ToString();
        var generated = options.ToString();

        await Assert.That(toString).IsEqualTo(generated);
        await Assert.That(toString).Contains("kurrentdb://");
        await Assert.That(toString).Contains("localhost:2113");
    }

    #endregion

    #region Authentication Configuration Edge Cases

    [Test]
    public async ValueTask handles_mixed_authentication_configuration_priority() {
        var connectionString = "kurrentdb://user:pass@localhost?userCertFile=/cert.crt&userKeyFile=/key.key";

        var options = KurrentClientOptions.Parse(connectionString);

        await Assert.That(options.Security.Authentication.IsFileCertificateCredentials).IsTrue();
        await Assert.That(options.Security.Authentication.IsBasicCredentials).IsFalse();
    }

    [Test]
    public async ValueTask handles_basic_authentication_from_connection_string() {
        var connectionString = "kurrentdb://testuser:testpass@localhost";

        var options = KurrentClientOptions.Parse(connectionString);

        await Assert.That(options.Security.Authentication.IsBasicCredentials).IsTrue();
        var credentials = options.Security.Authentication.AsBasicCredentials;
        await Assert.That(credentials.Username).IsEqualTo("testuser");
        await Assert.That(credentials.Password).IsEqualTo("testpass");
    }

    [Test]
    public async ValueTask handles_no_authentication_configuration() {
        var connectionString = "kurrentdb://localhost";

        var options = KurrentClientOptions.Parse(connectionString);

        await Assert.That(options.Security.Authentication.IsNoCredentials).IsTrue();
    }

    #endregion

    #region Transport Security Combinations

    [Test]
    public async ValueTask handles_tls_enabled_with_certificate_verification_disabled() {
        var connectionString = "kurrentdb://localhost?tls=true&tlsVerifyCert=false";

        var options = KurrentClientOptions.Parse(connectionString);

        await Assert.That(options.Security.Transport.IsEnabled).IsTrue();
        await Assert.That(options.Security.Transport.VerifyServerCertificate).IsFalse();
    }

    [Test]
    public async ValueTask handles_tls_disabled_configuration() {
        var connectionString = "kurrentdb://localhost?tls=false";

        var options = KurrentClientOptions.Parse(connectionString);

        await Assert.That(options.Security.Transport.IsEnabled).IsFalse();
    }

    [Test]
    public async ValueTask handles_custom_ca_certificate_configuration() {
        var connectionString = "kurrentdb://localhost?tls=true&tlsCaFile=/path/to/ca.crt";

        var options = KurrentClientOptions.Parse(connectionString);

        await Assert.That(options.Security.Transport.IsEnabled).IsTrue();
        await Assert.That(options.Security.Transport.IsFileCertificateTls).IsTrue();
    }

    #endregion

    #region Validation Integration Tests

    [Test]
    public async ValueTask validation_fails_for_direct_connection_with_multiple_endpoints() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Direct,
            Endpoints = [
                new DnsEndPoint("host1", 2113),
                new DnsEndPoint("host2", 2113)
            ]
        };

        var result = options.Validate();
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async ValueTask validation_succeeds_for_discovery_connection_with_multiple_endpoints() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Discover,
            Endpoints = [
                new DnsEndPoint("host1", 2113),
                new DnsEndPoint("host2", 2113)
            ]
        };

        var result = options.Validate();
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async ValueTask validation_fails_for_empty_endpoints_array() {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Direct,
            Endpoints        = []
        };

        var result = options.Validate();
        await Assert.That(result.IsValid).IsFalse();
    }

    #endregion

    #region Connection String Generation Edge Cases

    [Test]
    public async ValueTask generates_minimal_connection_string_for_default_options() {
        var options = new KurrentClientOptions();

        var connectionString = options.ToString();

        await Assert.That(connectionString).Contains("kurrentdb://");
        await Assert.That(connectionString).Contains("localhost:2113");
        await Assert.That(connectionString).DoesNotContain("connectionName=conn-");
    }

    [Test]
    public async ValueTask generates_connection_string_with_all_custom_options() {
        var options = new KurrentClientOptions {
            ConnectionName   = "custom-conn",
            ConnectionScheme = KurrentConnectionScheme.Discover,
            Endpoints        = [new DnsEndPoint("custom-host", 2113)],
            Security         = KurrentClientSecurityOptions.Insecure,
            Gossip = new KurrentClientGossipOptions {
                MaxDiscoverAttempts = 20,
                DiscoveryInterval   = TimeSpan.FromMilliseconds(300),
                Timeout             = TimeSpan.FromSeconds(15),
                ReadPreference      = NodeReadPreference.Follower
            },
            Resilience = new KurrentClientResilienceOptions {
                KeepAliveInterval = TimeSpan.FromMinutes(2),
                KeepAliveTimeout  = TimeSpan.FromMinutes(1),
                Deadline          = TimeSpan.FromSeconds(45)
            }
        };

        var connectionString = options.ToString();

        await Assert.That(connectionString).IsNotNullOrEmpty();
        await Assert.That(connectionString).Contains("kurrentdb+discover://");
        await Assert.That(connectionString).Contains("custom-host:2113");
        await Assert.That(connectionString).Contains("tls=false");
        await Assert.That(connectionString).Contains("connectionName=custom-conn");
        await Assert.That(connectionString).Contains("nodePreference=follower");
        await Assert.That(connectionString).Contains("maxDiscoverAttempts=20");
        await Assert.That(connectionString).Contains("discoveryInterval=300ms");
        await Assert.That(connectionString).Contains("gossipTimeout=15s");
        await Assert.That(connectionString).Contains("keepAliveInterval=120s");
        await Assert.That(connectionString).Contains("keepAliveTimeout=60s");
        await Assert.That(connectionString).Contains("defaultDeadline=45s");
    }

    #endregion

    #region ->> Duration Units <<-

    [Test]
    [BareDurationUnits]
    public async ValueTask bare_duration_uses_the_unit_the_other_sdks_read(string key, string value, TimeSpan expected) {
        // Arrange
        var connectionString = $"kurrentdb://localhost?{key}={value}";

        // Act
        var options = KurrentClientOptions.Parse(connectionString);

        // Assert
        await Assert.That(Read(options, key)).IsEqualTo(expected);
    }

    [Test]
    [SuffixedDurationUnits]
    public async ValueTask duration_suffix_overrides_the_bare_unit(string key, string value, TimeSpan expected) {
        // Arrange
        var connectionString = $"kurrentdb://localhost?{key}={value}";

        // Act
        var options = KurrentClientOptions.Parse(connectionString);

        // Assert
        await Assert.That(Read(options, key)).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask native_connection_string_round_trips_every_duration() {
        // Arrange
        var expected = KurrentClientOptions.Parse(
            "kurrentdb://localhost:2113?keepAliveInterval=20&keepAliveTimeout=10"
            + "&gossipTimeout=3&discoveryInterval=250&defaultDeadline=5000");

        // Act
        var actual = KurrentClientOptions.Parse(expected.ToConnectionString());

        // Assert
        await Assert.That(actual.Resilience).IsDeepEquivalentTo(expected.Resilience);
        await Assert.That(actual.Gossip).IsDeepEquivalentTo(expected.Gossip);
    }

    [Test]
    public async ValueTask legacy_connection_string_round_trips_whole_second_durations() {
        // Arrange
        var expected = KurrentClientOptions.Parse(
            "kurrentdb://localhost:2113?keepAliveInterval=20&keepAliveTimeout=10"
            + "&gossipTimeout=3&discoveryInterval=250&defaultDeadline=5000");

        // Act
        var actual = KurrentClientOptions.Parse(expected.ToConnectionString(legacy: true));

        // Assert
        await Assert.That(actual.Resilience).IsDeepEquivalentTo(expected.Resilience);
        await Assert.That(actual.Gossip).IsDeepEquivalentTo(expected.Gossip);
    }

    [Test]
    public async ValueTask legacy_connection_string_carries_no_unit_suffix() {
        // Arrange
        var options = KurrentClientOptions.Parse(
            "kurrentdb://localhost:2113?keepAliveInterval=45&gossipTimeout=3&discoveryInterval=250");

        // Act
        var connectionString = options.ToConnectionString(legacy: true);

        // Assert
        await Assert.That(connectionString).DoesNotContain("keepAliveInterval=45s");
        await Assert.That(connectionString).DoesNotContain("gossipTimeout=3s");
        await Assert.That(connectionString).DoesNotContain("discoveryInterval=250ms");
    }

    static TimeSpan Read(KurrentClientOptions options, string key) => key switch {
        "keepAliveInterval" => options.Resilience.KeepAliveInterval,
        "keepAliveTimeout"  => options.Resilience.KeepAliveTimeout,
        "defaultDeadline"   => options.Resilience.Deadline,
        "gossipTimeout"     => options.Gossip.Timeout,
        "discoveryInterval" => options.Gossip.DiscoveryInterval,
        _                   => throw new ArgumentOutOfRangeException(nameof(key), key, "Not a duration parameter.")
    };

    // The bare unit is fixed per parameter by the published cross-SDK tables, not chosen by Surge.
    public class BareDurationUnits : TestCaseGenerator<string, string, TimeSpan> {
        protected override IEnumerable<(string, string, TimeSpan)> Data() => [
            // KeepAliveInterval stays at or above the default KeepAliveTimeout, which the
            // validator requires when only one of the pair is supplied.
            ("keepAliveInterval", "45",   TimeSpan.FromSeconds(45)),
            ("keepAliveTimeout",  "10",   TimeSpan.FromSeconds(10)),
            ("gossipTimeout",     "3",    TimeSpan.FromSeconds(3)),
            ("discoveryInterval", "250",  TimeSpan.FromMilliseconds(250)),
            ("defaultDeadline",   "5000", TimeSpan.FromMilliseconds(5000))
        ];
    }

    public class SuffixedDurationUnits : TestCaseGenerator<string, string, TimeSpan> {
        protected override IEnumerable<(string, string, TimeSpan)> Data() => [
            ("keepAliveInterval", "45s",     TimeSpan.FromSeconds(45)),
            ("keepAliveInterval", "45000ms", TimeSpan.FromSeconds(45)),
            ("gossipTimeout",     "3000ms",  TimeSpan.FromSeconds(3)),
            ("discoveryInterval", "2s",      TimeSpan.FromSeconds(2)),
            ("defaultDeadline",   "5s",      TimeSpan.FromSeconds(5))
        ];
    }

    #endregion
}
