using Kurrent.Sdk.Testkit.Hosting;
using Kurrent.Testing.Containers.KurrentDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aspire.Hosting;

public static class KurrentDBBuilderExtensions {
    const string CertsTargetFolder = "/etc/kurrentdb/certs";

    const string CertGenResourceName = "kurrentdb-certs";
    const string CertGenImage        = "docker.eventstore.com/eventstore-utils/es-gencert-cli";
    const string CertGenTag          = "latest";
    const string CertGenTargetFolder = "/certs";
    const string CertGenScriptTarget = "/scripts/gencert.sh";

    static readonly string CertGenScriptSource = Path.Combine(AppContext.BaseDirectory, "Hosting", "gencert.sh");

    /// <summary>Registers a KurrentDB node.</summary>
    public static IResourceBuilder<KurrentDBResource> AddKurrentDB(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        string image = KurrentDBContainerImageTags.Nightly.Image,
        string tag = KurrentDBContainerImageTags.Nightly.Tag
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        var resource = new KurrentDBResource(name);

        string? connectionString = null;
        builder.Eventing.Subscribe<ConnectionStringAvailableEvent>(resource, async (_, ct) => {
            connectionString = await resource.ConnectionStringExpression.GetValueAsync(ct).ConfigureAwait(false)
                            ?? throw new DistributedApplicationException(
                                $"ConnectionStringAvailableEvent fired for '{resource.Name}' with a null connection string.");
        });

        var healthCheckKey = $"{name}_check";
        builder.Services.AddHealthChecks()
            .Add(new HealthCheckRegistration(healthCheckKey, _ => new KurrentDBHealthCheck(connectionString!), null, null, null));

        return builder
            .AddResource(resource)
            .WithHttpEndpoint(targetPort: KurrentDBResource.DefaultHttpPort, name: KurrentDBResource.HttpEndpointName)
            .WithImage(image, tag)
            .WithEnvironment(ctx => ApplyEnvironmentVariables(ctx, KurrentDBConfiguration.Insecure))
            .WithHealthCheck(healthCheckKey);
    }

    /// <summary>Expands this node into a <paramref name="size"/>-node cluster.</summary>
    public static IResourceBuilder<KurrentDBResource> WithClusterSize(
        this IResourceBuilder<KurrentDBResource> primary,
        int size
    ) {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        if (size == 1) return primary;

        var cluster = new List<KurrentDBResource> { primary.Resource };
        primary.Resource.Cluster = cluster;

        for (var i = 2; i <= size; i++) {
            var peer = primary.ApplicationBuilder.AddKurrentDB($"{primary.Resource.Name}-{i}");
            peer.Resource.IsPrimary = false;
            peer.Resource.Cluster   = cluster;
            cluster.Add(peer.Resource);

            if (primary.Resource.Secure)
                peer.WithSecureMode();
        }

        var gossipSeeds = string.Join(",", cluster.Select(n => $"{n.Name}:{KurrentDBResource.DefaultHttpPort}"));
        var clusterSize = cluster.Count;

        foreach (var node in cluster)
            Configure(primary.ApplicationBuilder.CreateResourceBuilder(node));

        return primary;

        void Configure(IResourceBuilder<KurrentDBResource> node) =>
            node.WithEnvironment(ctx => {
                ctx.EnvironmentVariables["KURRENTDB_CLUSTER_SIZE"]                    = clusterSize.ToString();
                ctx.EnvironmentVariables["KURRENTDB_DISCOVER_VIA_DNS"]                = "false";
                ctx.EnvironmentVariables["KURRENTDB_GOSSIP_SEED"]                     = gossipSeeds;
                ctx.EnvironmentVariables["KURRENTDB_NODE_HOST_ADVERTISE_AS"]          = node.Resource.Name;
                ctx.EnvironmentVariables["KURRENTDB_REPLICATION_HOST_ADVERTISE_AS"]   = node.Resource.Name;
                ctx.EnvironmentVariables["KURRENTDB_ADVERTISE_HOST_TO_CLIENT_AS"]     = "localhost";
                ctx.EnvironmentVariables["KURRENTDB_ADVERTISE_NODE_PORT_TO_CLIENT_AS"] =
                    ReferenceExpression.Create($"{node.Resource.PrimaryEndpoint.Property(EndpointProperty.Port)}");
            });
    }

    /// <summary>Enables TLS; propagates across the cluster in any call order.</summary>
    public static IResourceBuilder<KurrentDBResource> WithSecureMode(
        this IResourceBuilder<KurrentDBResource> builder,
        bool enabled = true
    ) {
        if (!enabled) return builder;

        var nodes = builder.Resource.Cluster ?? [builder.Resource];
        foreach (var node in nodes)
            Apply(builder.ApplicationBuilder.CreateResourceBuilder(node));

        return builder;

        static void Apply(IResourceBuilder<KurrentDBResource> node) {
            if (node.Resource.Secure) return;

            var certGen = AddOrGetCertGenResource(node.ApplicationBuilder);
            node.Resource.Secure        = true;
            node.Resource.CertsHostPath = certGen.HostPath;

            node
                .WithEndpoint(KurrentDBResource.HttpEndpointName, endpoint => endpoint.UriScheme = "https")
                .WithBindMount(certGen.HostPath, CertsTargetFolder, isReadOnly: true)
                .WithEnvironment(ctx => ApplyEnvironmentVariables(ctx, KurrentDBConfiguration.Secure))
                .WaitForCompletion(certGen.Resource);
        }
    }

    static void ApplyEnvironmentVariables(
        EnvironmentCallbackContext context,
        IReadOnlyDictionary<string, string?> variables
    ) {
        foreach (var (key, value) in variables) {
            if (value is not null)
                context.EnvironmentVariables[key] = value;
            else
                context.EnvironmentVariables.Remove(key);
        }
    }

    static (IResourceBuilder<ContainerResource> Resource, string HostPath) AddOrGetCertGenResource(IDistributedApplicationBuilder builder) {
        var existing = builder.Resources.OfType<ContainerResource>()
            .FirstOrDefault(r => r.Name == CertGenResourceName);

        if (existing is not null) {
            var mount = existing.Annotations.OfType<ContainerMountAnnotation>()
                .First(a => a.Target == CertGenTargetFolder);
            return (builder.CreateResourceBuilder(existing), mount.Source!);
        }

        var hostPath = Path.Combine(Path.GetTempPath(), $"kurrentdb-testkit-certs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(hostPath);

        var resource = builder
            .AddContainer(CertGenResourceName, CertGenImage, CertGenTag)
            .WithEntrypoint("/bin/sh")
            .WithArgs(ctx => {
                ctx.Args.Add(CertGenScriptTarget);
                ctx.Args.Add(BuildCertificateDnsNames(builder));
            })
            .WithBindMount(CertGenScriptSource, CertGenScriptTarget, true)
            .WithBindMount(hostPath, CertGenTargetFolder);

        return (resource, hostPath);
    }

    static string BuildCertificateDnsNames(IDistributedApplicationBuilder builder) =>
        string.Join(",", builder.Resources
            .OfType<KurrentDBResource>()
            .Where(r => r.Secure)
            .Select(r => r.Name)
            .Prepend("localhost"));
}
