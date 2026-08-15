using System.IO.Hashing;
using System.Text;
using Aspire.Hosting.KurrentDB;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        app.Services.AddResourceConnectionProvider();

        var server = new KurrentDBServerResource(name, settings);

        var serverBuilder = app
            .AddResource(server)
            .ExcludeFromManifest();

        // #pragma warning disable ASPIREFILESYSTEM001
        // var certsPath = app.FileSystemService.TempDirectory.CreateTempSubdirectory("aspire-kurrentdb-certs" + name);
        // #pragma warning restore ASPIREFILESYSTEM001

        var certGen = settings.TlsEnabled
            ? app.AddCertificateGenerator(name, server.CertsHostPath, server)
            : null;

        for (var i = 0; i < settings.Topology.TotalNodeCount; i++) {
            var nodeBuilder = serverBuilder.AddNode(i, settings);

            if (certGen is not null)
                nodeBuilder.WaitForCompletion(certGen);
        }

        return serverBuilder;
    }

    public static IResourceBuilder<KurrentDBServerResource> AddKurrentDB(
        this IDistributedApplicationBuilder app,
        [ResourceName] string name = KurrentDBServerResource.DefaultResourceName
    ) {
        var settings = new KurrentDBSettings();
        app.Configuration.GetSection("KurrentDB").Bind(settings);
        return app.AddKurrentDB(name, settings);
    }

    public static IResourceBuilder<KurrentDBServerResource> AddKurrentDB(
        this IDistributedApplicationBuilder app,
        Action<KurrentDBSettings> configure,
        [ResourceName] string name = KurrentDBServerResource.DefaultResourceName
    ) {
        var settings = new KurrentDBSettings();
        app.Configuration.GetSection("KurrentDB").Bind(settings);
        configure(settings);
        return app.AddKurrentDB(name, settings);
    }

    #region ->> Builder Extensions <<-

    /// <summary>
    /// Configures the cluster topology with the specified number of nodes and optional read-only replicas.
    /// </summary>
    public static IResourceBuilder<KurrentDBServerResource> WithCluster(
        this IResourceBuilder<KurrentDBServerResource> builder,
        int nodes = 3,
        int replicas = 0
    ) {
        builder.Resource.Settings.WithCluster(nodes, replicas);
        return builder;
    }

    /// <summary>
    /// Configures the cluster topology with the specified number of nodes and optional read-only replicas.
    /// </summary>
    public static KurrentDBSettings WithCluster(this KurrentDBSettings settings, int nodes = 3, int replicas = 0) {
        settings.Topology.Nodes    = nodes;
        settings.Topology.Replicas = replicas;
        return settings;
    }

    /// <summary>
    /// Sets the credentials for the KurrentDB resource.
    /// </summary>
    public static IResourceBuilder<KurrentDBServerResource> WithCredentials(
        this IResourceBuilder<KurrentDBServerResource> builder,
        string userName,
        string password = "changeit"
    ) {
        builder.Resource.Settings.Credentials = new() { UserName = userName, Password = password };
        return builder;
    }

    /// <summary>
    /// Sets the license key for KurrentDB licensed features.
    /// </summary>
    public static IResourceBuilder<KurrentDBServerResource> WithLicenseKey(
        this IResourceBuilder<KurrentDBServerResource> builder,
        string licenseKey
    ) {
        builder.Resource.Settings.LicenseKey = licenseKey;
        return builder;
    }

    /// <summary>
    /// Disables TLS encryption on all nodes.
    /// </summary>
    public static IResourceBuilder<KurrentDBServerResource> RunInsecure(this IResourceBuilder<KurrentDBServerResource> builder) {
        builder.Resource.Settings.TlsEnabled = false;
        return builder;
    }

    /// <summary>
    /// Enables X.509 user certificate authentication.
    /// Requires TLS and a valid license key.
    /// </summary>
    public static IResourceBuilder<KurrentDBServerResource> WithUserCertificates(this IResourceBuilder<KurrentDBServerResource> builder) {
        builder.Resource.Settings.UserCertificatesEnabled = true;
        return builder;
    }

    /// <summary>
    /// Configures OpenTelemetry export settings for the KurrentDB nodes.
    /// </summary>
    public static IResourceBuilder<KurrentDBServerResource> WithOpenTelemetry(
        this IResourceBuilder<KurrentDBServerResource> builder,
        Action<OpenTelemetrySettings> configure
    ) {
        configure(builder.Resource.Settings.OpenTelemetry);
        return builder;
    }

    /// <summary>
    /// Configures KurrentDB server settings that map to <c>KURRENTDB_*</c> environment variables.
    /// </summary>
    public static IResourceBuilder<KurrentDBServerResource> WithServerSettings(
        this IResourceBuilder<KurrentDBServerResource> builder,
        Action<KurrentServerSettings> configure
    ) {
        configure(builder.Resource.ServerSettings);
        return builder;
    }

    #endregion // Builder Extensions

    static IResourceBuilder<KurrentDBResource> AddNode(
        this IResourceBuilder<KurrentDBServerResource> serverBuilder,
        int nodeIndex,
        KurrentDBSettings settings
    ) {
        var app    = serverBuilder.ApplicationBuilder;
        var server = serverBuilder.Resource;

        var isReplica = nodeIndex >= settings.Topology.Nodes;
        var nodeName  = isReplica ? $"{server.Name}-ro{nodeIndex + 1:00}" : $"{server.Name}-{nodeIndex + 1:00}";

        // NodeConfigurations is sized by Nodes only; replicas sit past its end and never
        // carry a priority — read-only replicas do not participate in elections.
        var priority = isReplica ? 0 : settings.Topology.NodeConfigurations[nodeIndex].Priority;
        // var node =   new KurrentDBResource(nodeName, server, priority);

        var node = server.AddNode(nodeName, priority);

        // Multi-node topologies use fixed host ports: the client-advertised port must be
        // known when the container env is built, and the proxied-endpoint port reference
        // resolves to the target port on persistent resources (Aspire 13.4), which every
        // node would then share. Single-node keeps a dynamic port — its clients connect
        // by connection string and never read gossip.
        //
        // The base port is derived from the server's resource name (deterministically hashed,
        // see GetClusterPortBase) instead of a shared constant, so a second named cluster gets
        // its own non-overlapping port block for free and the same name keeps mapping to the
        // same ports across restarts of a persistent resource, with nothing to persist.
        var clientPort = settings.Topology.TotalNodeCount > 1
            ? GetClusterPortBase(server.Name) + nodeIndex
            : (int?)null;

        var builder = app
            .AddResource(node)
            .WithHttpEndpoint(port: clientPort, targetPort: KurrentDBServerResource.DefaultHttpPort)
            .WithImage(settings.Container.Image, settings.Container.Tag)
            .WithImageRegistry(settings.Container.Registry)
            .WithImagePullPolicy(ImagePullPolicy.Default)
            .WithEnvironment(async ctx => {
                node.ConfigureEnvironment(ctx, settings, clientPort);

                // Fan out env vars set on the server builder via .WithEnvironment()
                foreach (var annotation in server.Annotations.OfType<EnvironmentCallbackAnnotation>())
                    await annotation.Callback(ctx);
            })
            .WithLifetime(settings.Lifetime)
            // Persistent resources never get a proxy in Aspire 13.4 regardless of IsProxied, so a
            // multi-node topology must bind its fixed client port straight through docker
            // (proxyless) for the gossip-advertised port to have a real listener that survives
            // app-host restarts. Single-node keeps the historical proxied setting: its clients
            // resolve the docker-published port through the connection string after start.
            .WithEndpoint("http", ep => ep.IsProxied = clientPort is null);

        if (server.TlsEnabled)
            builder
                .WithEndpoint(
                    "http", callback: ep => {
                        ep.UriScheme  = "https";
                        ep.TlsEnabled = true;
                    })
                .WithBindMount(server.CertsHostPath, "/etc/kurrentdb/certs", true);

        var healthCheckKey = $"{node.Name}_check";

        app
            .Services
            .AddHealthChecks()
            .Add(
                new(
                    healthCheckKey,
                    factory: sp => {
                        var subscriber    = sp.GetRequiredService<ResourceConnectionProvider>();
                        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                        return new KurrentDBHealthCheck(node.Name, subscriber, loggerFactory);
                    },
                    HealthStatus.Unhealthy,
                    []));

        builder
            .WithHealthCheck(healthCheckKey)
            .WithAdminCommands();

        return builder;

        static int GetClusterPortBase(string resourceName, int portRangeStart = 20000) {
            const int bucketSize  = 16;   // enough headroom for up to a 16-node cluster
            const int bucketCount = 1000;

            var bucket = XxHash3.HashToUInt64(Encoding.UTF8.GetBytes(resourceName)) % bucketCount;
            var port   = portRangeStart + (int)bucket * bucketSize;
            
            return port;
        }
    }

    public static IResourceBuilder<KurrentDBServerResource> WithClusterHealthCheck(this IResourceBuilder<KurrentDBServerResource> serverBuilder) {
        var app    = serverBuilder.ApplicationBuilder;
        var server = serverBuilder.Resource;

        app.Services
            .AddHttpClient(KurrentDBClusterHealthCheck.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true });

        var healthCheckKey = $"{server.Name}_cluster_check";

        app.Services
            .AddHealthChecks()
            .AddTypeActivatedCheck<KurrentDBClusterHealthCheck>(healthCheckKey, server);

        return serverBuilder.WithHealthCheck(healthCheckKey);
    }

    static IResourceBuilder<ExecutableResource> AddCertificateGenerator(
        this IDistributedApplicationBuilder app,
        string name, string certsHostPath,
        KurrentDBServerResource server
    ) {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "gencert.sh");
        var certGen    = app.AddExecutable($"{name}-certs", "/bin/bash", ".");

        certGen.WithArgs(context => {
            var dnsNames = string.Join(
                ",", server
                    .Nodes
                    .Select(r => r.Name)
                    .Distinct(StringComparer.Ordinal)
                    .Prepend("localhost")
                    .Prepend("eventstoredb-node"));

            context.Args.Add(scriptPath);
            context.Args.Add("--output");
            context.Args.Add(certsHostPath);
            context.Args.Add("--dns");
            context.Args.Add(dnsNames);
            context.Args.Add("--user-certs");
            context.Args.Add("admin");
        });

        return certGen;
    }
}

