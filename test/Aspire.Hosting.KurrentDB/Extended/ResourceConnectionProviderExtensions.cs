using Aspire.Hosting.Lifecycle;
using Aspire.Hosting.KurrentDB;

namespace Microsoft.Extensions.DependencyInjection;

public static class ResourceConnectionProviderExtensions {
    /// <summary>
    /// Registers <see cref="ResourceConnectionProvider"/> as a singleton and as an
    /// <see cref="IDistributedApplicationEventingSubscriber"/> so Aspire invokes it
    /// during the application lifecycle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method exists because <c>AddEventingSubscriber&lt;T&gt;</c> registers the type
    /// only as <see cref="IDistributedApplicationEventingSubscriber"/>, making it impossible
    /// to resolve the concrete <see cref="ResourceConnectionProvider"/> from DI. The
    /// health check and admin commands need the concrete type to call
    /// <see cref="ResourceConnectionProvider.GetConnectionStringAsync(string, System.Threading.CancellationToken)"/>.
    /// </para>
    /// <para>
    /// <c>TryAddEnumerable</c> is not used because factory delegates register the
    /// implementation type as the interface itself, which it rejects as "indistinguishable"
    /// from other subscribers. A manual idempotency guard is used instead.
    /// </para>
    /// <para>
    /// Safe to call multiple times (e.g. multi-node topologies that call <c>AddKurrentDB</c>
    /// more than once).
    /// </para>
    /// </remarks>
    public static IServiceCollection AddResourceConnectionProvider(this IServiceCollection services) {
        if (services.Any(d => d.ServiceType == typeof(ResourceConnectionProvider)))
            return services;

        services.AddSingleton<ResourceConnectionProvider>();
        services.AddSingleton<IDistributedApplicationEventingSubscriber>(sp => sp.GetRequiredService<ResourceConnectionProvider>());

        return services;
    }
}
