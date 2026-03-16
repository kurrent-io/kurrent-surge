using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Model.Builders;
using Kurrent.Testing.Containers.FluentDocker;
using Microsoft.Extensions.Configuration;
using TUnit.Core.Interfaces;
using static System.StringComparer;

namespace Kurrent.Testing.Containers.KurrentDB;

[PublicAPI]
public class KurrentDBTestContainer : TestContainer, IAsyncInitializer, IAsyncDisposable {
    public const int    DefaultPort     = 2113;
    public const string DefaultUsername = "admin";
    public const string DefaultPassword = "changeit";

    // static string DefaultImage => "docker.kurrent.io/kurrent-latest/kurrentdb:latest";   // stable release
    // static string DefaultImage => "docker.kurrent.io/kurrent-latest/kurrentdb:lts";      // long-term support
    static string DefaultImage => "docker.kurrent.io/kurrent-staging/kurrentdb:nightly";    // nightly builds
    // static string DefaultImage => "docker.kurrent.io/kurrent-staging/kurrentdb:ci";      // CI builds

    public KurrentDBTestContainer(Dictionary<string, string?> settings, string? image = null) : base(image ?? DefaultImage) {
        ActiveConfiguration = KurrentDBConfiguration.FromContext(settings).ToDictionary();

        IsSecure = ActiveConfiguration.TryGetValue("KURRENTDB_INSECURE", out var value) && value == "false";

        // TODO SS: these connection strings need a cleanup
        if (IsSecure) {
            AuthenticatedConnectionString = "kurrentdb://admin:changeit@localhost:2113/?tls=true&tlsVerifyCert=false";
            AnonymousConnectionString     = "kurrentdb://localhost:2113/?tls=true&tlsVerifyCert=false";
        }
        else {
            AuthenticatedConnectionString = "kurrentdb://admin:changeit@localhost:2113/?tls=false";
            AnonymousConnectionString     = "kurrentdb://localhost:2113/?tls=false";
        }
    }

    public KurrentDBTestContainer(string image)
        : this(KurrentDBConfiguration.Insecure.ToDictionary(), image) { }

    public KurrentDBTestContainer()
        : this(KurrentDBConfiguration.Insecure.ToDictionary()) { }

    public Dictionary<string, string?> ActiveConfiguration { get; }

    public string AuthenticatedConnectionString { get; }
    public string AnonymousConnectionString     { get; }

    public bool IsSecure { get; }

    protected override ContainerBuilder ConfigureContainer(ContainerBuilder builder) {
        var healthCheckCommand = IsSecure
            ? "curl -u admin:changeit --cacert /etc/kurrentdb/certs/ca/ca.crt https://localhost:2113/health/live"
            : "curl -o - -I http://admin:changeit@localhost:2113/health/live";

        if (IsSecure) {
            var certsPath = Path.Combine(Environment.CurrentDirectory, "certs");
            CheckCertificates(certsPath); builder = builder.MountVolume(certsPath, "/etc/kurrentdb/certs", MountType.ReadOnly);
        }

        return builder
            //.UseNetwork(Network)
            .WithPublicEndpointResolver()
            .WithEnvironment(ActiveConfiguration)
            .ExposePort(DefaultPort, DefaultPort)
            .KeepContainer().KeepRunning().ReuseIfExists()
            .WaitUntilReadyWithConstantBackoff(1_000, 60, service => {
                var output = service.ExecuteCommand(healthCheckCommand);
                if (!output.Success) throw new Exception(output.Error);
            });

        static void CheckCertificates(string certificatePath) {
            var certificateFiles = new[] {
                Path.Combine("ca", "ca.crt"),
                Path.Combine("ca", "ca.key"),
                Path.Combine("node", "node.crt"),
                Path.Combine("node", "node.key")
            }.Select(path => Path.Combine(certificatePath, path));

            foreach (var file in certificateFiles)
                if (!File.Exists(file))
                    throw new InvalidOperationException(
                        $"Could not locate the certificates file {file} needed to run KurrentDB. Please run the 'gencert' tool at the root of the repository."
                    );
        }
    }

