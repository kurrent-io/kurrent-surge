using Kurrent.Processors.Configuration;
using Kurrent.Surge.Client;
using Kurrent.Surge.Streaming;
using Microsoft.Extensions.DependencyInjection;

namespace Kurrent.Surge;

/// <summary>
/// Registration scope opened by <c>AddKurrentSurge</c>. Carries the connection and every
/// component that runs against it.
/// </summary>
/// <remarks>
/// <para>
/// Members are declared on the interface rather than as extension methods so a call site needs
/// only <c>using Kurrent.Surge;</c>, whatever namespace the options types live in.
/// </para>
/// <para>
/// <c>Connection</c> is authoritative: it replaces any <see cref="KurrentClientOptions"/> or
/// <see cref="KurrentClient"/> already in the service collection, and the last call wins. A
/// connection set directly on a component's options always beats the one registered here.
/// </para>
/// </remarks>
[PublicAPI]
public interface IKurrentSurgeBuilder {
    /// <summary>
    /// The underlying service collection, for registrations this scope does not cover.
    /// </summary>
    IServiceCollection Services { get; }

    #region ->> Connection <<-

    /// <summary>
    /// Sets the scope's connection from a connection string.
    /// </summary>
    /// <param name="connectionString">
    /// KurrentDB connection string, for example <c>kurrentdb://localhost:2113</c>.
    /// </param>
    /// <returns>The builder for method chaining.</returns>
    IKurrentSurgeBuilder Connection(string connectionString);

    /// <summary>
    /// Sets the scope's connection from a connection string, then tunes the resolved options.
    /// </summary>
    /// <param name="connectionString">KurrentDB connection string.</param>
    /// <param name="configure">Runs after the string is applied, so it can override anything.</param>
    /// <returns>The builder for method chaining.</returns>
    IKurrentSurgeBuilder Connection(string connectionString, Action<KurrentClientOptions> configure);

    /// <summary>
    /// Sets the scope's connection from pre-constructed options. Validated immediately, so
    /// misconfiguration surfaces at registration rather than on first use.
    /// </summary>
    /// <param name="options">The options to use.</param>
    /// <returns>The builder for method chaining.</returns>
    IKurrentSurgeBuilder Connection(KurrentClientOptions options);

    /// <summary>
    /// Sets the scope's connection by configuring the options directly.
    /// </summary>
    /// <param name="configure">
    /// Runs after service-provider overrides are applied, so it can override anything.
    /// </param>
    /// <returns>The builder for method chaining.</returns>
    IKurrentSurgeBuilder Connection(Action<KurrentClientOptions> configure);

    #endregion // Connection

    #region ->> Processor <<-

    /// <summary>
    /// Registers a processor as a hosted service, resolved from the container at drone startup.
    /// </summary>
    /// <typeparam name="TProcessor">
    /// The processor type. Must be registered separately, for example with
    /// <c>services.AddSingleton&lt;TProcessor&gt;()</c>.
    /// </typeparam>
    /// <param name="configure">Optional per-processor settings.</param>
    /// <returns>The builder for method chaining.</returns>
    IKurrentSurgeBuilder AddProcessor<TProcessor>(Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordProcessor;

    /// <summary>
    /// Registers a processor as a hosted service, with settings that need the service provider.
    /// </summary>
    IKurrentSurgeBuilder AddProcessor<TProcessor>(Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordProcessor;

    /// <summary>
    /// Registers a processor as a hosted service, built by a factory invoked once per drone at
    /// host startup. The processor type is not added to the container.
    /// </summary>
    IKurrentSurgeBuilder AddProcessor<TProcessor>(
        Func<IServiceProvider, TProcessor> processorFactory,
        Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordProcessor;

    /// <summary>
    /// Registers a factory-built processor, with settings that need the service provider.
    /// </summary>
    IKurrentSurgeBuilder AddProcessor<TProcessor>(
        Func<IServiceProvider, TProcessor> processorFactory,
        Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordProcessor;

    /// <summary>
    /// Registers a pre-built processor instance as a hosted service. The instance is reused for
    /// the drone's lifetime and is not added to the container.
    /// </summary>
    IKurrentSurgeBuilder AddProcessor<TProcessor>(
        TProcessor instance,
        Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordProcessor;

    /// <summary>
    /// Registers a pre-built processor instance, with settings that need the service provider.
    /// </summary>
    IKurrentSurgeBuilder AddProcessor<TProcessor>(
        TProcessor instance,
        Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordProcessor;

    #endregion // Processor

    #region ->> Batch Processor <<-

    /// <summary>
    /// Registers a batch processor as a hosted service, resolved from the container at drone startup.
    /// </summary>
    /// <typeparam name="TProcessor">
    /// The batch processor type. Must be registered separately.
    /// </typeparam>
    /// <param name="configure">Optional per-processor settings.</param>
    /// <returns>The builder for method chaining.</returns>
    IKurrentSurgeBuilder AddBatchProcessor<TProcessor>(Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordBatchProcessor;

    /// <summary>
    /// Registers a batch processor as a hosted service, with settings that need the service provider.
    /// </summary>
    IKurrentSurgeBuilder AddBatchProcessor<TProcessor>(Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordBatchProcessor;

    /// <summary>
    /// Registers a batch processor built by a factory invoked once per drone at host startup.
    /// </summary>
    IKurrentSurgeBuilder AddBatchProcessor<TProcessor>(
        Func<IServiceProvider, TProcessor> processorFactory,
        Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordBatchProcessor;

    /// <summary>
    /// Registers a factory-built batch processor, with settings that need the service provider.
    /// </summary>
    IKurrentSurgeBuilder AddBatchProcessor<TProcessor>(
        Func<IServiceProvider, TProcessor> processorFactory,
        Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordBatchProcessor;

    /// <summary>
    /// Registers a pre-built batch processor instance as a hosted service.
    /// </summary>
    IKurrentSurgeBuilder AddBatchProcessor<TProcessor>(
        TProcessor instance,
        Action<ProcessorOptions>? configure = null)
        where TProcessor : class, IRecordBatchProcessor;

    /// <summary>
    /// Registers a pre-built batch processor instance, with settings that need the service provider.
    /// </summary>
    IKurrentSurgeBuilder AddBatchProcessor<TProcessor>(
        TProcessor instance,
        Action<ProcessorOptions, IServiceProvider> configure)
        where TProcessor : class, IRecordBatchProcessor;

    #endregion // Batch Processor
}
