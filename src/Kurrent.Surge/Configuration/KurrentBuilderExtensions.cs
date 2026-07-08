using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Kurrent.Client.KurrentClientServiceCollectionExtensions;

namespace Kurrent.Client;

/// <summary>
/// Single entry point for registering Kurrent components with dependency injection.
/// </summary>
/// <remarks>
/// <para>
/// <c>AddKurrent</c> opens a registration scope; components (processors, ...) register through
/// <see cref="IKurrentBuilder"/> extensions inside that scope. <c>Connection</c> sets the scope's
/// client options and client — mirroring <c>ProcessorOptions.Connection</c> — and is authoritative:
/// it replaces any <see cref="KurrentClientOptions"/> / <see cref="KurrentClient"/> already in the
/// service collection (unlike <c>AddKurrentClient</c>'s first-wins).
/// </para>
/// <para>
/// Because <c>Connection</c> registers the <see cref="KurrentClientOptions"/> themselves (not just
/// the client), components that resolve options from DI — processors without an explicit
/// <c>opts.Connection(...)</c> — inherit the scope connection automatically.
/// </para>
/// </remarks>
[PublicAPI]
public static class KurrentBuilderExtensions {
    /// <param name="services">The service collection.</param>
    extension(IServiceCollection services) {
        /// <summary>
        /// Opens the Kurrent registration scope.
        /// </summary>
        /// <param name="configure">Registers Kurrent components inside the scope.</param>
        /// <returns>The service collection for method chaining.</returns>
        public IServiceCollection AddKurrent(Action<IKurrentBuilder> configure) {
            configure(new KurrentBuilder(services));
            return services;
        }
    }

    /// <param name="builder">The Kurrent builder.</param>
    extension(IKurrentBuilder builder) {
        /// <summary>
        /// Sets the scope's connection from a connection string.
        /// </summary>
        /// <param name="connectionString">
        /// KurrentDB connection string (e.g., <c>kurrentdb://localhost:2113</c>).
        /// </param>
        /// <returns>The builder for method chaining.</returns>
        public IKurrentBuilder Connection(string connectionString) =>
            builder.Connection(options => options.Connection(connectionString));

        /// <summary>
        /// Sets the scope's connection from a connection string, then tunes the resolved options.
        /// </summary>
        public IKurrentBuilder Connection(string connectionString, Action<KurrentClientOptions> configure) =>
            builder.Connection(options => {
                options.Connection(connectionString);
                configure(options);
            });

        /// <summary>
        /// Sets the scope's connection from pre-constructed options. Service-provider overrides
        /// are still applied when the options are first resolved.
        /// </summary>
        public IKurrentBuilder Connection(KurrentClientOptions options) {
            options.EnsureValid();
            builder.Services.Replace(ServiceDescriptor.Singleton(sp => ResolveServices(options, sp)));
            RegisterClient(builder.Services);
            return builder;
        }

        /// <summary>
        /// Sets the scope's connection by configuring the options directly.
        /// </summary>
        /// <param name="configure">
        /// Configures the options. Runs after service-provider overrides, so the caller can
        /// override anything.
        /// </param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para>
        /// Registers a <see cref="KurrentClientOptions"/> singleton (built lazily: service-provider
        /// overrides, then <paramref name="configure"/>, then validation) and a
        /// <see cref="KurrentClient"/> resolving those options. Both use <c>Replace</c> — the scope
        /// is authoritative over anything pre-registered; calling <c>Connection</c> again replaces
        /// the previous call (last wins).
        /// </para>
        /// </remarks>
        public IKurrentBuilder Connection(Action<KurrentClientOptions> configure) {
            builder.Services.Replace(ServiceDescriptor.Singleton(sp => {
                var options = ResolveServices(new KurrentClientOptions(), sp);
                configure(options);
                options.EnsureValid();
                return options;
            }));

            RegisterClient(builder.Services);
            return builder;
        }
    }

    static void RegisterClient(IServiceCollection services) =>
        services.Replace(ServiceDescriptor.Singleton(sp =>
            KurrentClient.Create(sp.GetRequiredService<KurrentClientOptions>())));
}