    public async Task InitializeAsync() => await Start().ConfigureAwait(false);
}

public static class KurrentDBConfiguration {
    /// <summary>
    /// Default insecure configuration for KurrentDB.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string?> Insecure = new Dictionary<string, string?> {
        ["KURRENTDB_TELEMETRY_OPTOUT"]           = "true",
        ["KURRENTDB_ALLOW_UNKNOWN_OPTIONS"]      = "true",
        ["KURRENTDB_MEM_DB"]                     = "false",
        ["KURRENTDB_DISABLE_LOG_FILE"]           = "true",
        ["KURRENTDB_LOG_LEVEL"]                  = "Debug",
        ["KURRENTDB_ENABLE_ATOM_PUB_OVER_HTTP"]  = "true",
        ["KURRENTDB_RUN_PROJECTIONS"]            = "All",
        ["KURRENTDB_START_STANDARD_PROJECTIONS"] = "true",
        ["KURRENTDB_NODE_PORT"]                  = "2113",
        ["KURRENTDB_INSECURE"]                   = "true",
        ["KURRENTDB_USER_CERTIFICATES__ENABLED"] = "false",

        ["OTEL_LOG_LEVEL"]                       = "debug",

        // TODO: Need License key environment variable
        // ["KURRENTDB__OPENTELEMETRY__LOGS__ENABLED"]  = "true",
        // ["KURRENTDB__OPENTELEMETRY__OTLP__ENDPOINT"] = "http://localhost:4317",

        // ["KURRENTDB__OPENTELEMETRY__LOGS__ENABLED"]  = "true",
        // ["KURRENTDB__OPENTELEMETRY__OTLP__ENDPOINT"] = "http://host.docker.internal:4317",
        // ["OPENTELEMETRY__LOGS__ENABLED"]             = "true",
        // ["OPENTELEMETRY__OTLP__ENDPOINT"]            = "http://host.docker.internal:4317",
        // ["KURRENTDB_OPENTELEMETRY__LOGS__ENABLED"]   = "true",
        // ["KURRENTDB_OPENTELEMETRY__OTLP__ENDPOINT"]  = "http://host.docker.internal:4317",
        // ["KURRENTDB_OPENTELEMETRY__OTLP__ENDPOINT"] = "http://localhost:4317",
        // ["KURRENTDB_OPENTELEMETRY__OTLP__ENDPOINT"] = "http://host.docker.internal:4317",

    };

    /// <summary>
    /// Default secure configuration for KurrentDB.
    /// This configuration is used when TLS is enabled and user certificates are required.
    /// It includes settings for trusted root certificates and node certificates.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string?> Secure = new Dictionary<string, string?>(Insecure) {
        ["KURRENTDB_INSECURE"]                       = "false",
        ["KURRENTDB_USER_CERTIFICATES__ENABLED"]     = "true",
        ["KURRENTDB_TRUSTED_ROOT_CERTIFICATES_PATH"] = "/etc/kurrentdb/certs/ca",
        ["KURRENTDB_CERTIFICATE_FILE"]               = "/etc/kurrentdb/certs/node/node.crt",
        ["KURRENTDB_CERTIFICATE_PRIVATE_KEY_FILE"]   = "/etc/kurrentdb/certs/node/node.key",
    };

    /// <summary>
    /// Combines the default insecure configuration with any overrides specified in the current execution context configuration.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> FromContext(Dictionary<string, string?>? baseConfiguration = null) {
        var fromContext = TestkitExecutionContext.Configuration.AsEnumerable()
            .Where(x => x.Key.StartsWith("KURRENTDB_"))
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .OrderBy(x => x.Key)
            .ToDictionary(x => x.Key, x => x.Value);

        return (baseConfiguration ?? Insecure)
            .Concat(fromContext).GroupBy(x => x.Key)
            .ToDictionary(x => x.Key, x => x.Last().Value, OrdinalIgnoreCase);
    }
}
