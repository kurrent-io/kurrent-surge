using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Bogus;

namespace Kurrent.Surge.Tests.Options;

public class FileCertificateCredentialsTests {
    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask loads_certificate_with_encrypted_private_key_when_password_is_provided(CancellationToken cancellationToken) {
        // Arrange
        var password = Faker.Internet.Password();

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=kurrent-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        var expectedSubject = cert.Subject;

        var certPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.crt");
        var keyPath  = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.key");

        var encryptedKeyPem = rsa.ExportEncryptedPkcs8PrivateKeyPem(
            password, new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 100_000));

        await File.WriteAllTextAsync(certPath, cert.ExportCertificatePem(), cancellationToken);
        await File.WriteAllTextAsync(keyPath, encryptedKeyPem, cancellationToken);

        try {
            // Act
            var credentials = new FileCertificateCredentials(certPath, keyPath, password);
            using var loaded = credentials.Certificate;

            // Assert
            await Assert.That(loaded.HasPrivateKey).IsTrue();
            await Assert.That(loaded.Subject).IsEqualTo(expectedSubject);
        }
        finally {
            File.Delete(certPath);
            File.Delete(keyPath);
        }
    }

    [Test]
    public async ValueTask loads_certificate_with_unencrypted_private_key_without_password(CancellationToken cancellationToken) {
        // Arrange
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=kurrent-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        var expectedSubject = cert.Subject;

        var certPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.crt");
        var keyPath  = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.key");

        await File.WriteAllTextAsync(certPath, cert.ExportCertificatePem(), cancellationToken);
        await File.WriteAllTextAsync(keyPath, rsa.ExportPkcs8PrivateKeyPem(), cancellationToken);

        try {
            // Act
            var credentials = new FileCertificateCredentials(certPath, keyPath);
            using var loaded = credentials.Certificate;

            // Assert
            await Assert.That(loaded.HasPrivateKey).IsTrue();
            await Assert.That(loaded.Subject).IsEqualTo(expectedSubject);
        }
        finally {
            File.Delete(certPath);
            File.Delete(keyPath);
        }
    }
}
