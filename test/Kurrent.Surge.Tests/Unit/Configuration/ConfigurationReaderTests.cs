using System.Net;
using Kurrent.Surge.Client.Streams;
using Microsoft.Extensions.Configuration;

namespace Kurrent.Surge.Tests.Configuration;

/// <summary>
/// Covers the SDK's own configuration reading: the text forms it defines, and how it reports a
/// setting it cannot use. How a key reaches <see cref="IConfiguration"/> is the framework's job and
/// is not tested here.
/// </summary>
[Category("Configuration")]
public class ConfigurationReaderTests {
    static IConfiguration Config(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => (string?)e.Value))
            .Build();

    static ConfigurationReadResult<KurrentClientOptions> ReadClient(params (string, string)[] entries) =>
        KurrentSurgeConfigurationReader.ReadClientOptions(Config(entries));

    #region ->> Duration syntax <<-

    [Test]
    [Arguments("30000", 30_000)]
    [Arguments("00:00:30", 30_000)]
    [Arguments("1.00:00:00", 86_400_000)]
    public async ValueTask a_duration_reads_as_milliseconds_or_as_a_timespan(string text, int expectedMilliseconds) {
        // Arrange
        var expected = TimeSpan.FromMilliseconds(expectedMilliseconds);

        // Act
        var result = ReadClient(("Kurrent:Surge:Resilience:KeepAliveInterval", text));

        // Assert
        await Assert.That(result.Value.Resilience.KeepAliveInterval).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask an_unreadable_duration_is_reported_and_the_previous_value_kept() {
        // Arrange
        var untouched = new KurrentClientOptions().Resilience.KeepAliveInterval;

        // Act
        var result = ReadClient(("Kurrent:Surge:Resilience:KeepAliveInterval", "whenever"));

        // Assert
        await Assert.That(result.Value.Resilience.KeepAliveInterval).IsEqualTo(untouched);
        await Assert.That(result.HasErrors).IsTrue();
        await Assert.That(result.Problems[0].Path).IsEqualTo("Kurrent:Surge:Resilience:KeepAliveInterval");
    }

    #endregion // Duration syntax

    #region ->> Endpoint syntax <<-

    [Test]
    [Arguments("node-a", "node-a", 2113)]
    [Arguments("node-a:1113", "node-a", 1113)]
    [Arguments("[::1]:1113", "[::1]", 1113)]
    public async ValueTask an_endpoint_reads_from_its_text_form(string text, string expectedHost, int expectedPort) {
        // Act
        var result = ReadClient(("Kurrent:Surge:Endpoints:0", text));

        // Assert
        await Assert.That(result.Value.Endpoints[0]).IsEqualTo(new DnsEndPoint(expectedHost, expectedPort));
    }

    [Test]
    public async ValueTask an_endpoint_reads_from_its_long_form() {
        // Act
        var result = ReadClient(
            ("Kurrent:Surge:Endpoints:0:Host", "node-a"),
            ("Kurrent:Surge:Endpoints:0:Port", "1113"),
            ("Kurrent:Surge:Endpoints:1:Host", "node-b"));

        // Assert — the second endpoint omits its port and takes the default
        await Assert.That(result.Value.Endpoints).IsEquivalentTo([
            new DnsEndPoint("node-a", 1113),
            new DnsEndPoint("node-b", 2113)
        ]);
    }

    [Test]
    [Arguments("node-a:not-a-port")]
    [Arguments(":2113")]
    [Arguments("node-a:99999")]
    public async ValueTask an_unreadable_endpoint_is_reported(string text) {
        // Act
        var result = ReadClient(("Kurrent:Surge:Endpoints:0", text));

        // Assert
        await Assert.That(result.HasErrors).IsTrue();
    }

    #endregion // Endpoint syntax

    #region ->> Transport security <<-

    [Test]
    [Arguments("none", false, false)]
    [Arguments("standard", true, true)]
    [Arguments("tls", true, true)]
    [Arguments("insecure", true, false)]
    public async ValueTask transport_security_reads_from_its_mode_name(string text, bool enabled, bool verifies) {
        // Act
        var parsed = TransportSecurity.TryParse(text, null, out var transport);

        // Assert
        await Assert.That(parsed).IsTrue();
        await Assert.That(transport.IsEnabled).IsEqualTo(enabled);
        await Assert.That(transport.VerifyServerCertificate).IsEqualTo(verifies);
    }

    [Test]
    public async ValueTask an_unknown_transport_mode_is_rejected() {
        await Assert.That(TransportSecurity.TryParse("mutual", null, out _)).IsFalse();
    }

    [Test]
    public async ValueTask the_short_and_long_transport_forms_agree() {
        // Act
        var shortForm = ReadClient(("Kurrent:Surge:Security:Transport", "insecure"));
        var longForm  = ReadClient(("Kurrent:Surge:Security:Transport:Mode", "insecure"));

        // Assert
        await Assert.That(longForm.Value.Security.Transport).IsEqualTo(shortForm.Value.Security.Transport);
    }

    [Test]
    public async ValueTask a_certificate_path_selects_the_file_certificate_mode() {
        // Act
        var result = ReadClient(
            ("Kurrent:Surge:Security:Transport:CaPath", "/certs/ca.pem"),
            ("Kurrent:Surge:Security:Transport:Password", "secret"));

        // Assert — the path is carried, not the file; loading happens at connect time
        await Assert.That(result.Value.Security.Transport.IsEnabled).IsTrue();
        await Assert.That(result.IsClean).IsTrue();
    }

    #endregion // Transport security

    #region ->> Credentials <<-

    [Test]
    [Arguments("admin:changeit", "admin", "changeit")]
    [Arguments("admin", "admin", null)]
    [Arguments("admin:pass:with:colons", "admin", "pass:with:colons")]
    public async ValueTask credentials_read_from_their_text_form(string text, string username, string? password) {
        // Act
        var parsed = ClientCredentials.TryParse(text, null, out var credentials);

        // Assert
        var basic = credentials.Match<BasicCredentials?>(_ => null, b => b, _ => null, _ => null);

        await Assert.That(parsed).IsTrue();
        await Assert.That(basic).IsNotNull();
        await Assert.That(basic!.Username).IsEqualTo(username);
        await Assert.That(basic.Password).IsEqualTo(password);
    }

    [Test]
    public async ValueTask credentials_text_never_carries_the_password() {
        // Act
        var text = ClientCredentials.Basic("admin", "changeit").ToText();

        // Assert
        await Assert.That(text).DoesNotContain("changeit");
    }

    #endregion // Credentials

    #region ->> Problem reporting <<-

    [Test]
    public async ValueTask a_misspelled_key_is_reported_as_a_warning_and_does_not_throw() {
        // Act
        var result = ReadClient(("Kurrent:Surge:ConectionName", "typo"));

        // Assert
        await Assert.That(result.HasErrors).IsFalse();
        await Assert.That(result.Problems).HasCount(1);
        await Assert.That(result.Problems[0].Severity).IsEqualTo(ConfigurationProblemSeverity.Warning);
        await Assert.That(result.Problems[0].Path).IsEqualTo("Kurrent:Surge:ConectionName");
    }

    [Test]
    public async ValueTask a_setting_that_only_code_can_supply_is_reported() {
        // Act
        var result = ReadClient(("Kurrent:Surge:LoggerFactory", "serilog"));

        // Assert
        await Assert.That(result.Problems.Any(p => p.Path == "Kurrent:Surge:LoggerFactory")).IsTrue();
        await Assert.That(result.HasErrors).IsFalse();
    }

    [Test]
    public async ValueTask one_unusable_setting_does_not_cost_the_rest_of_the_section() {
        // Act
        var result = ReadClient(
            ("Kurrent:Surge:ConnectionName", "orders-api"),
            ("Kurrent:Surge:Resilience:MaxAttempts", "not-a-number"),
            ("Kurrent:Surge:Compression:Enabled", "true"));

        // Assert
        await Assert.That(result.Value.ConnectionName).IsEqualTo("orders-api");
        await Assert.That(result.Value.Compression.Enabled).IsTrue();
    }

    #endregion // Problem reporting

    #region ->> Processor sections <<-

    [Test]
    [Arguments("FraudDetection")]
    [Arguments("fraud-detection")]
    [Arguments("fraud_detection")]
    public async ValueTask a_processor_section_is_found_by_any_spelling_of_its_name(string sectionKey) {
        // Arrange
        var configuration = Config(($"Kurrent:Surge:Processors:{sectionKey}:Debug", "true"));

        // Act
        var result = KurrentSurgeConfigurationReader.ReadProcessorOptions(configuration, "FraudDetection");

        // Assert
        await Assert.That(result.Value.Debug).IsTrue();
    }

    [Test]
    public async ValueTask a_processor_reads_its_own_connection() {
        // Arrange
        var configuration = Config(
            ("Kurrent:Surge:ConnectionString", "kurrentdb://shared:2113?tls=false"),
            ("Kurrent:Surge:Processors:Fraud:Connection:ConnectionString", "kurrentdb://dedicated:2113?tls=false"));

        // Act
        var result = KurrentSurgeConfigurationReader.ReadProcessorOptions(configuration, "Fraud");

        // Assert
        var clientOptions = (KurrentClientOptions)result.Value.ClientOptions!;
        await Assert.That(clientOptions.Endpoints[0].Host).IsEqualTo("dedicated");
    }

    [Test]
    public async ValueTask an_empty_filter_is_a_configuration_error() {
        // Arrange — the raw log is RecordFilter.None, asked for by name; a blank value must not select
        // it silently, and the default stays in place.
        var untouched     = RecordFilter.ExcludeSystemRecords;
        var configuration = Config(("Kurrent:Surge:Processors:Fraud:Subscription:Filter", ""));

        // Act
        var result = KurrentSurgeConfigurationReader.ReadProcessorOptions(configuration, "Fraud");

        // Assert
        await Assert.That(result.Value.Subscription.Filter).IsEqualTo(untouched);
        await Assert.That(result.HasErrors).IsTrue();
        await Assert.That(result.Problems.Any(p => p.Path.EndsWith("Subscription:Filter"))).IsTrue();
    }

    [Test]
    public async ValueTask a_filter_reads_from_its_expression() {
        // Arrange
        var expression    = "schema_name = 'Transaction'";
        var configuration = Config(("Kurrent:Surge:Processors:Fraud:Subscription:Filter", expression));

        // Act
        var result = KurrentSurgeConfigurationReader.ReadProcessorOptions(configuration, "Fraud");

        // Assert
        await Assert.That(result.Value.Subscription.Filter.Expression).IsEqualTo(expression);
    }

    [Test]
    public async ValueTask an_internal_setting_is_not_configurable() {
        // Arrange — MaxParallelism is internal, so configuration cannot reach it
        var configuration = Config(("Kurrent:Surge:Processors:Fraud:MaxParallelism", "8"));

        // Act
        var result = KurrentSurgeConfigurationReader.ReadProcessorOptions(configuration, "Fraud");

        // Assert
        await Assert.That(result.Problems.Any(p => p.Path.EndsWith("MaxParallelism"))).IsTrue();
        await Assert.That(result.HasErrors).IsFalse();
    }

    #endregion // Processor sections
}
