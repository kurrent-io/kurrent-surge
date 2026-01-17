using Microsoft.Extensions.DependencyInjection;

namespace Kurrent.Client.Streams;

/// <summary>
/// Builder for registering multiple processors with shared connection settings.
/// </summary>
/// <remarks>
/// <para>
/// Use this builder when you need to register multiple processors that share
/// the same KurrentDB connection. Each processor can still have its own
/// filter and settings configuration.
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
[PublicAPI]
public sealed class KurrentProcessorsBuilder {
    readonly IServiceCollection _services;
    string?                     _connectionString;

    internal KurrentProcessorsBuilder(IServiceCollection services) => _services = services;

    /// <summary>
    /// Sets the shared connection string for all processors added via this builder.
    /// </summary>
    /// <param name="connectionString">
    /// KurrentDB connection string (e.g., "kurrentdb://localhost:2113").
    /// </param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Processors added after this call will use the specified connection string
    /// unless they override it with <see cref="KurrentProcessorHostBuilder.UseClient(string)"/>.
    /// </para>
    /// </remarks>
    public KurrentProcessorsBuilder ConnectTo(string connectionString) {
        _connectionString = connectionString;
        return this;
    }

    /// <summary>
    /// Adds a processor with the shared connection and optional custom configuration.
    /// </summary>
    /// <typeparam name="TProcessor">
    /// The processor type implementing <see cref="IRecordProcessor"/>.
    /// </typeparam>
    /// <param name="configure">
    /// Optional configuration for the processor's hosting options.
    /// The shared connection is applied first, then the custom configuration.
    /// </param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// If the configuration specifies <see cref="KurrentProcessorHostBuilder.UseClient(string)"/>
    /// or <see cref="KurrentProcessorHostBuilder.ConfigureClient"/>, it will override
    /// the shared connection.
    /// </para>
    /// </remarks>
    public KurrentProcessorsBuilder AddProcessor<TProcessor>(
        Action<KurrentProcessorHostBuilder>? configure = null)
        where TProcessor : class, IRecordProcessor {
        _services.AddKurrentProcessor<TProcessor>(builder => {
            // Apply shared connection first
            if (_connectionString is not null) {
                builder.UseClient(_connectionString);
            }

            // Allow custom configuration to override
            configure?.Invoke(builder);
        });

        return this;
    }

    /// <summary>
    /// Adds a processor using a factory delegate with the shared connection.
    /// </summary>
    /// <typeparam name="TProcessor">
    /// The processor type implementing <see cref="IRecordProcessor"/>.
    /// </typeparam>
    /// <param name="processorFactory">
    /// Factory function to create the processor instance.
    /// </param>
    /// <param name="configure">
    /// Optional configuration for the processor's hosting options.
    /// </param>
    /// <returns>The builder instance for method chaining.</returns>
    public KurrentProcessorsBuilder AddProcessor<TProcessor>(
        Func<IServiceProvider, TProcessor> processorFactory,
        Action<KurrentProcessorHostBuilder>? configure = null)
        where TProcessor : class, IRecordProcessor {
        _services.AddKurrentProcessor(processorFactory, builder => {
            // Apply shared connection first
            if (_connectionString is not null) {
                builder.UseClient(_connectionString);
            }

            // Allow custom configuration to override
            configure?.Invoke(builder);
        });

        return this;
    }
}
