using System.Diagnostics.CodeAnalysis;
using Kurrent.Surge.Processors;
using Kurrent.Surge.Processors.Configuration;
using Kurrent.Surge.Processors.Hosting;
using Kurrent.Surge.Schema.Naming;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;

namespace Kurrent.Surge;

/// <summary>
/// Builds an application that hosts exactly one <typeparamref name="TProcessor"/>. Services,
/// configuration, and logging work as they do on any host application builder.
/// </summary>
/// <remarks>
/// <para>
/// The processor id is the one passed to <c>SurgeProcessor.CreateBuilder</c>, else the type name in
/// kebab-case. Options are read from the one section <c>Kurrent:Surge:Processors:{id}</c>, then the
/// configure delegate runs over them, so values it sets win. The connection resolves last. The
/// processor reports health under the id.
/// </para>
/// <para>
/// Unless the <c>applicationName</c> host setting is present, <see cref="IHostEnvironment.ApplicationName"/>
/// is the id in Title Case: <c>live-transfers</c> gives <c>Live Transfers</c>.
/// </para>
/// <para>
/// Changing the processor id or the application name in code afterwards is your responsibility. The
/// section already read, the health check name, and the application name stay as they are.
/// </para>
/// <para>
/// To tune the configured connection for the whole application, the processor and every service that
/// takes a <c>KurrentClient</c>, call <c>AddKurrentSurge(surge => surge.Connection(client => ...))</c>
/// on this builder. <c>ProcessorOptions.Connection(...)</c> reaches the processor only.
/// </para>
/// </remarks>
/// <typeparam name="TProcessor">The hosted processor type.</typeparam>
[PublicAPI]
public sealed class SurgeProcessorBuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProcessor>
    : IHostApplicationBuilder where TProcessor : class, IProcessor {
    readonly HostApplicationBuilder _inner;

    internal SurgeProcessorBuilder(string[]? args, string? processorId, Action<ProcessorOptions>? configure) {
        _inner = Host.CreateApplicationBuilder(args);

        var id = processorId ?? typeof(TProcessor).Name.ToKebabCase();

        if (_inner.Configuration[HostDefaults.ApplicationKey] is null)
            _inner.Environment.ApplicationName = id.ToTitleCase();

        Services.TryAddSingleton<TProcessor>();
        Services.TryAddSingleton<ProcessorHealthCheck<TProcessor>>();

        Services.AddKurrentSurge(surge => ((KurrentSurgeBuilder)surge).AddProcessorCore<TProcessor>(
            id,
            static sp => sp.GetRequiredService<TProcessor>(),
            (options, sp) => {
                configure?.Invoke(options);
                options.Observers.Add(sp.GetRequiredService<ProcessorHealthCheck<TProcessor>>());
            }));

        Services.AddSingleton<IDrone>(static sp => sp.GetRequiredService<DroneService<TProcessor>>().Drone);

        // Resolves the registered singleton, so the check and the observer are one instance.
        Services.AddHealthChecks().AddCheck<ProcessorHealthCheck<TProcessor>>(id, tags: ["kurrent", "surge", "processor"]);
    }

    /// <summary>
    /// The application's configuration.
    /// </summary>
    public ConfigurationManager Configuration => _inner.Configuration;

    IConfigurationManager IHostApplicationBuilder.Configuration => _inner.Configuration;

    /// <summary>
    /// The application's hosting environment.
    /// </summary>
    public IHostEnvironment Environment => _inner.Environment;

    /// <summary>
    /// The application's logging providers.
    /// </summary>
    public ILoggingBuilder Logging => _inner.Logging;

    /// <summary>
    /// The application's metrics configuration.
    /// </summary>
    public IMetricsBuilder Metrics => _inner.Metrics;

    /// <summary>
    /// The application's services.
    /// </summary>
    public IServiceCollection Services => _inner.Services;

    /// <summary>
    /// State shared between components while the application is built.
    /// </summary>
    public IDictionary<object, object> Properties => ((IHostApplicationBuilder)_inner).Properties;

    /// <summary>
    /// Replaces the factory that creates the application's service provider.
    /// </summary>
    /// <param name="factory">Creates the container builder and the service provider.</param>
    /// <param name="configure">Optional. Configures the container builder.</param>
    public void ConfigureContainer<TContainerBuilder>(
        IServiceProviderFactory<TContainerBuilder> factory,
        Action<TContainerBuilder>? configure = null
    ) where TContainerBuilder : notnull =>
        _inner.ConfigureContainer(factory, configure);

    /// <summary>
    /// Builds the application. Resolves the processor and validates its options.
    /// </summary>
    /// <returns>The host. Resolve <see cref="IDrone"/> from its services for the processor's status and options.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when another processor is registered beside <typeparamref name="TProcessor"/>, or when
    /// configuration or the options are invalid.
    /// </exception>
    public IHost Build() {
        var hosted = Services
            .Where(static descriptor => descriptor.ServiceType.IsGenericType
                                     && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(DroneService<>))
            .Select(static descriptor => descriptor.ServiceType.GenericTypeArguments[0])
            .ToList();

        if (hosted.Count > 1) {
            var extras = hosted.Where(static type => type != typeof(TProcessor)).Select(static type => type.Name);

            throw new InvalidOperationException(
                $"SurgeProcessor hosts exactly one processor ({typeof(TProcessor).Name}), but {string.Join(", ", extras)} "
              + "is also registered. Use AddKurrentSurge on a host application builder to run several processors.");
        }

        var host = _inner.Build();

        _ = host.Services.GetRequiredService<DroneService<TProcessor>>();

        return host;
    }
}
