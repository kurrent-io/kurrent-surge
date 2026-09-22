using System.Diagnostics.CodeAnalysis;
using Kurrent.Surge.Processors.Configuration;
using Kurrent.Surge.Client;
using Kurrent.Surge.Processors;
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
/// <para>
/// A processor's id is the one passed at registration, else its type name in kebab-case. Its
/// options are read from <c>Kurrent:Surge:Processors:{id}</c>, then the configure delegate runs
/// over them. Setting <c>ProcessorId</c> in the delegate does not change which section was read.
/// </para>
/// <para>
/// Each processor registration adds the processor type as a keyed singleton keyed by the id, one
/// hosted service, an <see cref="IDrone"/> keyed by the id, and a health check named by the id. A
/// keyed registration of the type already present for that id is kept. While a type is registered
/// under a single id it also resolves unkeyed. A second registration under the same id throws.
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
    /// Registers a processor under its type name in kebab-case.
    /// </summary>
    /// <typeparam name="TProcessor">The processor type, constructed by the container.</typeparam>
    /// <param name="configure">Optional per-processor settings.</param>
    /// <returns>The builder for method chaining.</returns>
    IKurrentSurgeBuilder AddProcessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProcessor>(
        Action<ProcessorOptions>? configure = null
    ) where TProcessor : class, IRecordProcessor;

    /// <summary>
    /// Registers a processor under <paramref name="processorId"/>.
    /// </summary>
    /// <typeparam name="TProcessor">The processor type, constructed by the container.</typeparam>
    /// <param name="processorId">
    /// The processor id, and the key of the configuration section the processor reads. A blank id
    /// reads as none given.
    /// </param>
    /// <param name="configure">Optional per-processor settings.</param>
    /// <returns>The builder for method chaining.</returns>
    IKurrentSurgeBuilder AddProcessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProcessor>(
        string processorId,
        Action<ProcessorOptions>? configure = null
    ) where TProcessor : class, IRecordProcessor;

    /// <summary>
    /// Registers a processor under its type name in kebab-case, with settings that need the
    /// service provider.
    /// </summary>
    /// <typeparam name="TProcessor">The processor type, constructed by the container.</typeparam>
    /// <param name="configure">Per-processor settings.</param>
    /// <returns>The builder for method chaining.</returns>
    IKurrentSurgeBuilder AddProcessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProcessor>(
        Action<ProcessorOptions, IServiceProvider> configure
    ) where TProcessor : class, IRecordProcessor;

    /// <summary>
    /// Registers a processor under <paramref name="processorId"/>, with settings that need the
    /// service provider.
    /// </summary>
    /// <typeparam name="TProcessor">The processor type, constructed by the container.</typeparam>
    /// <param name="processorId">
    /// The processor id, and the key of the configuration section the processor reads. A blank id
    /// reads as none given.
    /// </param>
    /// <param name="configure">Per-processor settings.</param>
    /// <returns>The builder for method chaining.</returns>
    IKurrentSurgeBuilder AddProcessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProcessor>(
        string processorId,
        Action<ProcessorOptions, IServiceProvider> configure
    ) where TProcessor : class, IRecordProcessor;

    #endregion // Processor

    #region ->> Batch Processor <<-

    /// <summary>
    /// Registers a batch processor under its type name in kebab-case.
    /// </summary>
    /// <typeparam name="TProcessor">The batch processor type, constructed by the container.</typeparam>
    /// <param name="configure">Optional per-processor settings.</param>
    /// <returns>The builder for method chaining.</returns>
    IKurrentSurgeBuilder AddBatchProcessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProcessor>(
        Action<ProcessorOptions>? configure = null
    ) where TProcessor : class, IRecordBatchProcessor;

    /// <summary>
    /// Registers a batch processor under <paramref name="processorId"/>.
    /// </summary>
    /// <typeparam name="TProcessor">The batch processor type, constructed by the container.</typeparam>
    /// <param name="processorId">
    /// The processor id, and the key of the configuration section the processor reads. A blank id
    /// reads as none given.
    /// </param>
    /// <param name="configure">Optional per-processor settings.</param>
    /// <returns>The builder for method chaining.</returns>
    IKurrentSurgeBuilder AddBatchProcessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProcessor>(
        string processorId,
        Action<ProcessorOptions>? configure = null
    ) where TProcessor : class, IRecordBatchProcessor;

    /// <summary>
    /// Registers a batch processor under its type name in kebab-case, with settings that need the
    /// service provider.
    /// </summary>
    /// <typeparam name="TProcessor">The batch processor type, constructed by the container.</typeparam>
    /// <param name="configure">Per-processor settings.</param>
    /// <returns>The builder for method chaining.</returns>
    IKurrentSurgeBuilder AddBatchProcessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProcessor>(
        Action<ProcessorOptions, IServiceProvider> configure
    ) where TProcessor : class, IRecordBatchProcessor;

    /// <summary>
    /// Registers a batch processor under <paramref name="processorId"/>, with settings that need
    /// the service provider.
    /// </summary>
    /// <typeparam name="TProcessor">The batch processor type, constructed by the container.</typeparam>
    /// <param name="processorId">
    /// The processor id, and the key of the configuration section the processor reads. A blank id
    /// reads as none given.
    /// </param>
    /// <param name="configure">Per-processor settings.</param>
    /// <returns>The builder for method chaining.</returns>
    IKurrentSurgeBuilder AddBatchProcessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProcessor>(
        string processorId,
        Action<ProcessorOptions, IServiceProvider> configure
    ) where TProcessor : class, IRecordBatchProcessor;

    #endregion // Batch Processor

    #region ->> Failure <<-

    /// <summary>
    /// Makes the host's <c>RunAsync</c> throw the failure of a processor that stopped with an
    /// error, so the process exits with a failure code. Without this call a failed processor stops
    /// the host and the process exits normally.
    /// </summary>
    /// <remarks>
    /// Wraps the <see cref="Microsoft.Extensions.Hosting.IHostLifetime"/> registered at the time of
    /// the call, so call it after the lifetime is chosen, for example after <c>UseSystemd</c> or
    /// <c>UseWindowsService</c>.
    /// </remarks>
    /// <returns>The builder for method chaining.</returns>
    IKurrentSurgeBuilder FailProcessOnFatalError();

    #endregion // Failure
}
