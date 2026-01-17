using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Kurrent.Client.Streams;

/// <summary>
/// Extension methods for registering Kurrent processors with dependency injection.
/// </summary>
[PublicAPI]
public static class KurrentProcessorServiceCollectionExtensions {
    /// <summary>
    /// Registers a processor as a hosted service.
    /// </summary>
    /// <typeparam name="TProcessor">
    /// The processor type implementing <see cref="IRecordProcessor"/>.
    /// Must have a public constructor for DI resolution.
    /// </typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Optional configuration for the processor's hosting options.
    /// </param>
    /// <returns>The service collection for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// The processor is registered as a singleton and will be resolved from DI,
    /// allowing constructor injection of dependencies like <c>ILogger&lt;T&gt;</c>,
    /// database connections, etc.
    /// </para>
    /// <para>
    /// If no client is configured via <see cref="KurrentProcessorHostBuilder.UseClient(string)"/>
    /// or <see cref="KurrentProcessorHostBuilder.ConfigureClient"/>, the processor will use
    /// the shared client registered via <see cref="KurrentClientServiceCollectionExtensions.AddKurrentClient"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Simple registration with shared client
    /// builder.Services.AddKurrentClient("kurrentdb://localhost:2113");
    /// builder.Services.AddKurrentProcessor&lt;TransferProcessor&gt;(config => config
    ///     .WithFilter("schema_name = 'PlayerTransferred'")
    /// );
    ///
    /// // With checkpoint and batching configuration
    /// builder.Services.AddKurrentProcessor&lt;AnalyticsProcessor&gt;(config => config
    ///     .WithFilter("schema_name IN ('OrderPlaced', 'OrderShipped')")
    ///     .ConfigureCheckpoint(c => c with { Interval = 10_000, Threshold = 100 })
    ///     .ConfigureBatching(b => b with { MaxSize = 50 })
    /// );
    ///
    /// // With dedicated client
    /// builder.Services.AddKurrentProcessor&lt;SecureProcessor&gt;(config => config
    ///     .UseClient("kurrentdb://secure-cluster:2113")
    ///     .WithFilter("stream_id LIKE 'secure-%'")
    /// );
    /// </code>
    /// </example>
    public static IServiceCollection AddKurrentProcessor<TProcessor>(
        this IServiceCollection services,
        Action<KurrentProcessorHostBuilder>? configure = null)
        where TProcessor : class, IRecordProcessor {
        // Idempotent - skip if this processor type is already registered
        if (services.Any(d => d.ServiceType == typeof(KurrentProcessorRegistrationMarker<TProcessor>))) {
            return services;
        }
        services.AddSingleton<KurrentProcessorRegistrationMarker<TProcessor>>();

        // Register processor if not already registered
        services.TryAddSingleton<TProcessor>();

        return RegisterHostedService<TProcessor>(services, configure);
    }

    /// <summary>
    /// Registers a processor using a factory delegate for custom instantiation.
    /// </summary>
    /// <typeparam name="TProcessor">
    /// The processor type implementing <see cref="IRecordProcessor"/>.
    /// </typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="processorFactory">
    /// Factory function to create the processor instance.
    /// Receives the <see cref="IServiceProvider"/> for resolving dependencies.
    /// </param>
    /// <param name="configure">
    /// Optional configuration for the processor's hosting options.
    /// </param>
    /// <returns>The service collection for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Use this overload when you need custom instantiation logic, such as
    /// passing specific constructor arguments or configuring the processor
    /// before registration.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddKurrentProcessor&lt;PlayerCacheProcessor&gt;(
    ///     sp => new PlayerCacheProcessor(
    ///         sp.GetRequiredService&lt;IConnectionMultiplexer&gt;(),
    ///         sp.GetRequiredService&lt;ILogger&lt;PlayerCacheProcessor&gt;&gt;()
    ///     ),
    ///     config => config.WithFilter("schema_name LIKE 'Player%'")
    /// );
    /// </code>
    /// </example>
    public static IServiceCollection AddKurrentProcessor<TProcessor>(
        this IServiceCollection services,
        Func<IServiceProvider, TProcessor> processorFactory,
        Action<KurrentProcessorHostBuilder>? configure = null)
        where TProcessor : class, IRecordProcessor {
        // Idempotent - skip if this processor type is already registered
        if (services.Any(d => d.ServiceType == typeof(KurrentProcessorRegistrationMarker<TProcessor>))) {
            return services;
        }
        services.AddSingleton<KurrentProcessorRegistrationMarker<TProcessor>>();

        // Register processor via factory
        services.AddSingleton(processorFactory);

        return RegisterHostedService<TProcessor>(services, configure);
    }

