using Kurrent.Sdk.Testkit.Hosting;
using Kurrent.Testing.Containers.KurrentDB;
using Microsoft.Extensions.DependencyInjection;

namespace Aspire.Hosting;

public static class KurrentDBBuilderExtensions {
    const string CertsTargetFolder = "/etc/kurrentdb/certs";

    const string CertGenResourceName = "kurrentdb-certs";
    const string CertGenImage        = "docker.eventstore.com/eventstore-utils/es-gencert-cli";
    const string CertGenTag          = "latest";
    const string CertGenTargetFolder = "/certs";
    const string CertGenScriptTarget = "/scripts/gencert.sh";

    static readonly string CertGenScriptSource = Path.Combine(AppContext.BaseDirectory, "gencert.sh");

    public static IDistributedApplicationBuilder AddKurrentDB(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        KurrentDBTestOptions? options = null,
        string image = KurrentDBContainerImageTags.Nightly.Image,
        string tag = KurrentDBContainerImageTags.Nightly.Tag
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        options ??= new KurrentDBTestOptions();
        Validate(options);

        var clusterNames = CreateClusterNames(name, options.Topology.NodeCount);
        var replicaNames = CreateReplicaNames(name, options.Topology.ReadOnlyReplicaCount);
        var gossipSeeds  = string.Join(",", clusterNames.Select(n => $"{n}:{KurrentDBResource.DefaultHttpPort}"));

        var certs = options.Security.Tls
            ? GetOrCreateCertBundle(builder, [..clusterNames, ..replicaNames])
            : null;

        foreach (var nodeName in clusterNames)
            AddNode(nodeName, isReadOnlyReplica: false);

        foreach (var nodeName in replicaNames)
            AddNode(nodeName, isReadOnlyReplica: true);

        return builder;

        static string[] CreateClusterNames(string baseName, int nodeCount) =>
            nodeCount == 1
                ? [baseName]
                : Enumerable.Range(1, nodeCount).Select(i => i == 1 ? baseName : $"{baseName}-{i}").ToArray();

        static string[] CreateReplicaNames(string baseName, int replicaCount) =>
            Enumerable.Range(1, replicaCount).Select(i => $"{baseName}-ro{i}").ToArray();

        void AddNode(string nodeName, bool isReadOnlyReplica) {
            var resource = new KurrentDBResource(nodeName, options) {
                CertsHostPath = certs?.HostPath,
                IsReadOnlyReplica = isReadOnlyReplica
            };

            builder.AddResource(resource)
                .WithHttpEndpoint(targetPort: KurrentDBResource.DefaultHttpPort, name: KurrentDBResource.HttpEndpointName)
                .WithImage(image, tag)
                .WithKurrentEnvironment(options)
                .WithSecureMode(certs)
                .WithClusterMode(options, gossipSeeds)
                .WithHealthMonitor(builder);
        }
    }

    static void Validate(KurrentDBTestOptions options) {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Topology.NodeCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(options.Topology.ReadOnlyReplicaCount);

        if (options.Topology is { ReadOnlyReplicaCount: > 0, NodeCount: < 3 })
            throw new ArgumentException("Read-only replicas require a cluster of at least 3 nodes.", nameof(options));
    }

    extension(IResourceBuilder<KurrentDBResource> node) {
        IResourceBuilder<KurrentDBResource> WithKurrentEnvironment(
            KurrentDBTestOptions options
        ) {
            var variables = options.Security.Tls
                ? KurrentDBConfiguration.Secure
                : KurrentDBConfiguration.Insecure;

            return node.WithEnvironment(ctx => ApplyEnvironmentVariables(ctx, variables));
        }

        IResourceBuilder<KurrentDBResource> WithSecureMode(
            CertBundle? certs
        ) {
            if (certs is null)
                return node;

            return node
                .WithEndpoint(KurrentDBResource.HttpEndpointName, ep => ep.UriScheme = "https")
                .WithBindMount(certs.HostPath, CertsTargetFolder, isReadOnly: true)
                .WaitForCompletion(certs.Resource);
        }

