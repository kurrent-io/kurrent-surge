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
    /// Registers a single KurrentDB node as a <see cref="KurrentDBResource"/>. Chain
    /// <see cref="WithReplicas"/> to expand into a cluster or <see cref="WithSecureMode"/>
    /// to enable TLS; both propagate to peers when called after <see cref="WithReplicas"/>.
    /// </summary>
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

    /// <summary>
    /// Expands this node into a cluster by adding <paramref name="count"/> peers named
    /// <c>{name}-2</c>, <c>{name}-3</c>, ... with gossip seeds wired across all members.
    /// Chain this BEFORE <see cref="WithSecureMode"/> if you want TLS to apply to every
    /// node in the cluster.
    /// </summary>
    public static IResourceBuilder<KurrentDBResource> WithReplicas(
        this IResourceBuilder<KurrentDBResource> primary,
        int count
    ) {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0) return primary;

        var cluster = new List<KurrentDBResource> { primary.Resource };
        primary.Resource.Cluster = cluster;

        for (var i = 2; i <= count + 1; i++) {
            var peer = primary.ApplicationBuilder.AddKurrentDB($"{primary.Resource.Name}-{i}");
            peer.Resource.IsPrimary = false;
            peer.Resource.Cluster   = cluster;
            cluster.Add(peer.Resource);
        }

        const int port = KurrentDBResource.DefaultHttpPort;
        var gossipSeeds = string.Join(",", cluster.Select(n => $"{n.Name}:{port}"));
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

    /// <summary>
    /// Enables TLS. When called after <see cref="WithReplicas"/>, propagates to every node
    /// in the cluster; otherwise applies to this node only. The first invocation in the app
    /// registers a one-shot cert-gen container that writes a CA, node cert, and admin user
    /// cert to a temp directory; subsequent invocations reuse it. Each secure node waits
    /// for cert-gen to complete before starting and bind-mounts the certs directory read-only.
    /// </summary>
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

    static CertGenResource AddOrGetCertGenResource(IDistributedApplicationBuilder builder) {
        if (FindCertGenResource(builder) is { } existing)
            return new CertGenResource(builder.CreateResourceBuilder(existing.Resource), existing.HostPath);

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

        return new CertGenResource(resource, hostPath);
    }

    static string BuildCertificateDnsNames(IDistributedApplicationBuilder builder) {
        var names = new List<string> { "localhost" };

        names.AddRange(builder.Resources
            .OfType<KurrentDBResource>()
            .Where(r => r.Secure)
            .Select(r => r.Name));

        return string.Join(",", names.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    static (ContainerResource Resource, string HostPath)? FindCertGenResource(IDistributedApplicationBuilder builder) {
        var resource = builder.Resources.OfType<ContainerResource>()
            .FirstOrDefault(r => r.Name == CertGenResourceName);

        if (resource is null) return null;

        var mount = resource.Annotations.OfType<ContainerMountAnnotation>()
            .First(a => a.Target == CertGenTargetFolder);

        return (resource, mount.Source!);
    }

    readonly record struct CertGenResource(IResourceBuilder<ContainerResource> Resource, string HostPath);
}
