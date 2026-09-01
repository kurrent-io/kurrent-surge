using Kurrent.Surge.Client.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kurrent.Surge;

/// <summary>
/// The entry point for registering Kurrent Surge with dependency injection.
/// </summary>
/// <remarks>
/// <c>AddKurrentSurge</c> opens a registration scope. The connection and every component that runs
/// against it are declared inside that scope through <see cref="IKurrentSurgeBuilder"/>.
/// </remarks>
[PublicAPI]
public static class KurrentSurgeServiceCollectionExtensions {
    /// <param name="services">The service collection.</param>
    extension(IServiceCollection services) {
        /// <summary>
        /// Opens the Kurrent Surge registration scope.
        /// </summary>
        /// <param name="configure">Declares the connection and components inside the scope.</param>
        /// <returns>The service collection for method chaining.</returns>
        public IServiceCollection AddKurrentSurge(Action<IKurrentSurgeBuilder> configure) {
            var builder = new KurrentSurgeBuilder(services);

            // Registered first so an explicit Connection call inside configure replaces it.
            builder.RegisterDefaultConnection();
            configure(builder);

            return services;
        }
    }

    /// <param name="builder">The host application builder.</param>
    extension(IHostApplicationBuilder builder) {
        /// <summary>
        /// Opens the Kurrent Surge registration scope on a host application builder.
        /// </summary>
        /// <param name="configure">Declares the connection and components inside the scope.</param>
        /// <returns>The host application builder for method chaining.</returns>
        public IHostApplicationBuilder AddKurrentSurge(Action<IKurrentSurgeBuilder> configure) {
            builder.Services.AddKurrentSurge(configure);
            return builder;
        }

        /// <summary>
        /// Opens the Kurrent Surge registration scope with the connection read from configuration.
        /// </summary>
        /// <param name="connectionName">
        /// The key under <c>ConnectionStrings</c> holding the connection string. This is the name
        /// of the KurrentDB resource, not the connection string itself.
        /// </param>
        /// <param name="configure">Optional. Declares components inside the scope.</param>
        /// <returns>The host application builder for method chaining.</returns>
        /// <exception cref="KurrentClientNotConfiguredException">
        /// Thrown when <paramref name="connectionName"/> has no entry under <c>ConnectionStrings</c>.
        /// </exception>
        public IHostApplicationBuilder AddKurrentSurge(
            string connectionName,
            Action<IKurrentSurgeBuilder>? configure = null
        ) {
            var connectionString = builder.Configuration.GetConnectionString(connectionName)
                                ?? throw new KurrentClientNotConfiguredException();

            return builder.AddKurrentSurge(surge => {
                surge.Connection(connectionString);
                configure?.Invoke(surge);
            });
        }
    }
}
