using Kurrent.SDK.Testing.Hosting;
using Kurrent.Testing.Containers.KurrentDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aspire.Hosting;

public static class KurrentDBBuilderExtensions {
    const string DataTargetFolder  = "/var/lib/kurrentdb";
    const string CertsTargetFolder = "/etc/kurrentdb/certs";

    public static IResourceBuilder<KurrentDBResource> AddKurrentDB(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        string image = KurrentDBContainerImageTags.Latest.Image,
        string tag = KurrentDBContainerImageTags.Latest.Tag
    ) {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(name);

        var resource = new KurrentDBResource(name);

        string? connectionString = null;

        builder.Eventing.Subscribe<ConnectionStringAvailableEvent>(
            resource,
            async (@event, cancellationToken) => {
                connectionString = await resource.ConnectionStringExpression
                                       .GetValueAsync(cancellationToken)
                                       .ConfigureAwait(false)
                                ?? throw new DistributedApplicationException(
                                       $"ConnectionStringAvailableEvent was published for the '{resource.Name}' resource but the connection string was null."
                                   );
            }
        );

        var healthCheckKey = $"{name}_check";

        builder.Services.AddHealthChecks()
            .Add(
                new HealthCheckRegistration(
                    healthCheckKey,
                    sp => new KurrentDBHealthCheck(connectionString!),
                    default,
                    default,
                    default
                )
            );

        return builder
            .AddResource(resource)
            .WithHttpEndpoint(targetPort: KurrentDBResource.DefaultHttpPort, name: KurrentDBResource.HttpEndpointName)
            .WithImage(image, tag)
            .WithImageRegistry(KurrentDBContainerImageTags.Registry)
            .WithEnvironment(ctx => ApplyEnvironmentVariables(ctx, KurrentDBConfiguration.Insecure))
            .WithHealthCheck(healthCheckKey);
    }

    public static IResourceBuilder<KurrentDBResource> WithSecureMode(
        this IResourceBuilder<KurrentDBResource> builder,
        string certsPath
    ) {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(certsPath);

        builder.Resource.Secure = true;

        return builder
            .WithBindMount(certsPath, CertsTargetFolder, true)
            .WithEnvironment(ctx => ApplyEnvironmentVariables(ctx, KurrentDBConfiguration.Secure));
    }

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
        foreach (var (key, value) in variables)
            context.EnvironmentVariables[key] = value ?? string.Empty;
    }
}
