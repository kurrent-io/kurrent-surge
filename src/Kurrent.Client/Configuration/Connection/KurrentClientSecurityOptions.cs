using System.Security.Cryptography.X509Certificates;
using Kurrent.Validation;

namespace Kurrent.Client;

/// <summary>
/// Comprehensive security configuration for KurrentDB client connections.
/// </summary>
/// <remarks>
/// <para>
/// Controls all security-related aspects of client connections including TLS/SSL settings,
/// client authentication with certificates or username/password credentials.
/// </para>
/// <para>
/// Several predefined configurations are available as static properties to cover common scenarios:
/// <see cref="Default"/>, <see cref="Insecure"/>, <see cref="MutualTls"/>.
/// </para>
/// </remarks>
[PublicAPI]
public record KurrentClientSecurityOptions : SelfValidating<KurrentClientSecurityOptions, KurrentClientSecurityOptions.Validator> {
    /// <summary>
    /// Transport layer security settings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Controls whether TLS is enabled and how certificates are verified.
    /// </para>
    /// <para>
    /// When null, uses the default TLS settings (standard TLS with system trust store).
    /// </para>
    /// </remarks>
    public TransportSecurity Transport { get; set; } = TransportSecurity.Standard;

    /// <summary>
    /// Client authentication credentials.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Specifies how the client authenticates with the server.
    /// </para>
    /// <para>
    /// Can be certificate-based, username/password, or none.
    /// </para>
    /// </remarks>
    public ClientCredentials Authentication { get; set; } = ClientCredentials.None;

    /// <summary>
    /// Default security settings with TLS enabled and server certificate verification.
    /// No client authentication is configured.
    /// </summary>
    /// <returns>
    /// A new <see cref="KurrentClientSecurityOptions"/> instance with secure-by-default settings.
    /// </returns>
    public static KurrentClientSecurityOptions Default =>
        new() {
            Transport      = TransportSecurity.Standard,
            Authentication = ClientCredentials.None
        };

    /// <summary>
    /// Insecure settings with TLS disabled. Only use for development.
    /// </summary>
    /// <returns>
    /// A new <see cref="KurrentClientSecurityOptions"/> instance with TLS disabled.
    /// </returns>
    public static KurrentClientSecurityOptions Insecure =>
        new() {
            Transport      = TransportSecurity.None,
            Authentication = ClientCredentials.None
        };

    /// <summary>
    /// Mutual TLS authentication settings with client certificates from file paths.
    /// </summary>
    /// <param name="clientCertPath">Path to client certificate file</param>
    /// <param name="clientKeyPath">Path to client private key file</param>
    /// <param name="rootCaPath">Optional path to root CA certificate</param>
    /// <returns>
    /// A new <see cref="KurrentClientSecurityOptions"/> instance configured for mutual TLS authentication.
    /// </returns>
    public static KurrentClientSecurityOptions MutualTls(string clientCertPath, string clientKeyPath, string? rootCaPath = null) =>
        new() {
            Transport      = rootCaPath is not null ? TransportSecurity.Certificate(rootCaPath) : TransportSecurity.Standard,
            Authentication = new FileCertificateCredentials(clientCertPath, clientKeyPath)
        };

    /// <summary>
    /// Mutual TLS authentication settings using X509Certificate2 certificates.
    /// </summary>
    /// <param name="clientCertificate">Client certificate</param>
    /// <param name="rootCaCertificate">Optional root CA certificate</param>
    /// <returns>
    /// A new <see cref="KurrentClientSecurityOptions"/> instance configured for mutual TLS authentication with certificate objects.
    /// </returns>
    public static KurrentClientSecurityOptions MutualTls(X509Certificate2 clientCertificate, X509Certificate2? rootCaCertificate = null) =>
        new() {
            Transport      = rootCaCertificate is not null ? TransportSecurity.Certificate(rootCaCertificate) : TransportSecurity.Standard,
            Authentication = new X509CertificateCredentials(clientCertificate)
        };

    /// <summary>
    /// Basic authentication settings with username/password. Enables TLS for secure transmission.
    /// </summary>
    /// <param name="username">Username for authentication</param>
    /// <param name="password">Password for authentication</param>
    /// <returns>
    /// A new <see cref="KurrentClientSecurityOptions"/> instance configured for username/password authentication.
    /// </returns>
    public static KurrentClientSecurityOptions Basic(string username, string password) =>
        new() {
            Transport      = TransportSecurity.Standard, // Always use TLS with password authentication
            Authentication = new BasicCredentials(username, password)
        };

    public class Validator : Validator<Validator, KurrentClientSecurityOptions> {
        public Validator() {
            // Transport security validation
            When(options => options.Authentication.IsBasicCredentials, () => {
                RuleFor(options => options.Transport.IsEnabled)
                    .Equal(true)
                    .WithMessage("Using basic authentication without TLS is not secure.")
                    .WithSeverity(RuleSeverity.Warning);
            });

            // Certificate path validation for Authentication
            When(options => options.Authentication.IsFileCertificateCredentials, () => {
                RuleFor(options => options.Authentication.AsFileCertificateCredentials.CertificatePath)
                    .Must(Path.IsPathRooted, "Relative certificate path may not resolve correctly at runtime.")
                    .WithMessage(options => $"Relative certificate path may not resolve correctly at runtime: {options.Authentication.AsFileCertificateCredentials.CertificatePath}")
                    .WithSeverity(RuleSeverity.Warning);

                RuleFor(options => options.Authentication.AsFileCertificateCredentials.KeyPath)
                    .Must(Path.IsPathRooted, "Relative key path may not resolve correctly at runtime.")
                    .WithMessage(options => $"Relative key path may not resolve correctly at runtime: {options.Authentication.AsFileCertificateCredentials.KeyPath}")
                    .WithSeverity(RuleSeverity.Warning);
            });

            // Certificate path validation for Transport
            When(options => options.Transport.IsFileCertificateTls, () => {
                RuleFor(options => options.Transport.AsFileCertificateTls.CaPath)
                    .Must(Path.IsPathRooted, "Relative certificate path may not resolve correctly at runtime.")
                    .WithMessage(options => $"Relative certificate path may not resolve correctly at runtime: {options.Transport.AsFileCertificateTls.CaPath}")
                    .WithSeverity(RuleSeverity.Warning);
            });
        }
    }
}
