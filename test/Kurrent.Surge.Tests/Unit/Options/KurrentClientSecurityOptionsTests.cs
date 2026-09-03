using Kurrent.Surge.Validation;

namespace Kurrent.Surge.Tests.Options;

public class KurrentClientSecurityOptionsTests {
    [Test]
    public async ValueTask warns_when_certificate_based_auth_is_combined_with_disabled_transport(CancellationToken cancellationToken) {
        // Arrange: mTLS is a TLS-level mechanism — with transport security disabled, the
        // client certificate is built but never presented.
        var options = new KurrentClientSecurityOptions {
            Transport      = TransportSecurity.None,
            Authentication = new FileCertificateCredentials("/certs/client.crt", "/certs/client.key")
        };

        // Act
        var result = options.Validate();

        // Assert: a warning, not an error — the config is still usable, just misleading.
        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Problems.Any(p =>
            p.Message.Contains("Certificate-based authentication requires TLS") &&
            p.Severity == RuleSeverity.Warning
        )).IsTrue();
    }

    [Test]
    public async ValueTask does_not_warn_when_certificate_based_auth_is_combined_with_enabled_transport(CancellationToken cancellationToken) {
        // Arrange
        var options = new KurrentClientSecurityOptions {
            Transport      = TransportSecurity.Standard,
            Authentication = new FileCertificateCredentials("/certs/client.crt", "/certs/client.key")
        };

        // Act
        var result = options.Validate();

        // Assert
        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Problems.Any(p => p.Message.Contains("Certificate-based authentication requires TLS"))).IsFalse();
    }
}
