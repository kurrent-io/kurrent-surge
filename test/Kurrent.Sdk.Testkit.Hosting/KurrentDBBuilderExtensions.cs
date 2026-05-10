using System.Text;
using Aspire.Hosting.Lifecycle;
using Kurrent.Client;
using Kurrent.Sdk.Testkit.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting;

public static class KurrentDBBuilderExtensions {
    public static IDistributedApplicationBuilder AddKurrentDB(
        this IDistributedApplicationBuilder app,
        [ResourceName] string name,
        KurrentDBSettings settings
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        settings.EnsureValid();

        var connectionProvider = new ResourceConnectionProvider<KurrentDBResource>();
        app.Services.TryAddSingleton(connectionProvider);
        
        // app.Services.AddEventingSubscriber<ResourceConnectionSubscriber>();

        var topology = settings.Topology;
       
        var (nodes, gossipSeeds) = CreateResources(name, topology.Nodes, topology.Replicas, settings.UserName, settings.Password);

        KurrentDBClusterResource? cluster = null;

        if (topology.Nodes >= 3) {
            cluster = new(name, nodes);
            app.AddResource(cluster).ExcludeFromManifest();
        }

        for (var i = 0; i < nodes.Count; i++) {
            var node     = nodes[i];
            var priority = topology.NodeConfigurations[i].Priority;

            var builder = app
                .AddResource(node)
                .WithHttpEndpoint(targetPort: KurrentDBResource.DefaultHttpPort)
                //.WithImageRegistry(settings.ContainerImage.Registry)
                .WithImage(settings.Image, settings.Tag)
                .WithEnvironment(ctx => ConfigureClusterNode(
                    ctx, gossipSeeds, topology.Nodes,
                    priority, settings.TlsEnabled))
                .WithLifetime(ContainerLifetime.Persistent);

            if (cluster is not null)
                builder.WithParentRelationship(cluster);

            if (settings.TlsEnabled)
                builder.ConfigureCertificates(nodes);

            connectionProvider.Register(app, node);

            var healthCheckKey = $"{node.Name}_check";
            
            // app.Services.TryAddSingleton(
            //     sp => new KurrentDBHealthCheck(node.Name, sp.GetRequiredService<ResourceConnectionProvider<KurrentDBResource>>()));
           
            // app.Services
            //     .AddHealthChecks()
            //     // .AddCheck<KurrentDBHealthCheck>(healthCheckKey)
            //     // .AddTypeActivatedCheck<KurrentDBHealthCheck>(healthCheckKey, node.Name)
            //     .Add(new HealthCheckRegistration(
            //         node.Name,
            //         sp => {
            //             var provider      = sp.GetRequiredService<ResourceConnectionSubscriber>();
            //             var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            //             return new KurrentDBHealthCheck(node.Name, provider, loggerFactory);
            //         },
            //         failureStatus: null, tags: null
            //     ));
            
            // app.Services
            //     .AddHealthChecks()
            //     // .AddCheck<KurrentDBHealthCheck>(healthCheckKey)
            //     // .AddTypeActivatedCheck<KurrentDBHealthCheck>(healthCheckKey, node.Name)
            //     .Add(new HealthCheckRegistration(
            //         healthCheckKey,
            //         sp => {
            //             var provider = sp.GetRequiredService<ResourceConnectionProvider<KurrentDBResource>>();
            //             var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            //             return new KurrentDBHealthCheck(node.Name, provider, loggerFactory);
            //         },
            //         failureStatus: null, tags: null
            //     ));
            
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

        return app;

        static (List<KurrentDBResource> resources, string gossipSeeds) CreateResources(string baseName, int nodes, int replicas, string userName, string password) {
            var isCluster   = nodes >= 3;
            var clusterSize = nodes + replicas;
            var resources   = new List<KurrentDBResource>(clusterSize);
            var gossipSeeds = new StringBuilder();

            for (var i = 1; i <= clusterSize; i++) {
                var isReadOnlyReplica = i > nodes;

                var nodeName = isReadOnlyReplica
                    ? $"{baseName}-ro{i:00}"
                    : isCluster
                        ? $"{baseName}-{i:00}"
                        : baseName;

                resources.Add(new(nodeName, userName, password));
                gossipSeeds.Append($"{nodeName}:{KurrentDBResource.DefaultHttpPort}{(i < clusterSize ? ", " : "")}");
            }

            return (resources, gossipSeeds.ToString());
        }

        static void ConfigureClusterNode(EnvironmentCallbackContext ctx, string gossipSeeds, int clusterSize, int nodePriority, bool tlsEnabled) {
            var node = ctx.Resource.As<KurrentDBResource>();

            var variables = tlsEnabled
                ? NodeConfiguration.Secure
                : NodeConfiguration.Insecure;

            foreach (var (key, value) in variables) {
                if (value is not null)
                    ctx.EnvironmentVariables[key] = value;
                else
                    ctx.EnvironmentVariables.Remove(key);
            }

            ctx.EnvironmentVariables["KURRENTDB_CLUSTER_SIZE"]                     = clusterSize.ToString();
            ctx.EnvironmentVariables["KURRENTDB_DISCOVER_VIA_DNS"]                 = "false";
            ctx.EnvironmentVariables["KURRENTDB_GOSSIP_SEED"]                      = gossipSeeds;
            ctx.EnvironmentVariables["KURRENTDB_NODE_HOST_ADVERTISE_AS"]           = node.Name;
            ctx.EnvironmentVariables["KURRENTDB_REPLICATION_HOST_ADVERTISE_AS"]    = node.Name;
            ctx.EnvironmentVariables["KURRENTDB_ADVERTISE_HOST_TO_CLIENT_AS"]      = "localhost";
            ctx.EnvironmentVariables["KURRENTDB_ADVERTISE_NODE_PORT_TO_CLIENT_AS"] = ReferenceExpression.Create($"{node.PrimaryEndpoint.Property(EndpointProperty.Port)}");
            ctx.EnvironmentVariables["KURRENTDB_READ_ONLY_REPLICA"]                = node.IsReadOnlyReplica.ToString().ToLowerInvariant();

            if (nodePriority != 0)
                ctx.EnvironmentVariables["KURRENTDB_NODE_PRIORITY"] = nodePriority.ToString();
        }
    }

    public static IDistributedApplicationBuilder AddKurrentDB(
        this IDistributedApplicationBuilder app,
        Action<KurrentDBSettings> configure,
        [ResourceName] string name = KurrentDBResource.DefaultResourceName
    ) {
        var settings = new KurrentDBSettings();
        configure(settings);
        return app.AddKurrentDB(name, settings);
    }
    
    public static IDistributedApplicationBuilder AddKurrentDB(
        this IDistributedApplicationBuilder app
    ) => app.AddKurrentDB(KurrentDBResource.DefaultResourceName, KurrentDBSettings.SingleNode);

    static void ConfigureCertificates(this IResourceBuilder<KurrentDBResource> builder, IReadOnlyList<IResource> nodes) {
        const string certsTargetFolder   = "/etc/kurrentdb/certs";
        const string certGenResourceName = "kurrentdb-certs";
        const string certGenImage        = "docker.eventstore.com/eventstore-utils/es-gencert-cli";
        const string certGenTag          = "latest";
        const string certGenTargetFolder = "/certs";
        const string certGenScriptTarget = "/scripts/gencert.sh";

        (IResourceBuilder<ContainerResource> Resource, string HostPath) certs;

        var existing = builder.ApplicationBuilder
            .Resources.OfType<ContainerResource>()
            .FirstOrDefault(r => r.Name == certGenResourceName);

        if (existing is not null) {
            var mount = existing
                .Annotations.OfType<ContainerMountAnnotation>()
                .First(a => a.Target == certGenTargetFolder);

            certs = (builder.ApplicationBuilder.CreateResourceBuilder(existing), mount.Source!);
        }
        else {
            var hostPath = Path.Combine(Path.GetTempPath(), "kurrentdb-testkit-certs");
            Directory.CreateDirectory(hostPath);

            var containerBuilder = builder.ApplicationBuilder
                .AddContainer(certGenResourceName, certGenImage, certGenTag)
                .WithEntrypoint("/bin/sh")
                .WithArgs(ctx => {
                    ctx.Args.Add(certGenScriptTarget);
                    ctx.Args.Add(BuildCertificateDnsNames());
                })
                .WithBindMount(Path.Combine(AppContext.BaseDirectory, "gencert.sh"), certGenScriptTarget, true)
                .WithBindMount(hostPath, certGenTargetFolder)
                .WithLifetime(ContainerLifetime.Persistent);

            certs = (containerBuilder, hostPath);
        }

        builder
            .WithEndpoint("http", ep => {
                ep.UriScheme  = "https";
                ep.TlsEnabled = true;
            })
            .WithBindMount(certs.HostPath, certsTargetFolder, isReadOnly: true)
            .WaitForCompletion(certs.Resource);


        string BuildCertificateDnsNames() =>
            string.Join(",", builder.ApplicationBuilder.Resources
                    .OfType<KurrentDBResource>()
                    .Select(r => r.Name)
                    .Concat(nodes.Select(n => n.Name))
                    .Distinct(StringComparer.Ordinal)
                    .Prepend("localhost"));
    }
}

static class NodeConfiguration {
    /// <summary>
    /// Default insecure configuration for KurrentDB.
    /// </summary>
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

    /// <summary>
    /// Default secure configuration for KurrentDB.
    /// This configuration is used when TLS is enabled and user certificates are required.
    /// It includes settings for trusted root certificates and node certificates.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string?> Secure = new Dictionary<string, string?>(Insecure) {
        ["KURRENTDB_INSECURE"]                       = "false",
        ["KURRENTDB_TRUSTED_ROOT_CERTIFICATES_PATH"] = "/etc/kurrentdb/certs/ca",
        ["KURRENTDB_CERTIFICATE_FILE"]               = "/etc/kurrentdb/certs/node/node.crt",
        ["KURRENTDB_CERTIFICATE_PRIVATE_KEY_FILE"]   = "/etc/kurrentdb/certs/node/node.key",
        ["KURRENTDB_OPEN_TELEMETRY__OTLP__ENDPOINT"] = null,
        ["KURRENTDB_OPEN_TELEMETRY__LOGS__ENABLED"]  = "false",
    };
}