    /// <summary>
    /// Registers multiple processors with shared connection settings.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Configuration for the processors builder, which allows setting
    /// a shared connection and adding multiple processors.
    /// </param>
    /// <returns>The service collection for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// This is a convenience method for registering multiple processors that
    /// share the same KurrentDB connection.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddKurrentProcessors(kurrent => kurrent
    ///     .ConnectTo("kurrentdb://localhost:2113")
    ///     .AddProcessor&lt;TransferProcessor&gt;(c => c.WithFilter("schema_name = 'PlayerTransferred'"))
    ///     .AddProcessor&lt;PlayerCacheProcessor&gt;(c => c.WithFilter("schema_name LIKE 'Player%'"))
    /// );
    /// </code>
    /// </example>
    public static IServiceCollection AddKurrentProcessors(
        this IServiceCollection services,
        Action<KurrentProcessorsBuilder> configure) {
        var builder = new KurrentProcessorsBuilder(services);
        configure(builder);
        return services;
    }

    /// <summary>
    /// Registers a processor with access to <see cref="IServiceProvider"/> during configuration.
    /// </summary>
    /// <typeparam name="TProcessor">
    /// The processor type implementing <see cref="IRecordProcessor"/>.
    /// Must have a public constructor for DI resolution.
    /// </typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Configuration delegate that receives both the builder and the service provider.
    /// Use this overload when configuration depends on other registered services.
    /// </param>
    /// <returns>The service collection for method chaining.</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddKurrentProcessor&lt;MyProcessor&gt;((config, sp) => {
    ///     var settings = sp.GetRequiredService&lt;IOptions&lt;MySettings&gt;&gt;().Value;
    ///     config.WithFilter(settings.FilterExpression);
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddKurrentProcessor<TProcessor>(
        this IServiceCollection services,
        Action<KurrentProcessorHostBuilder, IServiceProvider> configure)
        where TProcessor : class, IRecordProcessor {
        // Idempotent - skip if this processor type is already registered
        if (services.Any(d => d.ServiceType == typeof(KurrentProcessorRegistrationMarker<TProcessor>))) {
            return services;
        }
        services.AddSingleton<KurrentProcessorRegistrationMarker<TProcessor>>();

        // Register processor if not already registered
        services.TryAddSingleton<TProcessor>();

        return RegisterHostedService<TProcessor>(services, configure);
    }

