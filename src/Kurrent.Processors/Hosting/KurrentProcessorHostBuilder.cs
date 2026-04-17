using Kurrent.Client;
using Kurrent.Client.Schema.NameStrategies;
using Kurrent.Client.Streams;
using Kurrent.Processors.Models;
using Microsoft.Extensions.Configuration;

namespace Kurrent.Processors.Options;

/// <summary>
/// Builder for configuring a processor's hosting options.
/// Used with <see cref="KurrentProcessorServiceCollectionExtensions.AddKurrentProcessor{TProcessor}"/>.
/// </summary>
[PublicAPI]
public sealed class KurrentProcessorHostBuilder {
    readonly ProcessorOptions _options = new();

    string?                                           _connectionString;
    Func<KurrentClientOptions, KurrentClientOptions>? _clientConfigure;
    bool                                              _useOwnClient;
    string?                                           _processorTypeName;

    #region ->> Configuration Binding <<-

    internal KurrentProcessorHostBuilder BindFromConfiguration(IConfiguration? configuration, string processorTypeName) {
        _processorTypeName = processorTypeName;

        if (configuration is not null)
            _options.FromConfiguration(configuration, processorTypeName);

        return this;
    }

    public KurrentProcessorHostBuilder FromConfiguration(IConfiguration configuration) {
        if (_processorTypeName is null)
            throw new InvalidOperationException("Processor type not available for configuration binding.");

        _options.FromConfiguration(configuration, _processorTypeName);
        return this;
    }

    #endregion

    #region ->> Client Configuration <<-

    public KurrentProcessorHostBuilder UseClient(string connectionString) {
        _connectionString = connectionString;
        _useOwnClient     = true;
        return this;
    }

    public KurrentProcessorHostBuilder ConfigureClient(Func<KurrentClientOptions, KurrentClientOptions> configure) {
        _clientConfigure = configure;
        _useOwnClient    = true;
        return this;
    }

    public KurrentProcessorHostBuilder UseClient(string connectionString, Func<KurrentClientOptions, KurrentClientOptions> configure) {
        _connectionString = connectionString;
        _clientConfigure  = configure;
        _useOwnClient     = true;
        return this;
    }

    #endregion

    #region ->> Filter <<-

    public KurrentProcessorHostBuilder WithFilter(string sqlExpression) {
        _options.Subscription.Filter = new ExpressionFilter(sqlExpression);
        return this;
    }

    public KurrentProcessorHostBuilder WithFilter(IRecordFilter filter) {
        _options.Subscription.Filter = filter;
        return this;
    }

    #endregion

    #region ->> Convenience Methods <<-

    public KurrentProcessorHostBuilder Checkpointing(Action<CheckpointOptions.Builder> configure) {
        _options.Checkpointing(configure);
        return this;
    }

    public KurrentProcessorHostBuilder WithProcessorId(string processorId) {
        _options.ProcessorId = processorId;
        return this;
    }

    public KurrentProcessorHostBuilder WithDroneId(string droneId) {
        _options.DroneId = droneId;
        return this;
    }

    public KurrentProcessorHostBuilder WithMaxParallelism(int maxParallelism) {
        _options.MaxParallelism = maxParallelism;
        return this;
    }

    public KurrentProcessorHostBuilder WithDebugMode(bool enabled) {
        _options.Debug = enabled;
        return this;
    }

    public KurrentProcessorHostBuilder WithPartitionKeySelector(string selector) {
        _options.Subscription.PartitionKeySelector = selector;
        return this;
    }

    public KurrentProcessorHostBuilder WithInitialPosition(SubscriptionInitialPosition position) {
        _options.Subscription.InitialPosition = position;
        return this;
    }

    #endregion

    #region ->> Build <<-

    internal KurrentProcessorRegistration<TProcessor> Build<TProcessor>()
        where TProcessor : IRecordProcessor {
        // Auto-generate ProcessorId if not set; the setter derives DroneId for us.
        if (string.IsNullOrWhiteSpace(_options.ProcessorId))
            _options.ProcessorId = typeof(TProcessor).Name.ToSnakeCase();

        return new KurrentProcessorRegistration<TProcessor>(
            _options,
            _connectionString,
            _clientConfigure,
            _useOwnClient
        );
    }

    #endregion
}
