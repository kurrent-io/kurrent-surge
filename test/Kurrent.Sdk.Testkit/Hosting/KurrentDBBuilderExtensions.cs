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

    /// <summary>
    /// Registers a KurrentDB topology in one pass. Set <see cref="KurrentDBTopology.NodeCount"/>
    /// for clusters and <see cref="KurrentDBTopology.Secure"/> for TLS. Returns the entry node;
    /// peers are named <c>{name}-2</c>, <c>{name}-3</c>, etc.
    /// </summary>
    public static IResourceBuilder<KurrentDBResource> AddKurrentDB(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        KurrentDBTopology? topology = null,
        string image = KurrentDBContainerImageTags.Nightly.Image,
        string tag = KurrentDBContainerImageTags.Nightly.Tag
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        topology ??= new KurrentDBTopology();
        ArgumentOutOfRangeException.ThrowIfLessThan(topology.NodeCount, 1);

        var nodeNames = BuildNodeNames(name, topology.NodeCount);

        IResourceBuilder<ContainerResource>? certGen = null;
        string? certsHostPath = null;
        if (topology.Secure)
            (certGen, certsHostPath) = EnsureCertGenResource(builder, nodeNames);

        var gossipSeeds = string.Join(",", nodeNames.Select(n => $"{n}:{KurrentDBResource.DefaultHttpPort}"));

        IResourceBuilder<KurrentDBResource>? entry = null;
        foreach (var nodeName in nodeNames) {
            var resource = new KurrentDBResource(nodeName, topology) { CertsHostPath = certsHostPath };

            var node = builder.AddResource(resource)
                .WithHttpEndpoint(targetPort: KurrentDBResource.DefaultHttpPort, name: KurrentDBResource.HttpEndpointName)
                .WithImage(image, tag)
                .WithKurrentEnvironment(topology)
                .WithSecureMode(topology, certsHostPath, certGen)
                .WithClusterMode(topology, gossipSeeds)
                .WithHealthMonitor(builder);

            entry ??= node;
        }

        return entry!;
    }

    static string[] BuildNodeNames(string name, int count) =>
        count == 1
            ? [name]
            : Enumerable.Range(1, count).Select(i => i == 1 ? name : $"{name}-{i}").ToArray();

    static IResourceBuilder<KurrentDBResource> WithKurrentEnvironment(
        this IResourceBuilder<KurrentDBResource> node,
        KurrentDBTopology topology
    ) =>
        node.WithEnvironment(ctx => ApplyEnvironmentVariables(
            ctx, topology.Secure ? KurrentDBConfiguration.Secure : KurrentDBConfiguration.Insecure));

    static IResourceBuilder<KurrentDBResource> WithSecureMode(
        this IResourceBuilder<KurrentDBResource> node,
        KurrentDBTopology topology,
        string? certsHostPath,
        IResourceBuilder<ContainerResource>? certGen
    ) =>
        !topology.Secure ? node :
        node.WithEndpoint(KurrentDBResource.HttpEndpointName, ep => ep.UriScheme = "https")
            .WithBindMount(certsHostPath!, CertsTargetFolder, isReadOnly: true)
            .WaitForCompletion(certGen!);

    static IResourceBuilder<KurrentDBResource> WithClusterMode(
        this IResourceBuilder<KurrentDBResource> node,
        KurrentDBTopology topology,
        string gossipSeeds
    ) =>
        topology.NodeCount <= 1 ? node :
        node.WithEnvironment(ctx => ApplyClusterEnvironment(ctx, node.Resource, gossipSeeds, topology.NodeCount));

    static IResourceBuilder<KurrentDBResource> WithHealthMonitor(
        this IResourceBuilder<KurrentDBResource> node,
        IDistributedApplicationBuilder builder
    ) {
        var resource = node.Resource;

        string? connectionString = null;
        builder.Eventing.Subscribe<ConnectionStringAvailableEvent>(resource, async (_, ct) => {
            connectionString = await resource.ConnectionStringExpression.GetValueAsync(ct).ConfigureAwait(false)
                            ?? throw new DistributedApplicationException(
                                $"ConnectionStringAvailableEvent fired for '{resource.Name}' with a null connection string.");
        });

        var healthCheckKey = $"{resource.Name}_check";
        builder.Services.AddHealthChecks()
            .Add(new HealthCheckRegistration(healthCheckKey, _ => new KurrentDBHealthCheck(connectionString!), null, null, null));

        return node.WithHealthCheck(healthCheckKey);
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

    static void ApplyClusterEnvironment(
        EnvironmentCallbackContext context,
        KurrentDBResource resource,
        string gossipSeeds,
        int clusterSize
    ) {
        context.EnvironmentVariables["KURRENTDB_CLUSTER_SIZE"]                  = clusterSize.ToString();
        context.EnvironmentVariables["KURRENTDB_DISCOVER_VIA_DNS"]              = "false";
        context.EnvironmentVariables["KURRENTDB_GOSSIP_SEED"]                   = gossipSeeds;
        context.EnvironmentVariables["KURRENTDB_NODE_HOST_ADVERTISE_AS"]        = resource.Name;
        context.EnvironmentVariables["KURRENTDB_REPLICATION_HOST_ADVERTISE_AS"] = resource.Name;
        context.EnvironmentVariables["KURRENTDB_ADVERTISE_HOST_TO_CLIENT_AS"]   = "localhost";
        context.EnvironmentVariables["KURRENTDB_ADVERTISE_NODE_PORT_TO_CLIENT_AS"] =
            ReferenceExpression.Create($"{resource.PrimaryEndpoint.Property(EndpointProperty.Port)}");
    }

    static (IResourceBuilder<ContainerResource> Builder, string HostPath) EnsureCertGenResource(
        IDistributedApplicationBuilder builder,
        IReadOnlyList<string> secureNodeNames
    ) {
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
                ctx.Args.Add(BuildCertificateDnsNames(builder, secureNodeNames));
            })
            .WithBindMount(CertGenScriptSource, CertGenScriptTarget, true)
            .WithBindMount(hostPath, CertGenTargetFolder);

        return (resource, hostPath);
    }

    static string BuildCertificateDnsNames(IDistributedApplicationBuilder builder, IReadOnlyList<string> secureNodeNames) =>
        string.Join(",", builder.Resources
            .OfType<KurrentDBResource>()
            .Where(r => r.Secure)
            .Select(r => r.Name)
            .Concat(secureNodeNames)
            .Distinct(StringComparer.Ordinal)
            .Prepend("localhost"));
}
