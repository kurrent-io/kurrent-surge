using Kurrent.Client.Exceptions;
using Kurrent.Client.Schema;
using Kurrent.Client.Streams;
using Kurrent.Schema.Catalogs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Kurrent.Client;

/// <summary>
/// Extension methods for registering <see cref="KurrentClient"/> with dependency injection.
/// </summary>
/// <remarks>
/// <para>
/// All overloads use <c>TryAddSingleton</c> — the first registration wins; subsequent calls are ignored.
/// </para>
/// <para>
/// Every path resolves the following services from the container and applies them to the options
/// (overriding any pre-set values):
/// </para>
/// <list type="bullet">
///   <item><description><see cref="MessageTypeMapper"/> — assigned to <c>Schema.Mapper</c></description></item>
///   <item><description><see cref="ILoggerFactory"/> — assigned to <c>LoggerFactory</c></description></item>
///   <item><description><see cref="IMetadataDecoder"/> — assigned to <c>MetadataDecoder</c></description></item>
/// </list>
/// </remarks>
[PublicAPI]
public static class KurrentClientServiceCollectionExtensions {
    /// <param name="services">The service collection.</param>
    extension(IServiceCollection services) {
        /// <summary>
        /// Registers a <see cref="KurrentClient"/> with options resolved from the service provider
        /// or configuration, with an optional final configuration step.
        /// </summary>
        /// <param name="configure">
        /// Optional action to tune the resolved options. Runs last, after service-provider overrides,
        /// so the caller can override anything.
        /// </param>
        /// <returns>The service collection for method chaining.</returns>
        /// <remarks>
        /// <para>
        /// Options are resolved in this order, first match wins:
        /// </para>
        /// <list type="number">
        ///   <item><description>A <see cref="KurrentClientOptions"/> instance registered in the service provider.</description></item>
        ///   <item><description>A connection string at <c>ConnectionStrings:KurrentDB</c> in <see cref="IConfiguration"/>.</description></item>
        /// </list>
        /// <para>
        /// If neither is available, <see cref="KurrentClientNotConfiguredException"/> is thrown when
        /// the client is first resolved. Falling back to defaults is intentionally not supported here —
        /// silently connecting to <c>localhost:2113</c> would mask a real misconfiguration in any
        /// non-development environment. Callers wanting defaults should pass a connection string
        /// explicitly via the <see cref="AddKurrentClient(IServiceCollection, string)"/> overload, or
        /// register a <see cref="KurrentClientOptions"/> instance.
        /// </para>
        /// <para>
        /// After resolution, service-provider overrides (<see cref="MessageTypeMapper"/>,
        /// <see cref="ILoggerFactory"/>, <see cref="IMetadataDecoder"/>) are applied, then
        /// <paramref name="configure"/> runs.
        /// </para>
        /// </remarks>
        /// <exception cref="KurrentClientNotConfiguredException">
        /// Thrown on first resolution when neither a registered <see cref="KurrentClientOptions"/> nor
        /// a <c>ConnectionStrings:KurrentDB</c> entry can be found.
        /// </exception>
        public IServiceCollection AddKurrentClient(Action<KurrentClientOptions>? configure = null) {
            services.TryAddSingleton(sp => {
                var options = sp.GetService<KurrentClientOptions>();
                var config  = sp.GetService<IConfiguration>();

                options ??= config?.GetConnectionString("KurrentDB") is { } connString
                    ? KurrentClientOptions.Parse(connString)
                    : throw new KurrentClientNotConfiguredException();

                ResolveServices(options, sp);

                configure?.Invoke(options);

                return KurrentClient.Create(options);
            });

            return services;
        }

        /// <summary>
        /// Registers a <see cref="KurrentClient"/> from a connection string.
        /// </summary>
        /// <param name="connectionString">
        /// KurrentDB connection string (e.g., <c>kurrentdb://localhost:2113</c>).
        /// </param>
        /// <returns>The service collection for method chaining.</returns>
        /// <remarks>
        /// The string is parsed into <see cref="KurrentClientOptions"/> and delegated to the
        /// <see cref="AddKurrentClient(IServiceCollection, KurrentClientOptions)"/> overload,
        /// so service-provider overrides still apply.
        /// </remarks>
        public IServiceCollection AddKurrentClient(string connectionString) =>
            services.AddKurrentClient(KurrentClientOptions.Parse(connectionString));

        /// <summary>
        /// Registers a <see cref="KurrentClient"/> built from a pre-constructed
        /// <see cref="KurrentClientOptions"/> instance.
        /// </summary>
        /// <param name="options">
        /// The options to use. Validated immediately — throws if invalid, so misconfiguration
        /// surfaces at registration time rather than on first use.
        /// </param>
        /// <returns>The service collection for method chaining.</returns>
        /// <remarks>
        /// Service-provider overrides (<see cref="MessageTypeMapper"/>, <see cref="ILoggerFactory"/>,
        /// <see cref="IMetadataDecoder"/>) are still applied before the client is built, overriding
        /// whatever is set on the supplied <paramref name="options"/>.
        /// </remarks>
        public IServiceCollection AddKurrentClient(KurrentClientOptions options) {
            options.EnsureValid();
            services.TryAddSingleton(sp => KurrentClient.Create(ResolveServices(options, sp)));
            return services;
        }
    }

    /// <summary>
    /// Applies service-provider-resolved services to the options as overrides.
    /// Each service only overrides the corresponding option if it is registered.
    /// Internal so the <see cref="IKurrentBuilder"/> connection path applies the
    /// exact same override pipeline.
    /// </summary>
    internal static KurrentClientOptions ResolveServices(KurrentClientOptions options, IServiceProvider sp) {
        // Container-registered catalogs join the client's selection; they compose with
        // (rather than replace) attribute-declared and auto-discovered catalogs.
        options.Messages.UseCatalog(sp.GetServices<MessageCatalog>());

        if (sp.GetService<ILoggerFactory>() is { } loggerFactory)
            options.LoggerFactory = loggerFactory;

        if (sp.GetService<IMetadataDecoder>() is { } metadataDecoder)
            options.MetadataDecoder = metadataDecoder;

        return options;
    }
}
