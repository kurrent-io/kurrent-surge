using System.Diagnostics.Metrics;
using Kurrent.Processors.Configuration;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema.Naming;
using Kurrent.Surge.Streaming;
using Kurrent.Surge.Streaming.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

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
        AddProcessor(static sp => sp.GetRequiredService<TProcessor>(), (opts, _) => configure?.Invoke(opts));

    public IKurrentSurgeBuilder AddProcessor<TProcessor>(Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordProcessor =>
        AddProcessor(static sp => sp.GetRequiredService<TProcessor>(), configure);

    public IKurrentSurgeBuilder AddProcessor<TProcessor>(
        Func<IServiceProvider, TProcessor> processorFactory,
        Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordProcessor =>
        AddProcessor(processorFactory, (opts, _) => configure?.Invoke(opts));

    public IKurrentSurgeBuilder AddProcessor<TProcessor>(
        Func<IServiceProvider, TProcessor> processorFactory,
        Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordProcessor {
        Services.TryRegisterHostedService<DroneService<TProcessor>>(sp => {
            var options   = BuildOptions<TProcessor>(sp, configure);
            var processor = processorFactory(sp);
            var drone     = new KurrentProcessor(options, processor).Build();
            return new DroneService<TProcessor>(drone, sp.GetRequiredService<ILogger<DroneService<TProcessor>>>());
        });

        return this;
    }

    public IKurrentSurgeBuilder AddProcessor<TProcessor>(
        TProcessor instance,
        Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordProcessor =>
        AddProcessor(_ => instance, configure);

    public IKurrentSurgeBuilder AddProcessor<TProcessor>(
        TProcessor instance,
        Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordProcessor =>
        AddProcessor(_ => instance, configure);

    #endregion // Processor

    #region ->> Batch Processor <<-

    public IKurrentSurgeBuilder AddBatchProcessor<TProcessor>(Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordBatchProcessor =>
        AddBatchProcessor(static sp => sp.GetRequiredService<TProcessor>(), (opts, _) => configure?.Invoke(opts));

    public IKurrentSurgeBuilder AddBatchProcessor<TProcessor>(Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordBatchProcessor =>
        AddBatchProcessor(static sp => sp.GetRequiredService<TProcessor>(), configure);

    public IKurrentSurgeBuilder AddBatchProcessor<TProcessor>(
        Func<IServiceProvider, TProcessor> processorFactory,
        Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordBatchProcessor =>
        AddBatchProcessor(processorFactory, (opts, _) => configure?.Invoke(opts));

    public IKurrentSurgeBuilder AddBatchProcessor<TProcessor>(
        Func<IServiceProvider, TProcessor> processorFactory,
        Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordBatchProcessor {
        Services.TryRegisterHostedService<DroneService<TProcessor>>(sp => {
            var options   = BuildOptions<TProcessor>(sp, configure);
            var processor = processorFactory(sp);
            var drone     = new KurrentProcessor(options, processor).Build();
            return new DroneService<TProcessor>(drone, sp.GetRequiredService<ILogger<DroneService<TProcessor>>>());
        });

        return this;
    }

    public IKurrentSurgeBuilder AddBatchProcessor<TProcessor>(
        TProcessor instance,
        Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordBatchProcessor =>
        AddBatchProcessor(_ => instance, configure);

    public IKurrentSurgeBuilder AddBatchProcessor<TProcessor>(
        TProcessor instance,
        Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordBatchProcessor =>
        AddBatchProcessor(_ => instance, configure);

    #endregion // Batch Processor

    #region ->> Options resolution <<-

    static ProcessorOptions BuildOptions<TProcessor>(
        IServiceProvider sp,
        Action<ProcessorOptions, IServiceProvider> configure
    ) {
        var configuration = sp.GetService<IConfiguration>();
        var options       = new ProcessorOptions();
        var logger        = ConfigurationLogger(sp);

        // Read Kurrent:Surge:Processors:{TypeName} first, so the configure delegate overrides it.
        if (configuration is not null)
            options.FromConfiguration(configuration, typeof(TProcessor).Name).EnsureValid(logger);

        configure(options, sp);

        // ProcessorId has to be settled before configuration and connection strings are matched by id.
        if (string.IsNullOrWhiteSpace(options.ProcessorId)) {
            options.ProcessorId = typeof(TProcessor).Name.ToKebabCase();
        }
        else if (configuration is not null && !NamesMatch(options.ProcessorId, typeof(TProcessor).Name)) {
            // A section named by a custom processor id can only be matched after the configure
            // delegate settles that id. It is a deliberate per-deployment override, so it applies
            // over code — except the connection, where an explicit Connection() call stays first in
            // the documented precedence chain.
            var explicitConnection = options.ClientOptions;

            options.FromConfiguration(configuration, options.ProcessorId).EnsureValid(logger);

            if (explicitConnection is not null)
                options.ClientOptions = explicitConnection;
        }

        // Precedence, most specific first: a connection set on the options, then this processor's
        // own configuration key, then the scope's connection, then the shared configuration keys.
        // The processor's own key outranks the scope so one processor can be pointed at another
        // cluster without touching code.
        if (options.ClientOptions is null) {
            var ownConnectionString = configuration is not null
                ? FindConnectionString(configuration, options.ProcessorId)
                : null;

            if (ownConnectionString is not null)
                options.Connection(ownConnectionString);
            else if (sp.GetService<KurrentClientOptions>() is { } scopeOptions)
                options.Connection(scopeOptions);
            else if (configuration is not null && ResolveConnectionString(configuration) is { } shared)
                options.Connection(shared);
        }

        // Route metrics through the container's meter factory unless the caller set one. Resolved
        // optionally: a bare IServiceCollection need not have called AddMetrics.
        options.MeterFactory ??= sp.GetService<IMeterFactory>();

        return options;
    }

    /// <summary>
    /// Resolves the shared connection string from configuration, most specific key first.
    /// </summary>
    static string? ResolveConnectionString(IConfiguration configuration) =>
        FindConnectionString(configuration, "KurrentSurge")
     ?? FindConnectionString(configuration, "KurrentDB");

    /// <summary>
    /// Looks up a <c>ConnectionStrings</c> entry, accepting PascalCase, kebab-case and snake_case
    /// spellings of the same name.
    /// </summary>
    /// <remarks>
    /// A processor id is kebab-case by default but a caller may set anything, and hand-written
    /// configuration rarely matches a generated identifier exactly. Separators are stripped and the
    /// comparison is case-insensitive, so <c>FraudProcessor</c>, <c>fraud-processor</c> and
    /// <c>fraud_processor</c> all reach the same entry.
    /// </remarks>
    static string? FindConnectionString(IConfiguration configuration, string name) {
        foreach (var entry in configuration.GetSection("ConnectionStrings").GetChildren()) {
            if (!NamesMatch(entry.Key, name))
                continue;

            return string.IsNullOrWhiteSpace(entry.Value) ? null : entry.Value;
        }

        return null;
    }

    /// <summary>
    /// Logger for configuration problems found while building options. Warnings are logged and
    /// errors throw, mirroring how options validation behaves on the same path.
    /// </summary>
    static ILogger ConfigurationLogger(IServiceProvider sp) =>
        sp.GetService<ILoggerFactory>()?.CreateLogger<KurrentSurgeBuilder>() ?? NullLogger<KurrentSurgeBuilder>.Instance;

    static bool NamesMatch(string left, string right) {
        return Strip(left).Equals(Strip(right), StringComparison.OrdinalIgnoreCase);

        static string Strip(string value) => value.Replace("-", "").Replace("_", "");
    }

    #endregion // Options resolution
}
