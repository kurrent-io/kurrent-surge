using Kurrent.Sdk.Testkit.Hosting;
using Kurrent.Testing.Containers.KurrentDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aspire.Hosting;

public static class KurrentDBBuilderExtensions {
    const string DataTargetFolder  = "/var/lib/kurrentdb";
    const string CertsTargetFolder = "/etc/kurrentdb/certs";

    const string CertGenResourceName = "kurrentdb-certs";
    const string CertGenImage        = "docker.eventstore.com/eventstore-utils/es-gencert-cli";
    const string CertGenTag          = "latest";
    const string CertGenTargetFolder = "/certs";
    const string CertGenScriptTarget = "/scripts/gencert.sh";

    static readonly string CertGenScriptSource = Path.Combine(AppContext.BaseDirectory, "Hosting", "gencert.sh");

    public static IResourceBuilder<KurrentDBResource> AddKurrentDB(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        string image = KurrentDBContainerImageTags.Latest.Image,
        string tag = KurrentDBContainerImageTags.Latest.Tag
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        var resource = new KurrentDBResource(name);

        string? connectionString = null;

        builder.Eventing.Subscribe<ConnectionStringAvailableEvent>(resource, callback: async (_, ct) => {
            connectionString = await resource.ConnectionStringExpression.GetValueAsync(ct).ConfigureAwait(false);
            if (connectionString is null)
                throw new DistributedApplicationException(
                    $"ConnectionStringAvailableEvent was published for the '{resource.Name}' "
                  + $"resource but the connection string was null.");
        });

        var healthCheckKey = $"{name}_check";

        builder.Services.AddHealthChecks()
            .Add(new HealthCheckRegistration(healthCheckKey, _ => new KurrentDBHealthCheck(connectionString!), null, null, null));

        return builder
            .AddResource(resource)
            .WithHttpEndpoint(targetPort: KurrentDBResource.DefaultHttpPort, name: KurrentDBResource.HttpEndpointName)
            .WithImage(image, tag)
            //.WithImageRegistry(KurrentDBContainerImageTags.Registry)
            .WithEnvironment(ctx => ApplyEnvironmentVariables(ctx, KurrentDBConfiguration.Insecure))
            .WithHealthCheck(healthCheckKey);
    }

    public static IResourceBuilder<KurrentDBResource> WithSecureMode(
        this IResourceBuilder<KurrentDBResource> builder
    ) {
        ArgumentNullException.ThrowIfNull(builder);

        var certGen = AddOrGetCertGenResource(builder.ApplicationBuilder);

        builder.Resource.Secure = true;

        return builder
            .WithEndpoint(KurrentDBResource.HttpEndpointName, endpoint => endpoint.UriScheme = "https")
            .WithBindMount(certGen.HostPath, CertsTargetFolder, true)
            .WithEnvironment(ctx => ApplyEnvironmentVariables(ctx, KurrentDBConfiguration.Secure))
            .WaitForCompletion(certGen.Resource);
    }

    internal static string? GetCertsHostPath(IDistributedApplicationBuilder builder) =>
        builder.Resources.OfType<ContainerResource>()
            .FirstOrDefault(r => r.Name == CertGenResourceName)
            ?.Annotations.OfType<ContainerMountAnnotation>()
            .FirstOrDefault(a => a.Target == CertGenTargetFolder)
            ?.Source;

    static CertGenResource AddOrGetCertGenResource(IDistributedApplicationBuilder builder) {
        var existing = builder.Resources.OfType<ContainerResource>()
            .FirstOrDefault(r => r.Name == CertGenResourceName);

        if (existing is not null) {
            var mount = existing.Annotations.OfType<ContainerMountAnnotation>()
                .First(a => a.Target == CertGenTargetFolder);

            return new CertGenResource(builder.CreateResourceBuilder(existing), mount.Source!);
        }

        var hostPath = Path.Combine(Path.GetTempPath(), $"kurrentdb-testkit-certs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(hostPath);

        var resource = builder
            .AddContainer(CertGenResourceName, CertGenImage, CertGenTag)
            .WithEntrypoint("/bin/sh")
            .WithArgs(CertGenScriptTarget)
            .WithBindMount(CertGenScriptSource, CertGenScriptTarget, true)
            .WithBindMount(hostPath, CertGenTargetFolder);

        return new CertGenResource(resource, hostPath);
    }

    readonly record struct CertGenResource(IResourceBuilder<ContainerResource> Resource, string HostPath);

    public static IResourceBuilder<KurrentDBResource> WithDataVolume(
        this IResourceBuilder<KurrentDBResource> builder,
        string? name = null
    ) {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithVolume(name ?? VolumeNameGenerator.Generate(builder, "data"), DataTargetFolder);
    }

    public static IResourceBuilder<KurrentDBResource> WithDataBindMount(
        this IResourceBuilder<KurrentDBResource> builder,
        string source
    ) {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(source);

        return builder.WithBindMount(source, DataTargetFolder);
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
}
