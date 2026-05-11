using Kurrent.Sdk.Testkit.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting;

public static class KurrentDBBuilderExtensions {
    public static IResourceBuilder<KurrentDBServerResource> AddKurrentDB(
        this IDistributedApplicationBuilder app,
        [ResourceName] string name,
        KurrentDBSettings settings
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        settings.EnsureValid();

        var userNameParameter = new ParameterResource($"{name}-username", _ => settings.UserName, secret: true);
        var passwordParameter = new ParameterResource($"{name}-password", _ => settings.Password, secret: true);

        var connectionProvider = new ResourceConnectionProvider<KurrentDBResource>();
        app.Services.TryAddSingleton(connectionProvider);

        var topology  = settings.Topology;
        var nodeCount = topology.Nodes + topology.Replicas;
        var server    = new KurrentDBServerResource(name, userNameParameter, passwordParameter, settings.TlsEnabled);

        var serverBuilder = app.AddResource(server).ExcludeFromManifest();

        for (var i = 1; i <= nodeCount; i++) {
            var isReplica = i > topology.Nodes;
            var nodeName  = isReplica ? $"{name}-ro{i:00}" : $"{name}-{i:00}";
            var priority  = topology.NodeConfigurations[i - 1].Priority;

            server.AddNode(nodeName, priority);
        }

        IResourceBuilder<ExecutableResource>? certGen = null;
        var certsHostPath = Path.Combine(AppContext.BaseDirectory, "certs", name);

        if (settings.TlsEnabled)
            certGen = app.AddCertificateGenerator(name, certsHostPath, server.Nodes);

        foreach (var node in server.Nodes) {
            var builder = app
                .AddResource(node)
                .WithHttpEndpoint(targetPort: KurrentDBServerResource.DefaultHttpPort)
                .WithImage(KurrentDBContainerImageTags.Image, settings.Tag)
                .WithImageRegistry(KurrentDBContainerImageTags.Registry)
                .WithEnvironment(node.ConfigureEnvironment)
                .WithLifetime(ContainerLifetime.Persistent);

            if (certGen is not null) {
                builder
                    .WithEndpoint("http", ep => {
                        ep.UriScheme  = "https";
                        ep.TlsEnabled = true;
                    })
                    .WithBindMount(certsHostPath, "/etc/kurrentdb/certs", isReadOnly: true)
                    .WaitForCompletion(certGen);
            }

            connectionProvider.Register(app, node);

            var healthCheckKey = $"{node.Name}_check";

            app.Services
                .AddHealthChecks()
                .Add(new HealthCheckRegistration(
                    healthCheckKey,
                    sp => {
                        var provider      = sp.GetRequiredService<ResourceConnectionProvider<KurrentDBResource>>();
                        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                        return new KurrentDBHealthCheck(node.Name, provider, loggerFactory);
                    },
                    failureStatus: HealthStatus.Unhealthy,
                    tags: []));

            builder.WithHealthCheck(healthCheckKey);
        }

        return serverBuilder;
    }

    public static IResourceBuilder<KurrentDBServerResource> AddKurrentDB(
        this IDistributedApplicationBuilder app,
        Action<KurrentDBSettings> configure,
        [ResourceName] string name = KurrentDBServerResource.DefaultResourceName
    ) {
        var settings = new KurrentDBSettings();
        configure(settings);
        return app.AddKurrentDB(name, settings);
    }

    public static IResourceBuilder<KurrentDBServerResource> AddKurrentDB(
        this IDistributedApplicationBuilder app
    ) => app.AddKurrentDB(KurrentDBServerResource.DefaultResourceName, KurrentDBSettings.SingleNode);

    static IResourceBuilder<ExecutableResource> AddCertificateGenerator(
        this IDistributedApplicationBuilder app,
        string name,
        string certsHostPath,
        IReadOnlyList<KurrentDBResource> nodes
    ) {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "gencert.sh");

        var dnsNames = string.Join(",", nodes
            .Select(r => r.Name)
            .Distinct(StringComparer.Ordinal)
            .Prepend("localhost"));

        var certGen = app.AddExecutable(
            name: $"{name}-certs", command: "/bin/bash", workingDirectory: ".",
            args: [scriptPath, "--output", certsHostPath, "--dns", dnsNames, "--user-certs", "admin"]);

        return certGen;
    }
}

static class NodeConfiguration {
    public static readonly IReadOnlyDictionary<string, string?> Insecure = new Dictionary<string, string?> {
        ["KURRENTDB_TELEMETRY_OPTOUT"]               = "true",
        ["KURRENTDB_ALLOW_UNKNOWN_OPTIONS"]          = "true",
        ["KURRENTDB_MEM_DB"]                         = "false",
        ["KURRENTDB_DISABLE_LOG_FILE"]               = "true",
        ["KURRENTDB_LOG_LEVEL"]                      = "Debug",
        ["KURRENTDB_ENABLE_ATOM_PUB_OVER_HTTP"]      = "true",
        ["KURRENTDB_RUN_PROJECTIONS"]                = "All",
        ["KURRENTDB_START_STANDARD_PROJECTIONS"]     = "true",
        ["KURRENTDB_NODE_PORT"]                      = "2113",
        ["KURRENTDB_INSECURE"]                       = "true",
        ["KURRENTDB_USER_CERTIFICATES__ENABLED"]     = "false",
        ["KURRENTDB_SECONDARY_INDEXING__ENABLED"]    = "true",
        ["KURRENTDB_OPEN_TELEMETRY__OTLP__ENDPOINT"] = null,
        ["KURRENTDB_OPEN_TELEMETRY__LOGS__ENABLED"]  = "false",
    };

    public static readonly IReadOnlyDictionary<string, string?> Secure = new Dictionary<string, string?>(Insecure) {
        ["KURRENTDB_INSECURE"]                       = "false",
        ["KURRENTDB_TRUSTED_ROOT_CERTIFICATES_PATH"] = "/etc/kurrentdb/certs/ca",
        ["KURRENTDB_CERTIFICATE_FILE"]               = "/etc/kurrentdb/certs/node/node.crt",
        ["KURRENTDB_CERTIFICATE_PRIVATE_KEY_FILE"]   = "/etc/kurrentdb/certs/node/node.key",
        ["KURRENTDB_OPEN_TELEMETRY__OTLP__ENDPOINT"] = null,
        ["KURRENTDB_OPEN_TELEMETRY__LOGS__ENABLED"]  = "false",
    };
}
