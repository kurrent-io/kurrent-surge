using System.Diagnostics.Metrics;
using Kurrent.Client;
using Kurrent.Client.Schema.NameStrategies;
using Kurrent.Processors.Diagnostics.Metrics;
using Kurrent.Processors.Hosting;
using Kurrent.Processors.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kurrent.Processors;

/// <summary>
/// Extension methods for registering Kurrent processors with dependency injection.
/// </summary>
/// <remarks>
/// <para>
/// Every call registers a dedicated <see cref="DroneService{TProcessor}"/>. Each processor
/// owns its own <see cref="Client.KurrentClient"/> — client instances are never shared across
/// processors. Duplicate <c>AddKurrentProcessor&lt;T&gt;</c> calls for the same <c>T</c> are no-ops
/// (first-wins) — deduped by <c>DroneService&lt;T&gt;</c>'s closed type.
/// </para>
/// <para>
/// <b>Processor resolution:</b>
/// </para>
/// <list type="bullet">
/// <item>Instance overloads use the supplied instance directly.</item>
/// <item>Factory overloads invoke the supplied factory once per drone at host startup.</item>
/// <item>Bare overloads (no factory, no instance) resolve <typeparamref name="TProcessor"/> via
/// <c>sp.GetRequiredService&lt;TProcessor&gt;()</c> at drone startup — the user is responsible
/// for registering the processor in DI.</item>
/// </list>
/// <para>
/// <b>ClientOptions resolution</b> (when <c>opts.Connection(...)</c> is not called in the configure lambda):
/// <see cref="Client.KurrentClientOptions"/> registered in DI → <c>ConnectionStrings:{ProcessorId}</c>
/// → <c>ConnectionStrings:Default</c> → <c>Kurrent:Client</c> section → throw.
/// </para>
/// <para>
/// <b>AOT:</b> all overloads use closed generic types at the callsite and perform no
/// <c>ActivatorUtilities</c>-based reflection, so the extension surface is native-AOT safe.
/// </para>
/// </remarks>
[PublicAPI]
public static class KurrentProcessorServiceCollectionExtensions {
    #region ->> Bare overloads (user must register TProcessor in DI) <<-

