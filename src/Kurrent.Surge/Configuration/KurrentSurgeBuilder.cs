using Kurrent.Toolkit;
using System.Diagnostics.Metrics;
using Kurrent.Surge.Processors.Configuration;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema.Naming;
using Kurrent.Surge.Processors;
using Kurrent.Surge.Processors.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kurrent.Surge;

sealed class KurrentSurgeBuilder(IServiceCollection services) : IKurrentSurgeBuilder {
    public IServiceCollection Services { get; } = services;

    #region ->> Connection <<-

    public IKurrentSurgeBuilder Connection(string connectionString) =>
        Connection(connectionString, static _ => { });

    public IKurrentSurgeBuilder Connection(string connectionString, Action<KurrentClientOptions> configure) =>
        ConnectionCore(_ => KurrentClientOptions.Parse(connectionString), configure);

    public IKurrentSurgeBuilder Connection(KurrentClientOptions options) {
        options.EnsureValid();
        Services.Replace(ServiceDescriptor.Singleton(sp => ResolveServices(options, sp)));
        RegisterClient();
        return this;
    }

    // Seeds from configuration because no connection string was supplied: the caller is tuning the
    // configured connection, not declaring a new one. Falls back to blank options when nothing is
    // configured, so the delegate can supply the connection itself.
    public IKurrentSurgeBuilder Connection(Action<KurrentClientOptions> configure) =>
        ConnectionCore(SeedFromConfiguration, configure);

    IKurrentSurgeBuilder ConnectionCore(
        Func<IServiceProvider, KurrentClientOptions> seed,
        Action<KurrentClientOptions> configure
    ) {
        Services.Replace(ServiceDescriptor.Singleton(sp => {
            var options = ResolveServices(seed(sp), sp);
            configure(options);
            options.EnsureValid();
            return options;
        }));

        RegisterClient();
        return this;
    }

    static KurrentClientOptions SeedFromConfiguration(IServiceProvider sp) =>
        OptionsFromConfiguration(sp) ?? new KurrentClientOptions();

    void RegisterClient() =>
        Services.Replace(ServiceDescriptor.Singleton(sp =>
            KurrentClient.Create(sp.GetRequiredService<KurrentClientOptions>())));

    /// <summary>
    /// Registers the client the scope falls back to when <c>Connection</c> is never called: options
    /// already in the container, else the configuration chain. Uses <c>TryAdd</c>, so an explicit
    /// <c>Connection</c> call replaces it.
    /// </summary>
    /// <remarks>
    /// Only the client is registered, never <see cref="KurrentClientOptions"/>. Components probe for
    /// the scope's options with <c>GetService</c>, and a factory registered under that type would
    /// have to throw when nothing is configured — turning an absent connection into an exception
    /// during a probe, and masking the component's own configuration key.
    /// </remarks>
    internal void RegisterDefaultConnection() =>
        Services.TryAddSingleton(sp => {
            var options = sp.GetService<KurrentClientOptions>()
                       ?? OptionsFromConfiguration(sp)
                       ?? throw new KurrentClientNotConfiguredException();

            ResolveServices(options, sp);
            options.EnsureValid();

            return KurrentClient.Create(options);
        });

    /// <summary>
    /// Builds client options from configuration: the <c>Kurrent:Surge</c> section first, then the
    /// shared <c>ConnectionStrings</c> entries when that section supplied no connection.
    /// </summary>
    static KurrentClientOptions? OptionsFromConfiguration(IServiceProvider sp) {
        if (sp.GetService<IConfiguration>() is not { } configuration)
            return null;

        var section = configuration.GetSection(KurrentSurgeConfigurationReader.SectionPath);

        if (section.Exists())
            return KurrentSurgeConfigurationReader.ReadClientOptions(configuration).EnsureValid(ConfigurationLogger(sp));

        return ResolveConnectionString(configuration) is { } connectionString
            ? KurrentClientOptions.Parse(connectionString)
            : null;
    }

    /// <summary>
    /// Applies container-resolved services to the options as overrides. Each only applies when
    /// the corresponding service is registered.
    /// </summary>
    internal static KurrentClientOptions ResolveServices(KurrentClientOptions options, IServiceProvider sp) {
        if (sp.GetService<ILoggerFactory>() is { } loggerFactory)
            options.LoggerFactory = loggerFactory;

        if (sp.GetService<IMetadataDecoder>() is { } metadataDecoder)
            options.MetadataDecoder = metadataDecoder;

        return options;
    }

    #endregion // Connection

    #region ->> Processor <<-

