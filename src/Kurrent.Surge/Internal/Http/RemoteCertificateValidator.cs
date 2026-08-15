using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Kurrent.Http;

/// <summary>
/// Validates the server's certificate against a custom CA certificate on SSL/TLS connections.
/// </summary>
/// <param name="CaCertificate">
/// The custom CA certificate used to validate the server's certificate.
/// This should be a valid X509Certificate2 object representing the CA that signed the server's certificate.
/// </param>
/// <param name="Logger">
/// The logger instance used to log errors and information during the validation process.
/// </param>
sealed record RemoteCertificateValidator(X509Certificate2 CaCertificate, ILogger Logger) {
    bool Callback(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors) {
        if (errors != SslPolicyErrors.None)
            Logger.LogError("SSL Policy Errors: {SslPolicyErrors}", errors);

        if (certificate is null || chain is null) {
            Logger.LogError("Certificate or chain is null. Ensure the certificate is properly loaded.");
            return false;
        }

        if (certificate is not X509Certificate2 peerCertificate) {
            try {
                peerCertificate = new(certificate);
            }
            catch (Exception ex) {
                Logger.LogError(
                    ex,
                    "Failed to convert certificate to X509Certificate2 format. " +
                    "Ensure the certificate is in a valid format (e.g., PEM, CRT, CER)."
                );

                return false;
            }
        }

        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(CaCertificate);

        var chainIsValid = chain.Build(peerCertificate);

        if (!chainIsValid)
            Logger.LogError("X.509 Chain Status: {ChainStatusInformation}", chain.ChainStatus);

        return chainIsValid;
    }

    public static implicit operator RemoteCertificateValidationCallback(RemoteCertificateValidator _) => _.Callback;
}