    /// <summary>
    /// Registers a processor as a hosted service. <typeparamref name="TProcessor"/> must be
    /// registered separately via <c>services.AddSingleton&lt;TProcessor&gt;()</c> or equivalent —
    /// it is resolved at drone startup via <c>sp.GetRequiredService&lt;TProcessor&gt;()</c>.
    /// </summary>
    public static IServiceCollection AddKurrentProcessor<TProcessor>(
        this IServiceCollection services,
        Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordProcessor =>
        services.AddKurrentProcessor<TProcessor>(
            static sp => sp.GetRequiredService<TProcessor>(),
            (opts, _) => configure?.Invoke(opts));

    /// <summary>
    /// Registers a processor as a hosted service with <see cref="IServiceProvider"/>-aware configure.
    /// <typeparamref name="TProcessor"/> must be registered separately in DI.
    /// </summary>
    public static IServiceCollection AddKurrentProcessor<TProcessor>(
        this IServiceCollection services,
        Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordProcessor =>
        services.AddKurrentProcessor<TProcessor>(
            static sp => sp.GetRequiredService<TProcessor>(),
            configure);

    #endregion

    #region ->> Factory overloads <<-

    /// <summary>
    /// Registers a processor as a hosted service using a factory. The factory is invoked once
    /// per drone at host startup; the processor type is not added to the DI container.
    /// </summary>
    public static IServiceCollection AddKurrentProcessor<TProcessor>(
        this IServiceCollection services,
        Func<IServiceProvider, TProcessor> processorFactory,
        Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordProcessor =>
        services.AddKurrentProcessor(processorFactory, (opts, _) => configure?.Invoke(opts));

    /// <summary>
    /// Registers a processor as a hosted service using a factory with <see cref="IServiceProvider"/>-aware configure.
    /// </summary>
    public static IServiceCollection AddKurrentProcessor<TProcessor>(
        this IServiceCollection services,
        Func<IServiceProvider, TProcessor> processorFactory,
        Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordProcessor {
        EnsureDiagnosticsRegistered(services);

        services.TryRegisterHostedService<DroneService<TProcessor>>(sp => {
            var configuration = sp.GetService<IConfiguration>();
            var options       = new ProcessorOptions();

            // 1. Bind config under Kurrent:Processors:{TypeName} first (base settings).
            if (configuration is not null)
                options.FromConfiguration(configuration, typeof(TProcessor).Name);

            // 2. Apply user configuration (overrides).
            configure(options, sp);

            // 3. Ensure ProcessorId is set before resolving connection strings by id.
            if (string.IsNullOrWhiteSpace(options.ProcessorId))
                options.ProcessorId = typeof(TProcessor).Name.ToKebabCase();

            // 4. If still no connection, resolve from DI + IConfiguration as a fallback chain.
            if (options.ClientOptions is null)
                ResolveClientOptions(options, sp, configuration);

            // 4. Resolve the processor instance — factory fires once, at host startup.
            var processor = processorFactory(sp);

            // 5. Build the drone eagerly so connection/validation errors surface at host startup.
            var drone  = new KurrentProcessor(options, processor).Build();
            var logger = sp.GetRequiredService<ILogger<DroneService<TProcessor>>>();
            return new DroneService<TProcessor>(drone, logger);
        });

        return services;
    }

    #endregion

    #region ->> Instance overloads <<-

    /// <summary>
    /// Registers a processor as a hosted service using a pre-built instance. The same instance
    /// is handed to the drone at startup; the processor type is not added to the DI container.
    /// </summary>
    public static IServiceCollection AddKurrentProcessor<TProcessor>(
        this IServiceCollection services,
        TProcessor instance,
        Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordProcessor =>
        services.AddKurrentProcessor(_ => instance, configure);

    /// <summary>
    /// Registers a processor as a hosted service using a pre-built instance with
    /// <see cref="IServiceProvider"/>-aware configure.
    /// </summary>
    public static IServiceCollection AddKurrentProcessor<TProcessor>(
        this IServiceCollection services,
        TProcessor instance,
        Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordProcessor =>
        services.AddKurrentProcessor(_ => instance, configure);

    #endregion

    static void EnsureDiagnosticsRegistered(IServiceCollection services) {
        services.TryRegister(sp => {
            var meterFactory = sp.GetRequiredService<IMeterFactory>();
            return new DroneMetricsObserver(meterFactory).Start();
        });
    }

    /// <summary>
    /// Fallback resolution chain for <see cref="KurrentClientOptions"/> when the user did
    /// not call <c>opts.Connection(...)</c> in their configure delegate:
    /// <list type="number">
    /// <item>Cloned <see cref="KurrentClientOptions"/> registered in the service provider.</item>
    /// <item><c>ConnectionStrings:{ProcessorId}</c>.</item>
    /// <item><c>ConnectionStrings:Default</c>.</item>
    /// <item><c>Kurrent:Client</c> configuration section.</item>
    /// </list>
    /// If none resolve, <see cref="KurrentProcessor.Build"/> throws.
    /// </summary>
    static void ResolveClientOptions(ProcessorOptions options, IServiceProvider services, IConfiguration? configuration) {
        var templateOptions = services.GetService<KurrentClientOptions>();
        if (templateOptions is not null) {
            options.Connection(templateOptions with { });
            return;
        }

        if (configuration is null)
            return;

        var connectionString = configuration.GetConnectionString(options.ProcessorId)
                            ?? configuration.GetConnectionString("Default");

        if (!string.IsNullOrWhiteSpace(connectionString)) {
            options.Connection(connectionString);
            return;
        }

        var clientSection = configuration.GetSection("Kurrent:Client");
        if (clientSection.Exists()) {
            var clientOptions        = new KurrentClientOptions();
            var clientConnectionString = clientSection["ConnectionString"];
            if (!string.IsNullOrWhiteSpace(clientConnectionString)) {
                var parsed = KurrentClientOptions.Parse(clientConnectionString);
                clientOptions.ConnectionName           = parsed.ConnectionName;
                clientOptions.ConnectionScheme         = parsed.ConnectionScheme;
                clientOptions.Endpoints                = parsed.Endpoints;
                clientOptions.Security                 = parsed.Security;
                clientOptions.Resilience               = parsed.Resilience;
                clientOptions.Gossip                   = parsed.Gossip;
                clientOptions.OriginalConnectionString = parsed.OriginalConnectionString;
            }

            clientSection.Bind(clientOptions, o => o.BindNonPublicProperties = true);
            options.Connection(clientOptions);
        }
    }
}
