using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Kurrent.Surge.Tests.Experimental.LoadBalancing.FakeCluster;

/// <summary>
/// An ephemeral CA plus a CA-signed server certificate (SAN: 127.0.0.1 / localhost) and a
/// CA-signed client certificate, generated in-process for the harness's TLS/mTLS tests.
/// Never touches the system certificate store.
/// </summary>
public sealed class FakeClusterTls : IDisposable {
    FakeClusterTls(X509Certificate2 ca, X509Certificate2 server, X509Certificate2 client, RSA clientKey) {
        CaCertificate     = ca;
        ServerCertificate = server;
        ClientCertificate = client;
        ClientPrivateKey  = clientKey;
    }

    public X509Certificate2 CaCertificate     { get; }
    public X509Certificate2 ServerCertificate { get; }
    public X509Certificate2 ClientCertificate { get; }

    RSA ClientPrivateKey { get; }

    const string ServerAuthenticationEku = "1.3.6.1.5.5.7.3.1";
    const string ClientAuthenticationEku = "1.3.6.1.5.5.7.3.2";

    public static FakeClusterTls Create() {
        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter  = DateTimeOffset.UtcNow.AddDays(1);

        using var ca = CreateCa(notBefore, notAfter);

        using var server = SignLeaf(ca, "FakeCluster Node", ServerAuthenticationEku, notBefore, notAfter, san => {
            san.AddIpAddress(IPAddress.Loopback);
            san.AddDnsName("localhost");
        }, out var serverKey);

        using var client = SignLeaf(ca, "FakeCluster Client", ClientAuthenticationEku, notBefore, notAfter, san => san.AddDnsName("fake-cluster-client"), out var clientKey);

        using (serverKey) {
            return new(Reimport(ca), Reimport(server), new(client.Export(X509ContentType.Cert)), clientKey);
        }
    }

    public void ExportCaPem(string path) =>
        File.WriteAllText(path, CaCertificate.ExportCertificatePem());

    public void ExportClientPem(string certPath, string keyPath) {
        File.WriteAllText(certPath, ClientCertificate.ExportCertificatePem());
        File.WriteAllText(keyPath, ClientPrivateKey.ExportPkcs8PrivateKeyPem());
    }

    public void Dispose() {
        CaCertificate.Dispose();
        ServerCertificate.Dispose();
        ClientCertificate.Dispose();
        ClientPrivateKey.Dispose();
    }

    static X509Certificate2 CreateCa(DateTimeOffset notBefore, DateTimeOffset notAfter) {
        using var key = RSA.Create(2048);

        var request = new CertificateRequest("CN=FakeCluster CA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));

        return request.CreateSelfSigned(notBefore, notAfter);
    }

    static X509Certificate2 SignLeaf(
        X509Certificate2 issuer, string subjectName, string enhancedKeyUsageOid, DateTimeOffset notBefore, DateTimeOffset notAfter,
        Action<SubjectAlternativeNameBuilder> configureSan, out RSA privateKey
    ) {
        privateKey = RSA.Create(2048);

        var request = new CertificateRequest($"CN={subjectName}", privateKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new(enhancedKeyUsageOid) }, false));

        var san = new SubjectAlternativeNameBuilder();
        configureSan(san);
        request.CertificateExtensions.Add(san.Build());

        var serialNumber = RandomNumberGenerator.GetBytes(16);

        using var certOnly = request.Create(issuer, notBefore, notAfter, serialNumber);

        return certOnly.CopyWithPrivateKey(privateKey);
    }

    // .NET certificates produced via CopyWithPrivateKey carry an ephemeral key that SslStream
    // rejects on some platforms; round-tripping through PKCS12 fixes it cross-platform.
    static X509Certificate2 Reimport(X509Certificate2 certificate) =>
        X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), password: null, X509KeyStorageFlags.Exportable);
}
