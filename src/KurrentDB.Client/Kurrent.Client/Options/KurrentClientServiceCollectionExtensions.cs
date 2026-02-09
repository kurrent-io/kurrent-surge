using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kurrent.Client;

/// <summary>
/// Extension methods for registering <see cref="KurrentClient"/> with dependency injection.
/// All overloads use <c>TryAddSingleton</c> — if a client is already registered, the call is ignored.
/// </summary>
[PublicAPI]
public static class KurrentClientServiceCollectionExtensions {
    /// <param name="services">The service collection.</param>
    extension(IServiceCollection services) {
        /// <summary>
        /// Registers a <see cref="KurrentClient"/> from the <c>Kurrent:Client</c> configuration section.
        /// Falls back to default options if no configuration is available.
        /// </summary>
        /// <returns>The service collection for method chaining.</returns>
        public IServiceCollection AddKurrentClient() {
            services.TryAddSingleton(sp => {
                var configuration = sp.GetService<IConfiguration>();
                var builder       = KurrentClientOptionsBuilder.Create();

                if (configuration is not null)
                    builder.FromConfiguration(configuration);

                return builder.CreateClient();
            });
            return services;
        }

        /// <summary>
        /// Registers a <see cref="KurrentClient"/> from a connection string.
        /// </summary>
        /// <param name="connectionString">
        /// KurrentDB connection string (e.g., "kurrentdb://localhost:2113").
        /// </param>
        /// <returns>The service collection for method chaining.</returns>
        public IServiceCollection AddKurrentClient(string connectionString) {
            services.TryAddSingleton(_ => KurrentClient.Create(connectionString));
            return services;
        }

        /// <summary>
        /// Registers a <see cref="KurrentClient"/> from a <see cref="KurrentClientOptions"/> factory.
        /// Configuration from <c>Kurrent:Client</c> is not applied — the factory has full control.
        /// </summary>
        /// <param name="configure">Factory that returns a fully constructed <see cref="KurrentClientOptions"/>.</param>
        /// <returns>The service collection for method chaining.</returns>
        public IServiceCollection AddKurrentClient(Func<KurrentClientOptions> configure) {
            services.TryAddSingleton(_ => new KurrentClient(configure()));
            return services;
        }

        /// <summary>
        /// Registers a <see cref="KurrentClient"/> using the options builder.
        /// Settings from the <c>Kurrent:Client</c> configuration section are applied first,
        /// then the configure action is applied as overrides.
        /// </summary>
        /// <param name="configure">Action to configure the client options builder.</param>
        /// <returns>The service collection for method chaining.</returns>
        public IServiceCollection AddKurrentClient(Action<KurrentClientOptionsBuilder> configure) {
            services.TryAddSingleton(sp => {
                var configuration = sp.GetService<IConfiguration>();
                var builder       = KurrentClientOptionsBuilder.Create();

                // 1. Bind from configuration first (base settings)
                if (configuration is not null)
                    builder.FromConfiguration(configuration);

                // 2. Apply user configuration (overrides)
                configure(builder);

                return builder.CreateClient();
            });
            return services;
        }

        /// <summary>
        /// Registers a <see cref="KurrentClient"/> using the options builder with access to the service provider.
        /// Settings from the <c>Kurrent:Client</c> configuration section are applied first,
        /// then the configure action is applied as overrides.
        /// </summary>
        /// <param name="configure">Action to configure the client with access to the service provider.</param>
        /// <returns>The service collection for method chaining.</returns>
        public IServiceCollection AddKurrentClient(Action<KurrentClientOptionsBuilder, IServiceProvider> configure) {
            services.TryAddSingleton(sp => {
                var configuration = sp.GetService<IConfiguration>();
                var builder       = KurrentClientOptionsBuilder.Create();

                if (configuration is not null)
                    builder.FromConfiguration(configuration);

                configure(builder, sp);

                return builder.CreateClient();
            });
            return services;
        }
    }
}