        IResourceBuilder<KurrentDBResource> WithClusterMode(
            KurrentDBTestOptions options, string gossipSeeds
        ) {
            if (!node.Resource.IsClusterMember)
                return node;

            return node.WithEnvironment(ctx => {
                ApplyClusterEnvironment(ctx, node.Resource, gossipSeeds, options.Topology.NodeCount);
                if (node.Resource.IsReadOnlyReplica)
                    ctx.EnvironmentVariables["KURRENTDB_READ_ONLY_REPLICA"] = "true";
            });
        }

        IResourceBuilder<KurrentDBResource> WithHealthMonitor(
            IDistributedApplicationBuilder builder
        ) {
            var healthCheckKey = $"{node.Resource.Name}_check";

            builder.Services
                .AddHealthChecks()
                .AddCheck(healthCheckKey, new KurrentDBHealthCheck(node.Resource));

            return node.WithHealthCheck(healthCheckKey);
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

    static void ApplyClusterEnvironment(
        EnvironmentCallbackContext context,
        KurrentDBResource resource,
        string gossipSeeds,
        int clusterSize
    ) {
        context.EnvironmentVariables["KURRENTDB_CLUSTER_SIZE"]                     = clusterSize.ToString();
        context.EnvironmentVariables["KURRENTDB_DISCOVER_VIA_DNS"]                 = "false";
        context.EnvironmentVariables["KURRENTDB_GOSSIP_SEED"]                      = gossipSeeds;
        context.EnvironmentVariables["KURRENTDB_NODE_HOST_ADVERTISE_AS"]           = resource.Name;
        context.EnvironmentVariables["KURRENTDB_REPLICATION_HOST_ADVERTISE_AS"]    = resource.Name;
        context.EnvironmentVariables["KURRENTDB_ADVERTISE_HOST_TO_CLIENT_AS"]      = "localhost";
        context.EnvironmentVariables["KURRENTDB_ADVERTISE_NODE_PORT_TO_CLIENT_AS"] = ReferenceExpression.Create($"{resource.PrimaryEndpoint.Property(EndpointProperty.Port)}");
    }

    static CertBundle GetOrCreateCertBundle(
        IDistributedApplicationBuilder builder,
        IReadOnlyList<string> secureNodeNames
    ) {
        var existing = builder.Resources.OfType<ContainerResource>()
            .FirstOrDefault(r => r.Name == CertGenResourceName);

        if (existing is not null) {
            var mount = existing.Annotations.OfType<ContainerMountAnnotation>()
                .First(a => a.Target == CertGenTargetFolder);
            return new CertBundle(builder.CreateResourceBuilder(existing), mount.Source!);
        }

        var hostPath = Path.Combine(Path.GetTempPath(), $"kurrentdb-testkit-certs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(hostPath);

        var resource = builder
            .AddContainer(CertGenResourceName, CertGenImage, CertGenTag)
            .WithEntrypoint("/bin/sh")
            .WithArgs(ctx => {
                ctx.Args.Add(CertGenScriptTarget);
                ctx.Args.Add(BuildCertificateDnsNames());
            })
            .WithBindMount(CertGenScriptSource, CertGenScriptTarget, true)
            .WithBindMount(hostPath, CertGenTargetFolder);

        return new CertBundle(resource, hostPath);

        string BuildCertificateDnsNames() =>
            string.Join(",", builder.Resources
                .OfType<KurrentDBResource>()
                .Where(r => r.Secure)
                .Select(r => r.Name)
                .Concat(secureNodeNames)
                .Distinct(StringComparer.Ordinal)
                .Prepend("localhost"));
    }

    sealed record CertBundle(IResourceBuilder<ContainerResource> Resource, string HostPath);
}