    public IKurrentSurgeBuilder AddProcessor<TProcessor>(Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordProcessor =>
        AddProcessorCore(null, static sp => sp.GetRequiredService<TProcessor>(), (opts, _) => configure?.Invoke(opts));

    public IKurrentSurgeBuilder AddProcessor<TProcessor>(string processorId, Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordProcessor =>
        AddProcessorCore(processorId, static sp => sp.GetRequiredService<TProcessor>(), (opts, _) => configure?.Invoke(opts));

    public IKurrentSurgeBuilder AddProcessor<TProcessor>(Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordProcessor =>
        AddProcessorCore(null, static sp => sp.GetRequiredService<TProcessor>(), configure);

    public IKurrentSurgeBuilder AddProcessor<TProcessor>(string processorId, Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordProcessor =>
        AddProcessorCore(processorId, static sp => sp.GetRequiredService<TProcessor>(), configure);

    public IKurrentSurgeBuilder AddProcessor<TProcessor>(
        Func<IServiceProvider, TProcessor> processorFactory,
        Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordProcessor =>
        AddProcessorCore(null, processorFactory, (opts, _) => configure?.Invoke(opts));

    public IKurrentSurgeBuilder AddProcessor<TProcessor>(
        string processorId,
        Func<IServiceProvider, TProcessor> processorFactory,
        Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordProcessor =>
        AddProcessorCore(processorId, processorFactory, (opts, _) => configure?.Invoke(opts));

    public IKurrentSurgeBuilder AddProcessor<TProcessor>(
        Func<IServiceProvider, TProcessor> processorFactory,
        Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordProcessor =>
        AddProcessorCore(null, processorFactory, configure);

    public IKurrentSurgeBuilder AddProcessor<TProcessor>(
        string processorId,
        Func<IServiceProvider, TProcessor> processorFactory,
        Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordProcessor =>
        AddProcessorCore(processorId, processorFactory, configure);

    public IKurrentSurgeBuilder AddProcessor<TProcessor>(
        TProcessor instance,
        Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordProcessor =>
        AddProcessorCore(null, _ => instance, (opts, _) => configure?.Invoke(opts));

    public IKurrentSurgeBuilder AddProcessor<TProcessor>(
        string processorId,
        TProcessor instance,
        Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordProcessor =>
        AddProcessorCore(processorId, _ => instance, (opts, _) => configure?.Invoke(opts));

    public IKurrentSurgeBuilder AddProcessor<TProcessor>(
        TProcessor instance,
        Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordProcessor =>
        AddProcessorCore(null, _ => instance, configure);

    public IKurrentSurgeBuilder AddProcessor<TProcessor>(
        string processorId,
        TProcessor instance,
        Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordProcessor =>
        AddProcessorCore(processorId, _ => instance, configure);

    /// <summary>
    /// Registers the hosted drone for a processor of either shape. The drone picks the record or
    /// batch dispatcher from the processor's runtime type. The id is <paramref name="processorId"/>,
    /// else the type name in kebab-case.
    /// </summary>
    internal IKurrentSurgeBuilder AddProcessorCore<TProcessor>(
        string? processorId,
        Func<IServiceProvider, TProcessor> processorFactory,
        Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IProcessor {
        Services.TryRegisterHostedService<DroneService<TProcessor>>(sp => {
            var options   = BuildOptions<TProcessor>(sp, processorId, configure);
            var processor = processorFactory(sp);
            var drone     = new KurrentProcessor(options, processor).Build();
            return new DroneService<TProcessor>(drone, sp.GetRequiredService<ILogger<DroneService<TProcessor>>>());
        });

        return this;
    }

    #endregion // Processor

    #region ->> Batch Processor <<-

    public IKurrentSurgeBuilder AddBatchProcessor<TProcessor>(Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordBatchProcessor =>
        AddProcessorCore(null, static sp => sp.GetRequiredService<TProcessor>(), (opts, _) => configure?.Invoke(opts));

    public IKurrentSurgeBuilder AddBatchProcessor<TProcessor>(string processorId, Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordBatchProcessor =>
        AddProcessorCore(processorId, static sp => sp.GetRequiredService<TProcessor>(), (opts, _) => configure?.Invoke(opts));

    public IKurrentSurgeBuilder AddBatchProcessor<TProcessor>(Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordBatchProcessor =>
        AddProcessorCore(null, static sp => sp.GetRequiredService<TProcessor>(), configure);

    public IKurrentSurgeBuilder AddBatchProcessor<TProcessor>(string processorId, Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordBatchProcessor =>
        AddProcessorCore(processorId, static sp => sp.GetRequiredService<TProcessor>(), configure);

    public IKurrentSurgeBuilder AddBatchProcessor<TProcessor>(
        Func<IServiceProvider, TProcessor> processorFactory,
        Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordBatchProcessor =>
        AddProcessorCore(null, processorFactory, (opts, _) => configure?.Invoke(opts));

    public IKurrentSurgeBuilder AddBatchProcessor<TProcessor>(
        string processorId,
        Func<IServiceProvider, TProcessor> processorFactory,
        Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordBatchProcessor =>
        AddProcessorCore(processorId, processorFactory, (opts, _) => configure?.Invoke(opts));

    public IKurrentSurgeBuilder AddBatchProcessor<TProcessor>(
        Func<IServiceProvider, TProcessor> processorFactory,
        Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordBatchProcessor =>
        AddProcessorCore(null, processorFactory, configure);

    public IKurrentSurgeBuilder AddBatchProcessor<TProcessor>(
        string processorId,
        Func<IServiceProvider, TProcessor> processorFactory,
        Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordBatchProcessor =>
        AddProcessorCore(processorId, processorFactory, configure);

    public IKurrentSurgeBuilder AddBatchProcessor<TProcessor>(
        TProcessor instance,
        Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordBatchProcessor =>
        AddProcessorCore(null, _ => instance, (opts, _) => configure?.Invoke(opts));

    public IKurrentSurgeBuilder AddBatchProcessor<TProcessor>(
        string processorId,
        TProcessor instance,
        Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordBatchProcessor =>
        AddProcessorCore(processorId, _ => instance, (opts, _) => configure?.Invoke(opts));

    public IKurrentSurgeBuilder AddBatchProcessor<TProcessor>(
        TProcessor instance,
        Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordBatchProcessor =>
        AddProcessorCore(null, _ => instance, configure);

    public IKurrentSurgeBuilder AddBatchProcessor<TProcessor>(
        string processorId,
        TProcessor instance,
        Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordBatchProcessor =>
        AddProcessorCore(processorId, _ => instance, configure);

    #endregion // Batch Processor

    #region ->> Options resolution <<-

    static ProcessorOptions BuildOptions<TProcessor>(
        IServiceProvider sp,
        string? processorId,
        Action<ProcessorOptions, IServiceProvider> configure
    ) {
        var configuration = sp.GetService<IConfiguration>();
        var id            = processorId ?? typeof(TProcessor).Name.ToKebabCase();
        var options       = new ProcessorOptions { ProcessorId = id };

        // Everything configuration has to say about the processor is on the options before the
        // configure delegate runs, its own connection included.
        if (configuration is not null)
            options.FromConfiguration(configuration, id).EnsureValid(ConfigurationLogger(sp));

        // A connection parsed from configuration carries no container services, so the drone would
        // log to NullLoggerFactory. Apply them the way the scope's connection registration does.
        if (options.ClientOptions is KurrentClientOptions configured)
            ResolveServices(configured, sp);

        // Then the scope's connection, then the shared configuration keys. The delegate runs last, so
        // a connection it sets still wins.
        if (options.ClientOptions is null) {
            if (sp.GetService<KurrentClientOptions>() is { } scopeOptions)
                options.Connection(scopeOptions);
            else if (configuration is not null && ResolveConnectionString(configuration) is { } shared)
                options.Connection(ResolveServices(KurrentClientOptions.Parse(shared), sp));
        }

        configure(options, sp);

        // Route metrics through the container's meter factory unless the caller set one. Resolved
        // optionally: a bare IServiceCollection need not have called AddMetrics.
        options.MeterFactory ??= sp.GetService<IMeterFactory>();

        return options;
    }

    /// <summary>
    /// Resolves the shared connection string from configuration: <c>ConnectionStrings:Kurrent:Surge</c>,
    /// then <c>ConnectionStrings:KurrentDB</c>.
    /// </summary>
    static string? ResolveConnectionString(IConfiguration configuration) {
        var surge = configuration["ConnectionStrings:Kurrent:Surge"];

        return !string.IsNullOrWhiteSpace(surge)
            ? surge
            : KurrentSurgeConfigurationReader.FindConnectionString(configuration, "KurrentDB");
    }

    /// <summary>
    /// Logger for configuration problems found while building options. Warnings are logged and
    /// errors throw, mirroring how options validation behaves on the same path.
    /// </summary>
    internal static ILogger ConfigurationLogger(IServiceProvider sp) =>
        sp.GetService<ILoggerFactory>()?.CreateLogger<KurrentSurgeBuilder>() ?? NullLogger<KurrentSurgeBuilder>.Instance;

    internal static bool NamesMatch(string left, string right) {
        return Strip(left).Equals(Strip(right), StringComparison.OrdinalIgnoreCase);

        static string Strip(string value) => value.Replace("-", "").Replace("_", "");
    }

    #endregion // Options resolution
}
