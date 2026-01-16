using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kurrent.Client.Streams;

/// <summary>
/// Extension methods for registering <see cref="KurrentClient"/> with dependency injection.
/// </summary>
[PublicAPI]
public static class KurrentClientServiceCollectionExtensions {
    /// <summary>
    /// Registers a shared <see cref="KurrentClient"/> from configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Loads client options from the <c>Kurrent:Client</c> configuration section.
    /// If no configuration is registered or section doesn't exist, uses default options.
    /// </para>
    /// <para>
    /// Uses <c>TryAddSingleton</c> - if a client is already registered, this call is ignored.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // With appsettings.json:
    /// // { "Kurrent": { "Client": { "ConnectionString": "kurrentdb://localhost:2113" } } }
    /// builder.Services.AddKurrentClient();
    /// </code>
    /// </example>
    public static IServiceCollection AddKurrentClient(this IServiceCollection services) {
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
    /// Registers a shared <see cref="KurrentClient"/> that all processors can use.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">
    /// KurrentDB connection string (e.g., "kurrentdb://localhost:2113").
    /// </param>
    /// <returns>The service collection for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// The registered client is shared across all processors that don't specify their own
    /// client via <see cref="KurrentProcessorHostBuilder.UseClient(string)"/> or
    /// <see cref="KurrentProcessorHostBuilder.ConfigureClient"/>.
    /// </para>
    /// <para>
    /// Uses <c>TryAddSingleton</c> - if a client is already registered, this call is ignored.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddKurrentClient("kurrentdb://localhost:2113");
    ///
    /// builder.Services.AddKurrentProcessor&lt;MyProcessor&gt;(config => config
    ///     .WithFilter("schema_name = 'OrderPlaced'")
    /// );
    /// </code>
    /// </example>
    public static IServiceCollection AddKurrentClient(
        this IServiceCollection services,
        string connectionString) {
        services.TryAddSingleton(_ => KurrentClient.Create(connectionString));
        return services;
    }

    /// <summary>
    /// Registers a shared <see cref="KurrentClient"/> with custom configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Function to configure client options starting from a parsed connection string
    /// or default options.
    /// </param>
    /// <returns>The service collection for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Use this overload when you need to customize client options beyond what the
    /// connection string provides.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddKurrentClient(opts =>
    ///     KurrentClientOptions.Parse("kurrentdb://localhost:2113") with {
    ///         Resilience = KurrentClientResilienceOptions.HighAvailability
    ///     }
    /// );
    /// </code>
    /// </example>
    public static IServiceCollection AddKurrentClient(
        this IServiceCollection services,
        Func<KurrentClientOptions> configure) {
        services.TryAddSingleton(_ => new KurrentClient(configure()));
        return services;
    }

    /// <summary>
    /// Registers a shared <see cref="KurrentClient"/> using the options builder.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Action to configure the client options builder.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Configuration is applied in layers: settings from <c>Kurrent:Client</c> configuration
    /// section are loaded first, then the configure action is applied as overrides.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddKurrentClient(builder => builder
    ///     .WithResilience(KurrentClientResilienceOptions.HighAvailability)
    /// );
    /// </code>
    /// </example>
    public static IServiceCollection AddKurrentClient(
        this IServiceCollection services,
        Action<KurrentClientOptionsBuilder> configure) {
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
    /// Registers a shared <see cref="KurrentClient"/> with service provider access.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Action to configure the client with access to the service provider.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Use this overload when you need to resolve other services during configuration.
    /// Configuration is applied in layers: settings from <c>Kurrent:Client</c> configuration
    /// section are loaded first, then the configure action is applied as overrides.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddKurrentClient((builder, sp) => {
    ///     var loggerFactory = sp.GetRequiredService&lt;ILoggerFactory&gt;();
    ///     builder.WithLoggerFactory(loggerFactory);
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddKurrentClient(
        this IServiceCollection services,
        Action<KurrentClientOptionsBuilder, IServiceProvider> configure) {
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