    /// <summary>
    /// Registers a processor using a factory delegate with access to <see cref="IServiceProvider"/> during configuration.
    /// </summary>
    /// <typeparam name="TProcessor">
    /// The processor type implementing <see cref="IRecordProcessor"/>.
    /// </typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="processorFactory">
    /// Factory function to create the processor instance.
    /// Receives the <see cref="IServiceProvider"/> for resolving dependencies.
    /// </param>
    /// <param name="configure">
    /// Configuration delegate that receives both the builder and the service provider.
    /// Use this overload when configuration depends on other registered services.
    /// </param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddKurrentProcessor<TProcessor>(
        this IServiceCollection services,
        Func<IServiceProvider, TProcessor> processorFactory,
        Action<KurrentProcessorHostBuilder, IServiceProvider> configure)
        where TProcessor : class, IRecordProcessor {
        // Idempotent - skip if this processor type is already registered
        if (services.Any(d => d.ServiceType == typeof(KurrentProcessorRegistrationMarker<TProcessor>))) {
            return services;
        }
        services.AddSingleton<KurrentProcessorRegistrationMarker<TProcessor>>();

        // Register processor via factory
        services.AddSingleton(processorFactory);

        return RegisterHostedService<TProcessor>(services, configure);
    }

    static IServiceCollection RegisterHostedService<TProcessor>(
        IServiceCollection services,
        Action<KurrentProcessorHostBuilder>? configure)
        where TProcessor : class, IRecordProcessor {
        services.AddHostedService<KurrentProcessorHostedService<TProcessor>>(sp => {
            // Configuration deferred to resolution time
            var configuration = sp.GetService<IConfiguration>();
            var builder = new KurrentProcessorHostBuilder();

            // 1. Bind from config FIRST (base settings)
            builder.BindFromConfiguration(configuration, typeof(TProcessor).Name);

            // 2. Apply user's configuration SECOND (overrides)
            configure?.Invoke(builder);

            var registration = builder.Build<TProcessor>();
            return CreateHostedService<TProcessor>(registration, sp);
        });

        return services;
    }

    static IServiceCollection RegisterHostedService<TProcessor>(
        IServiceCollection services,
        Action<KurrentProcessorHostBuilder, IServiceProvider> configure)
        where TProcessor : class, IRecordProcessor {
        services.AddHostedService<KurrentProcessorHostedService<TProcessor>>(sp => {
            // Configuration deferred to resolution time with IServiceProvider access
            var configuration = sp.GetService<IConfiguration>();
            var builder = new KurrentProcessorHostBuilder();

            // 1. Bind from config FIRST (base settings)
            builder.BindFromConfiguration(configuration, typeof(TProcessor).Name);

            // 2. Apply user's configuration SECOND (overrides)
            configure(builder, sp);

            var registration = builder.Build<TProcessor>();
            return CreateHostedService<TProcessor>(registration, sp);
        });

        return services;
    }

    static KurrentProcessorHostedService<TProcessor> CreateHostedService<TProcessor>(
        KurrentProcessorRegistration<TProcessor> registration,
        IServiceProvider sp)
        where TProcessor : class, IRecordProcessor {
        var processor = sp.GetRequiredService<TProcessor>();
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var client = BuildClient(registration, sp);
        var worker = new ProcessorWorker(client, processor, registration.Settings, loggerFactory, registration.UseOwnClient);
        var logger = sp.GetRequiredService<ILogger<KurrentProcessorHostedService<TProcessor>>>();
        return new KurrentProcessorHostedService<TProcessor>(worker, logger);
    }

    static KurrentClient BuildClient<TProcessor>(
        KurrentProcessorRegistration<TProcessor> registration,
        IServiceProvider sp)
        where TProcessor : IRecordProcessor {

        if (registration.UseOwnClient) {
            if (registration.ConnectionString is not null && registration.ClientConfigure is not null) {
                var options = KurrentClientOptions.Parse(registration.ConnectionString);
                return new KurrentClient(registration.ClientConfigure(options));
            }

            if (registration.ConnectionString is not null) {
                return KurrentClient.Create(registration.ConnectionString);
            }

            if (registration.ClientConfigure is not null) {
                return new KurrentClient(registration.ClientConfigure(new KurrentClientOptions()));
            }
        }

        // Try to use shared client from DI
        var sharedClient = sp.GetService<KurrentClient>();
        if (sharedClient is not null) {
            return sharedClient;
        }

        throw new InvalidOperationException(
            $"No KurrentClient configured for processor {typeof(TProcessor).Name}. " +
            $"Either call AddKurrentClient() to register a shared client, " +
            $"or use UseClient()/ConfigureClient() in AddKurrentProcessor<{typeof(TProcessor).Name}>()."
        );
    }
}

/// <summary>
/// Internal marker to track registered processor types and prevent duplicate registrations.
/// </summary>
internal sealed class KurrentProcessorRegistrationMarker<TProcessor>
    where TProcessor : IRecordProcessor;