static class NodeConfiguration {
    public static readonly IReadOnlyDictionary<string, string?> Insecure = new Dictionary<string, string?> {
        ["KURRENTDB_TELEMETRY_OPTOUT"]               = "true",
        ["KURRENTDB_ALLOW_UNKNOWN_OPTIONS"]          = "false",
        ["KURRENTDB_MEM_DB"]                         = "false",
        ["KURRENTDB_DISABLE_LOG_FILE"]               = "true",
        ["KURRENTDB_LOG_LEVEL"]                      = "Debug",
        ["KURRENTDB_ENABLE_ATOM_PUB_OVER_HTTP"]      = "true",
        ["KURRENTDB_RUN_PROJECTIONS"]                = "All",
        ["KURRENTDB_START_STANDARD_PROJECTIONS"]     = "true",
        ["KURRENTDB_NODE_PORT"]                      = "2113",
        ["KURRENTDB_INSECURE"]                       = "true",
        ["KURRENTDB_SECONDARY_INDEXING__ENABLED"]    = "true",
        ["KURRENTDB_OPEN_TELEMETRY__OTLP__ENDPOINT"] = null,
        ["KURRENTDB_OPEN_TELEMETRY__LOGS__ENABLED"]  = "false"
    };

    public static readonly IReadOnlyDictionary<string, string?> Secure = new Dictionary<string, string?>(Insecure) {
        ["KURRENTDB_INSECURE"]                       = "false",
        ["KURRENTDB_TRUSTED_ROOT_CERTIFICATES_PATH"] = "/etc/kurrentdb/certs/ca",
        ["KURRENTDB_CERTIFICATE_FILE"]               = "/etc/kurrentdb/certs/node/node.crt",
        ["KURRENTDB_CERTIFICATE_PRIVATE_KEY_FILE"]   = "/etc/kurrentdb/certs/node/node.key",
        // The same node cert is shared across every node, so no per-node hostname can be a
        // SAN. Multi-node peer validation instead matches this reserved name against the
        // cert's SANs (server peer-cert matching is SAN-only when SANs exist, CN is ignored),
        // which is why AddCertificateGenerator's DNS list must also carry it.
        ["KURRENTDB_CERTIFICATE_RESERVED_NODE_COMMON_NAME"] = "eventstoredb-node"
    };
}