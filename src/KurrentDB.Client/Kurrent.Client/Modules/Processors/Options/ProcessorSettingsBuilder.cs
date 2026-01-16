using Kurrent.Client.Schema.NameStrategies;
using Microsoft.Extensions.Configuration;

namespace Kurrent.Client.Streams;

/// <summary>
/// Internal builder for processor settings manipulation.
/// Used by both <see cref="KurrentProcessorBuilder"/> and <see cref="KurrentProcessorHostBuilder"/>
/// to eliminate code duplication while maintaining their distinct public APIs.
/// </summary>
internal sealed class ProcessorSettingsBuilder {
    ProcessorSettings _settings;

    internal ProcessorSettingsBuilder(ProcessorSettings? initial = null)
        => _settings = initial ?? new ProcessorSettings();

    internal ProcessorSettings Settings => _settings;

    // ═══════════════════════════════════════════════════════════════════════════════
    // Configuration Binding
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Loads settings from a configuration section.
    /// Section path: <c>Kurrent:Processors:{sectionName}</c>
    /// </summary>
    /// <param name="configuration">The configuration root. If null, no-op.</param>
    /// <param name="sectionName">
    /// The section name under <c>Kurrent:Processors</c>.
    /// If <c>ProcessorId</c> is not set in config, it will be auto-generated from this name.
    /// </param>
    /// <returns>The builder instance for method chaining.</returns>
    internal ProcessorSettingsBuilder FromConfiguration(IConfiguration? configuration, string sectionName) {
        if (configuration is null) return this;

        var sectionPath = $"Kurrent:Processors:{sectionName}";
        var section = configuration.GetSection(sectionPath);
        if (section.Exists()) {
            section.Bind(_settings, options => options.BindNonPublicProperties = true);

            // Auto-generate ProcessorId from section name if not explicitly set in config
            if (string.IsNullOrEmpty(_settings.ProcessorId)) {
                _settings = _settings with { ProcessorId = sectionName.ToSnakeCase() };
            }
        }
        return this;
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Direct Settings
    // ═══════════════════════════════════════════════════════════════════════════════

    internal ProcessorSettingsBuilder WithProcessorId(string processorId) {
        _settings = _settings with { ProcessorId = processorId };
        return this;
    }

    internal ProcessorSettingsBuilder WithWorkerId(string workerId) {
        _settings = _settings with { WorkerId = workerId };
        return this;
    }

    internal ProcessorSettingsBuilder WithMaxParallelism(int maxParallelism) {
        _settings = _settings with { MaxParallelism = maxParallelism };
        return this;
    }

    internal ProcessorSettingsBuilder WithBufferCapacity(int capacity) {
        _settings = _settings with { BufferCapacity = capacity };
        return this;
    }

    internal ProcessorSettingsBuilder WithSkipDecoding(bool skip) {
        _settings = _settings with { SkipDecoding = skip };
        return this;
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Filter Configuration
    // ═══════════════════════════════════════════════════════════════════════════════

    internal ProcessorSettingsBuilder WithFilter(IRecordFilter filter) {
        _settings = _settings with {
            Subscription = _settings.Subscription with { Filter = filter }
        };
        return this;
    }

    internal ProcessorSettingsBuilder WithFilterExpression(string filterExpression) {
        var filter = new ExpressionFilter(filterExpression);
        _settings = _settings with {
            Subscription = _settings.Subscription with {
                FilterExpression = filterExpression,
                Filter           = filter
            }
        };
        return this;
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Subscription Settings
    // ═══════════════════════════════════════════════════════════════════════════════

    internal ProcessorSettingsBuilder WithPartitionKeySelector(string selector) {
        _settings = _settings with {
            Subscription = _settings.Subscription with { PartitionKeySelector = selector }
        };
        return this;
    }

    internal ProcessorSettingsBuilder WithInitialPosition(SubscriptionInitialPosition position) {
        _settings = _settings with {
            Subscription = _settings.Subscription with { InitialPosition = position }
        };
        return this;
    }

    internal ProcessorSettingsBuilder WithStartPosition(long position) {
        _settings = _settings with {
            Subscription = _settings.Subscription with { StartPosition = position }
        };
        return this;
    }

    internal ProcessorSettingsBuilder ConfigureSubscription(Func<SubscriptionSettings, SubscriptionSettings> configure) {
        _settings = _settings with { Subscription = configure(_settings.Subscription) };
        return this;
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Batching Settings
    // ═══════════════════════════════════════════════════════════════════════════════

    internal ProcessorSettingsBuilder WithBatching(BatchingSettings settings) {
        _settings = _settings with { Batching = settings };
        return this;
    }

    internal ProcessorSettingsBuilder ConfigureBatching(Func<BatchingSettings, BatchingSettings> configure) {
        _settings = _settings with { Batching = configure(_settings.Batching) };
        return this;
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Checkpoint Settings
    // ═══════════════════════════════════════════════════════════════════════════════

    internal ProcessorSettingsBuilder WithCheckpoint(CheckpointSettings settings) {
        _settings = _settings with { Checkpoint = settings };
        return this;
    }

    internal ProcessorSettingsBuilder ConfigureCheckpointing(Func<CheckpointSettings, CheckpointSettings> configure) {
        _settings = _settings with { Checkpoint = configure(_settings.Checkpoint) };
        return this;
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Full Configuration
    // ═══════════════════════════════════════════════════════════════════════════════

    internal ProcessorSettingsBuilder Configure(Func<ProcessorSettings, ProcessorSettings> configure) {
        _settings = configure(_settings);
        return this;
    }
}
