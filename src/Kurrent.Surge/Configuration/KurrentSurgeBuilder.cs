using Kurrent.Toolkit;
using System.Diagnostics.CodeAnalysis;
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
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

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

    public IKurrentSurgeBuilder AddProcessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProcessor>(
        Action<ProcessorOptions>? configure = null
    ) where TProcessor : class, IRecordProcessor =>
        AddProcessorCore<TProcessor>(null, (opts, _) => configure?.Invoke(opts));

    public IKurrentSurgeBuilder AddProcessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProcessor>(
        string processorId,
        Action<ProcessorOptions>? configure = null
    ) where TProcessor : class, IRecordProcessor =>
        AddProcessorCore<TProcessor>(processorId, (opts, _) => configure?.Invoke(opts));

    public IKurrentSurgeBuilder AddProcessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProcessor>(
        Action<ProcessorOptions, IServiceProvider> configure
    ) where TProcessor : class, IRecordProcessor =>
        AddProcessorCore<TProcessor>(null, configure);

    public IKurrentSurgeBuilder AddProcessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProcessor>(
        string processorId,
        Action<ProcessorOptions, IServiceProvider> configure
    ) where TProcessor : class, IRecordProcessor =>
        AddProcessorCore<TProcessor>(processorId, configure);

    /// <summary>
    /// Registers a processor of either shape under one id: <paramref name="processorId"/>, else the
    /// type name in kebab-case. The processor is a keyed singleton the container constructs, with a
    /// hosted drone, an <see cref="IDrone"/> and a health check keyed by the same id. The drone picks
    /// the record or batch dispatcher from the processor's runtime type.
    /// </summary>
    IKurrentSurgeBuilder AddProcessorCore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProcessor>(
        string? processorId,
        Action<ProcessorOptions, IServiceProvider> configure
    ) where TProcessor : class, IProcessor {
        var id = string.IsNullOrWhiteSpace(processorId) ? typeof(TProcessor).Name.ToKebabCase() : processorId;

        if (Services.IsRegistered<IDrone>(id))
            throw new InvalidOperationException($"A processor with id '{id}' is already registered.");

        Services.TryAddKeyedSingleton<TProcessor>(id);
        Services.AddKeyedSingleton<ProcessorHealthCheck>(id);

        Services.AddKeyedSingleton<DroneService<TProcessor>>(id, (sp, _) => {
            var options = BuildOptions(sp, id, configure);

            options.Observers.Add(sp.GetRequiredKeyedService<ProcessorHealthCheck>(id));

            if (sp.GetService<ProcessorFailureRecorder>() is { } recorder)
                options.Observers.Add(recorder);

            var drone = new KurrentProcessor(options, sp.GetRequiredKeyedService<TProcessor>(id)).Build();

            return new DroneService<TProcessor>(drone, sp.GetRequiredService<ILogger<DroneService<TProcessor>>>());
        });

        Services.AddSingleton<IHostedService>(sp => sp.GetRequiredKeyedService<DroneService<TProcessor>>(id));
        Services.AddKeyedSingleton<IDrone>(id, (sp, _) => sp.GetRequiredKeyedService<DroneService<TProcessor>>(id).Drone);

        Services.AddHealthChecks().Add(new HealthCheckRegistration(
            id,
            sp => sp.GetRequiredKeyedService<ProcessorHealthCheck>(id),
            failureStatus: null,
            tags: ["kurrent", "surge", "processor"]));

        RegisterUnkeyed<TProcessor>(id);

        return this;
    }

    /// <summary>
    /// Keeps an unkeyed registration of the processor type, forwarding to the keyed one, while the
    /// type is registered under a single id. A second id removes it. An unkeyed registration the
    /// caller made is left alone.
    /// </summary>
    void RegisterUnkeyed<TProcessor>(string id) where TProcessor : class, IProcessor {
        var ids = Services.Count(static descriptor => descriptor.IsKeyedService && descriptor.ServiceType == typeof(DroneService<TProcessor>));

        if (ids == 1) {
            Services.TryAddSingleton<TProcessor>(new UnkeyedProcessor<TProcessor>(id).Resolve);
            return;
        }

        var forwarder = Services.FirstOrDefault(static descriptor =>
            !descriptor.IsKeyedService
         && descriptor.ServiceType == typeof(TProcessor)
         && descriptor.ImplementationFactory?.Target is UnkeyedProcessor<TProcessor>);

        if (forwarder is not null)
            Services.Remove(forwarder);
    }

    sealed class UnkeyedProcessor<TProcessor>(string processorId) where TProcessor : class {
        public TProcessor Resolve(IServiceProvider sp) => sp.GetRequiredKeyedService<TProcessor>(processorId);
    }

    #endregion // Processor

    #region ->> Batch Processor <<-

    public IKurrentSurgeBuilder AddBatchProcessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProcessor>(
        Action<ProcessorOptions>? configure = null
    ) where TProcessor : class, IRecordBatchProcessor =>
        AddProcessorCore<TProcessor>(null, (opts, _) => configure?.Invoke(opts));

    public IKurrentSurgeBuilder AddBatchProcessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProcessor>(
        string processorId,
        Action<ProcessorOptions>? configure = null
    ) where TProcessor : class, IRecordBatchProcessor =>
        AddProcessorCore<TProcessor>(processorId, (opts, _) => configure?.Invoke(opts));

    public IKurrentSurgeBuilder AddBatchProcessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProcessor>(
        Action<ProcessorOptions, IServiceProvider> configure
    ) where TProcessor : class, IRecordBatchProcessor =>
        AddProcessorCore<TProcessor>(null, configure);

    public IKurrentSurgeBuilder AddBatchProcessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProcessor>(
        string processorId,
        Action<ProcessorOptions, IServiceProvider> configure
    ) where TProcessor : class, IRecordBatchProcessor =>
        AddProcessorCore<TProcessor>(processorId, configure);

    #endregion // Batch Processor

    #region ->> Failure <<-

    internal const string InnerLifetimeKey = "Kurrent.Surge.FailProcessOnFatalError";

    public IKurrentSurgeBuilder FailProcessOnFatalError() {
        if (Services.IsRegistered<IHostLifetime>(InnerLifetimeKey))
            return this;

        var lifetime = Services.LastOrDefault(static descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(IHostLifetime))
                    ?? throw new InvalidOperationException(
                           "No IHostLifetime is registered. Call FailProcessOnFatalError on a host application builder, after the host lifetime is chosen.");

        // The captured lifetime is re-registered under a private key so the container still constructs
        // it, whatever shape its registration had, and the decorator resolves it from there.
        Services.Remove(lifetime);

        Services.Add(lifetime switch {
            { ImplementationInstance: { } instance } => new ServiceDescriptor(typeof(IHostLifetime), InnerLifetimeKey, instance),
            { ImplementationFactory: { } factory }   => new ServiceDescriptor(typeof(IHostLifetime), InnerLifetimeKey, (sp, _) => factory(sp), lifetime.Lifetime),
            _                                        => new ServiceDescriptor(typeof(IHostLifetime), InnerLifetimeKey, lifetime.ImplementationType!, lifetime.Lifetime)
        });

        Services.TryAddSingleton<ProcessorFailureRecorder>();

        Services.AddSingleton<IHostLifetime>(static sp => new ProcessorFailureHostLifetime(
            sp.GetRequiredKeyedService<IHostLifetime>(InnerLifetimeKey),
            sp.GetRequiredService<ProcessorFailureRecorder>()));

        return this;
    }

    #endregion // Failure

    #region ->> Options resolution <<-

    static ProcessorOptions BuildOptions(
        IServiceProvider sp,
        string id,
        Action<ProcessorOptions, IServiceProvider> configure
    ) {
        var configuration = sp.GetService<IConfiguration>();
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
