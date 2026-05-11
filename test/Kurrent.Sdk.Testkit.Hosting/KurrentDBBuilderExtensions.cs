using Kurrent.Sdk.Testkit.Hosting;
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
        Action<KurrentDBSettings> configure,
        [ResourceName] string name = KurrentDBServerResource.DefaultResourceName
    ) {
        var settings = new KurrentDBSettings();
        app.Configuration.GetSection("KurrentDB").Bind(settings);
        configure(settings);
        return app.AddKurrentDB(name, settings);
    }

    public static IResourceBuilder<KurrentDBServerResource> AddKurrentDB(
        this IDistributedApplicationBuilder app
    ) => app.AddKurrentDB(_ => { });

    static IResourceBuilder<KurrentDBResource> AddNode(
        this IResourceBuilder<KurrentDBServerResource> serverBuilder,
        int nodeIndex,
        KurrentDBSettings settings
    ) {
        var app      = serverBuilder.ApplicationBuilder;
        var server   = serverBuilder.Resource;
        
        var isReplica = nodeIndex >=  settings.Topology.Nodes;
        var nodeName  = isReplica ? $"{server.Name}-ro{nodeIndex + 1:00}" : $"{server.Name}-{nodeIndex + 1:00}";
        var priority  = settings.Topology.NodeConfigurations[nodeIndex].Priority;
        // var node =   new KurrentDBResource(nodeName, server, priority);
        
        var node      = server.AddNode(nodeName, priority);

        var builder = app
            .AddResource(node)
            .WithHttpEndpoint(targetPort: KurrentDBServerResource.DefaultHttpPort)
            .WithImage(settings.Container.Image, settings.Container.Tag)
            .WithImageRegistry(settings.Container.Registry)
            .WithEnvironment(ctx => node.ConfigureEnvironment(ctx, settings))
            .WithLifetime(ContainerLifetime.Persistent);

        if (server.TlsEnabled) {
            builder
                .WithEndpoint("http", ep => { ep.UriScheme = "https"; ep.TlsEnabled = true; })
                .WithBindMount(server.CertsHostPath, "/etc/kurrentdb/certs", isReadOnly: true);
        }

        var healthCheckKey = $"{node.Name}_check";

        app.Services
            .AddHealthChecks()
            .Add(new HealthCheckRegistration(
                healthCheckKey,
                sp => {
                    var subscriber    = sp.GetRequiredService<ResourceConnectionProvider>();
                    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                    return new KurrentDBHealthCheck(node.Name, subscriber, loggerFactory);
                },
                failureStatus: HealthStatus.Unhealthy,
                tags: []));

        builder
            .WithHealthCheck(healthCheckKey)
            .WithAdminCommands();

        return builder;
    }

    public static IResourceBuilder<KurrentDBServerResource> WithClusterHealthCheck(
        this IResourceBuilder<KurrentDBServerResource> serverBuilder
    ) {
        var app    = serverBuilder.ApplicationBuilder;
        var server = serverBuilder.Resource;

        app.Services.AddHttpClient(KurrentDBClusterHealthCheck.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            });

        var healthCheckKey = $"{server.Name}_cluster_check";

        app.Services
            .AddHealthChecks()
            .AddTypeActivatedCheck<KurrentDBClusterHealthCheck>(healthCheckKey, server);

        return serverBuilder.WithHealthCheck(healthCheckKey);
    }

    static IResourceBuilder<ExecutableResource> AddCertificateGenerator(
        this IDistributedApplicationBuilder app,
        string name,
        string certsHostPath,
        KurrentDBServerResource server
    ) {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "gencert.sh");

        var certGen = app.AddExecutable(
            name: $"{name}-certs", command: "/bin/bash", workingDirectory: ".");

        certGen.WithArgs(context => {
            
            var dnsNames = string.Join(",", server.Nodes
                .Select(r => r.Name)
                .Distinct(StringComparer.Ordinal)
                .Prepend("localhost"));
            
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
        ["KURRENTDB_CERTIFICATE_PRIVATE_KEY_FILE"]   = "/etc/kurrentdb/certs/node/node.key"
    };
}
