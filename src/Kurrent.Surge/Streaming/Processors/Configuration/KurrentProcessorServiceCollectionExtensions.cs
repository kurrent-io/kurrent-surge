using System.Diagnostics.Metrics;
using Kurrent.Surge.Client;
using Kurrent.Surge.Schema.Naming;
using Kurrent.Surge.Streaming;
using Kurrent.Surge.Streaming.Diagnostics.Metrics;
using Kurrent.Surge.Streaming.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kurrent.Processors.Configuration;

/// <summary>
/// Extension methods for registering Kurrent processors with dependency injection.
/// </summary>
/// <remarks>
/// <para>
/// Every call registers a dedicated <see cref="DroneService{TProcessor}"/>. Each processor
/// owns its own <see cref="Client.KurrentClient"/> — client instances are never shared across
/// processors. Duplicate <c>AddKurrentProcessor&lt;T&gt;</c> / <c>AddKurrentBatchProcessor&lt;T&gt;</c>
/// calls for the same <c>T</c> are no-ops (first-wins) — deduped by <c>DroneService&lt;T&gt;</c>'s
/// closed type.
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
/// <c>ConnectionStrings:{ProcessorId}</c> falls back to <c>ConnectionStrings:kurrentdb</c> (matching
/// the default Aspire resource name). If neither is set, <see cref="KurrentProcessor.Build"/> throws.
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

            // 4. If still no connection, try resolving KurrentClientOptions directly from DI,
            //    then fall back to IConfiguration lookups by processor id / Aspire resource name.
            if (options.ClientOptions is null) {
                var clientOptions = sp.GetService<KurrentClientOptions>();
                if (clientOptions is not null)
                    options.Connection(clientOptions);
                else if (configuration is not null)
                    ResolveClientOptions(options, configuration);
            }

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

    #region ->> Batch — Bare overloads (user must register TProcessor in DI) <<-

    /// <summary>
    /// Registers a batch processor as a hosted service. <typeparamref name="TProcessor"/> must be
    /// registered separately via <c>services.AddSingleton&lt;TProcessor&gt;()</c> or equivalent —
    /// it is resolved at drone startup via <c>sp.GetRequiredService&lt;TProcessor&gt;()</c>.
    /// </summary>
    public static IServiceCollection AddKurrentBatchProcessor<TProcessor>(
        this IServiceCollection services,
        Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordBatchProcessor =>
        services.AddKurrentBatchProcessor<TProcessor>(
            static sp => sp.GetRequiredService<TProcessor>(),
            (opts, _) => configure?.Invoke(opts));

    /// <summary>
    /// Registers a batch processor as a hosted service with <see cref="IServiceProvider"/>-aware configure.
    /// <typeparamref name="TProcessor"/> must be registered separately in DI.
    /// </summary>
    public static IServiceCollection AddKurrentBatchProcessor<TProcessor>(
        this IServiceCollection services,
        Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordBatchProcessor =>
        services.AddKurrentBatchProcessor<TProcessor>(
            static sp => sp.GetRequiredService<TProcessor>(),
            configure);

    #endregion

    #region ->> Batch — Factory overloads <<-

    /// <summary>
    /// Registers a batch processor as a hosted service using a factory. The factory is invoked once
    /// per drone at host startup; the processor type is not added to the DI container.
    /// </summary>
    public static IServiceCollection AddKurrentBatchProcessor<TProcessor>(
        this IServiceCollection services,
        Func<IServiceProvider, TProcessor> processorFactory,
        Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordBatchProcessor =>
        services.AddKurrentBatchProcessor(processorFactory, (opts, _) => configure?.Invoke(opts));

    /// <summary>
    /// Registers a batch processor as a hosted service using a factory with <see cref="IServiceProvider"/>-aware configure.
    /// </summary>
    public static IServiceCollection AddKurrentBatchProcessor<TProcessor>(
        this IServiceCollection services,
        Func<IServiceProvider, TProcessor> processorFactory,
        Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordBatchProcessor {
        EnsureDiagnosticsRegistered(services);

        services.TryRegisterHostedService<DroneService<TProcessor>>(sp => {
            var configuration = sp.GetService<IConfiguration>();
            var options       = new ProcessorOptions();

            if (configuration is not null)
                options.FromConfiguration(configuration, typeof(TProcessor).Name);

            configure(options, sp);

            if (string.IsNullOrWhiteSpace(options.ProcessorId))
                options.ProcessorId = typeof(TProcessor).Name.ToKebabCase();

            if (options.ClientOptions is null) {
                var clientOptions = sp.GetService<KurrentClientOptions>();
                if (clientOptions is not null)
                    options.Connection(clientOptions);
                else if (configuration is not null)
                    ResolveClientOptions(options, configuration);
            }

            var processor = processorFactory(sp);
            var drone     = new KurrentProcessor(options, processor).Build();
            var logger    = sp.GetRequiredService<ILogger<DroneService<TProcessor>>>();
            return new DroneService<TProcessor>(drone, logger);
        });

        return services;
    }

    #endregion

    #region ->> Batch — Instance overloads <<-

    /// <summary>
    /// Registers a batch processor as a hosted service using a pre-built instance.
    /// </summary>
    public static IServiceCollection AddKurrentBatchProcessor<TProcessor>(
        this IServiceCollection services,
        TProcessor instance,
        Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordBatchProcessor =>
        services.AddKurrentBatchProcessor(_ => instance, configure);

    /// <summary>
    /// Registers a batch processor as a hosted service using a pre-built instance with
    /// <see cref="IServiceProvider"/>-aware configure.
    /// </summary>
    public static IServiceCollection AddKurrentBatchProcessor<TProcessor>(
        this IServiceCollection services,
        TProcessor instance,
        Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordBatchProcessor =>
        services.AddKurrentBatchProcessor(_ => instance, configure);

    #endregion

    static void EnsureDiagnosticsRegistered(IServiceCollection services) {
        services.TryRegister(sp => {
            var meterFactory = sp.GetRequiredService<IMeterFactory>();
            return new DroneMetricsObserver(meterFactory).Start();
        });
    }

    /// <summary>
    /// Looks up the connection string under <c>ConnectionStrings:{ProcessorId}</c> first, then
    /// <c>ConnectionStrings:kurrentdb</c> (the default Aspire resource name). If neither is set,
    /// <see cref="KurrentProcessor.Build"/> throws.
    /// </summary>
    static void ResolveClientOptions(ProcessorOptions options, IConfiguration configuration) {
        var connectionString = configuration.GetConnectionString(options.ProcessorId)
                            ?? configuration.GetConnectionString("kurrentdb");

        if (!string.IsNullOrWhiteSpace(connectionString))
            options.Connection(connectionString);
    }
}
