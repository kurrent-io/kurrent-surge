using Kurrent.Client.Schema.NameStrategies;
using Kurrent.Client.Streams;
using Kurrent.Processors.Models;
using Kurrent.Validation;
using Microsoft.Extensions.Configuration;

namespace Kurrent.Processors.Options;

/// <summary>
/// Configuration settings for a processor.
/// </summary>
[PublicAPI]
public record ProcessorOptions : SelfValidating<ProcessorOptions, ProcessorOptions.Validator> {
    /// <summary>
    /// Unique identifier for the processor group.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> Empty string</para>
    /// <para><b>Importance:</b> High</para>
    /// <para>
    /// Multiple workers with the same processor ID share checkpoints,
    /// enabling coordinated processing across instances.
    /// </para>
    /// </remarks>
    public string ProcessorId { get; init; } = string.Empty;

    /// <summary>
    /// Unique identifier for this worker instance within the processor group.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> Auto-generated short ID</para>
    /// <para><b>Importance:</b> High</para>
    /// <para>
    /// Used for checkpoint attribution. For sticky partition assignment,
    /// use a consistent identifier like <c>Environment.MachineName</c>.
    /// </para>
    /// </remarks>
    public string WorkerId { get; init; } = PunyId.New();

    /// <summary>
    /// Maximum number of partitions (key groups). Range: 1-32768.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> 128</para>
    /// <para><b>Importance:</b> High</para>
    /// <para>
    /// Determines the upper bound for parallel processing.
    /// </para>
    /// </remarks>
    internal int MaxParallelism { get; init; } = 1;

    /// <summary>
    /// Indicates whether debug mode is enabled for the processor.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> false</para>
    /// <para><b>Importance:</b> High</para>
    /// <para>
    /// When set to true, all exceptions are re-thrown, and  additional
    /// diagnostic and logging information may be generated, which can
    /// help in troubleshooting and debugging processor behavior.
    /// </para>
    /// <para>
    /// It is recommended to disable this setting in production environments
    /// to minimize performance overhead and log verbosity.
    /// </para>
    /// </remarks>
    public bool Debug { get; init; }

    /// <summary>
    /// Subscription settings (filter, partitioning, initial position).
    /// </summary>
    public SubscriptionOptions Subscription { get; init; } = new();

    /// <summary>
    /// Batching settings (size, timeout).
    /// </summary>
    public BatchingOptions Batching { get; init; } = new();

    /// <summary>
    /// Checkpoint settings (interval, retention, failure policy).
    /// </summary>
    public CheckpointOptions Checkpoint { get; init; } = new();

    /// <summary>
    /// Worker runtime settings (buffering, error handling, decoding).
    /// </summary>
    public WorkerOptions Worker { get; init; } = new();
    
    /// <summary>
    /// Internal builder for processor settings manipulation.
    /// Used by both <see cref="KurrentProcessorBuilder"/> and <see cref="KurrentProcessorHostBuilder"/>
    /// to eliminate code duplication while maintaining their distinct public APIs.
    /// </summary>
    internal sealed class Builder {
        ProcessorOptions _settings;

        internal Builder(ProcessorOptions? initial = null)
            => _settings = initial ?? new ProcessorOptions();

        internal ProcessorOptions Settings => _settings;

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
        internal Builder FromConfiguration(IConfiguration? configuration, string sectionName) {
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

        internal Builder WithProcessorId(string processorId) {
            _settings = _settings with { ProcessorId = processorId };
            return this;
        }

        internal Builder WithWorkerId(string workerId) {
            _settings = _settings with { WorkerId = workerId };
            return this;
        }

        internal Builder WithMaxParallelism(int maxParallelism) {
            _settings = _settings with { MaxParallelism = maxParallelism };
            return this;
        }

        internal Builder WithDebugMode(bool enabled) {
            _settings = _settings with { Debug = enabled };
            return this;
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // Filter Configuration
        // ═══════════════════════════════════════════════════════════════════════════════

        internal Builder WithFilter(IRecordFilter filter) {
            _settings = _settings with {
                Subscription = _settings.Subscription with { Filter = filter }
            };
            return this;
        }

        internal Builder WithFilterExpression(string filterExpression) {
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

        internal Builder WithPartitionKeySelector(string selector) {
            _settings = _settings with {
                Subscription = _settings.Subscription with { PartitionKeySelector = selector }
            };
            return this;
        }

        internal Builder WithInitialPosition(SubscriptionInitialPosition position) {
            _settings = _settings with {
                Subscription = _settings.Subscription with { InitialPosition = position }
            };
            return this;
        }

        internal Builder ConfigureSubscription(Func<SubscriptionOptions, SubscriptionOptions> configure) {
            _settings = _settings with { Subscription = configure(_settings.Subscription) };
            return this;
        }

        internal Builder ConfigureSubscription(Action<SubscriptionOptions.Builder> configure) {
            var builder = new SubscriptionOptions.Builder(_settings.Subscription);
            configure(builder);
            _settings = _settings with { Subscription = builder.Build() };
            return this;
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // Batching Settings
        // ═══════════════════════════════════════════════════════════════════════════════

        internal Builder ConfigureBatching(Func<BatchingOptions, BatchingOptions> configure) {
            _settings = _settings with { Batching = configure(_settings.Batching) };
            return this;
        }

        internal Builder ConfigureBatching(Action<BatchingOptions.Builder> configure) {
            var builder = new BatchingOptions.Builder(_settings.Batching);
            configure(builder);
            _settings = _settings with { Batching = builder.Build() };
            return this;
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // Checkpoint Settings
        // ═══════════════════════════════════════════════════════════════════════════════

        internal Builder ConfigureCheckpointing(Func<CheckpointOptions, CheckpointOptions> configure) {
            _settings = _settings with { Checkpoint = configure(_settings.Checkpoint) };
            return this;
        }

        internal Builder ConfigureCheckpointing(Action<CheckpointOptions.Builder> configure) {
            var builder = new CheckpointOptions.Builder(_settings.Checkpoint);
            configure(builder);
            _settings = _settings with { Checkpoint = builder.Build() };
            return this;
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // Worker Settings
        // ═══════════════════════════════════════════════════════════════════════════════

        internal Builder ConfigureWorker(Func<WorkerOptions, WorkerOptions> configure) {
            _settings = _settings with { Worker = configure(_settings.Worker) };
            return this;
        }

        internal Builder ConfigureWorker(Action<WorkerOptions.Builder> configure) {
            var builder = new WorkerOptions.Builder(_settings.Worker);
            configure(builder);
            _settings = _settings with { Worker = builder.Build() };
            return this;
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // Full Configuration
        // ═══════════════════════════════════════════════════════════════════════════════

        internal Builder Configure(Func<ProcessorOptions, ProcessorOptions> configure) {
            _settings = configure(_settings);
            return this;
        }
    }

    public class Validator : Validator<Validator, ProcessorOptions> {
        public Validator() {
            // ProcessorId validation - required when checkpointing
            RuleFor(options => options.ProcessorId)
                .NotEmpty()
                .WithMessage("ProcessorId is required for processor identification and checkpointing.");

            // MaxParallelism validation (1-32768)
            RuleFor(options => options.MaxParallelism)
                .InclusiveBetween(1, 32768)
                .WithMessage("MaxParallelism must be between 1 and 32768.");

            // PartitionKeySelector recommended when MaxParallelism > 1
            // This is a warning to maintain backward compatibility - parallelism works but may not be optimal
            When(options => options.MaxParallelism > 1, () => {
                RuleFor(options => options.Subscription.PartitionKeySelector)
                    .NotEmpty()
                    .WithMessage("PartitionKeySelector is recommended when MaxParallelism is greater than 1 for optimal partitioning.")
                    .WithSeverity(RuleSeverity.Warning);
            });

            // Nested validators
            RuleFor(options => options.Batching)
                .SetValidator(BatchingOptions.Validator.Instance);

            RuleFor(options => options.Checkpoint)
                .SetValidator(CheckpointOptions.Validator.Instance);

            RuleFor(options => options.Subscription)
                .SetValidator(SubscriptionOptions.Validator.Instance);

            RuleFor(options => options.Worker)
                .SetValidator(WorkerOptions.Validator.Instance);
        }
    }
}

/// <summary>
/// Configuration for subscription behavior.
/// </summary>
[PublicAPI]
public record SubscriptionOptions {
    /// <summary>
    /// Filter expression string for server-side filtering (e.g., <c>"$all"</c>, <c>"stream_id LIKE 'orders-%'"</c>).
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> Empty string (no filter)</para>
    /// <para><b>Importance:</b> High</para>
    /// <para>
    /// Used for proto/config binding. When binding from configuration,
    /// a <see cref="RecordFilter"/> can be created automatically from this expression.
    /// </para>
    /// </remarks>
    public string FilterExpression { get; init; } = string.Empty;

    /// <summary>
    /// Record filtering criteria to apply.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> RecordFilter.None</para>
    /// <para><b>Importance:</b> High</para>
    /// <para>
    /// Filters can target specific streams, schemas, metadata properties,
    /// or custom predicates to reduce data transfer and processing overhead.
    /// </para>
    /// </remarks>
    public IRecordFilter Filter { get; init; } = RecordFilter.None;

    /// <summary>
    /// Path to extract partition key. Required when <see cref="ProcessorOptions.MaxParallelism"/> > 1.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> Empty string</para>
    /// <para><b>Importance:</b> High (when using parallelism)</para>
    /// <para>
    /// Supports paths like:
    /// <list type="bullet">
    ///   <item><c>"stream_id"</c> - The stream ID</item>
    ///   <item><c>"data.customer_id"</c> - Field in record data</item>
    ///   <item><c>"metadata.tenant"</c> - Field in record metadata</item>
    /// </list>
    /// </para>
    /// </remarks>
    public string PartitionKeySelector { get; init; } = string.Empty;

    /// <summary>
    /// Where to start if no checkpoint exists.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> <see cref="SubscriptionInitialPosition.Latest"/></para>
    /// <para><b>Importance:</b> High</para>
    /// </remarks>
    public SubscriptionInitialPosition InitialPosition { get; init; } = SubscriptionInitialPosition.Latest;

    /// <summary>
    /// When set, overrides the stored checkpoint and forces subscription to start from this position.
    /// </summary>
    internal RecordPosition? StartPosition { get; init; }

    [PublicAPI]
    public sealed class Builder(SubscriptionOptions settings) : FluentBuilder<Builder, SubscriptionOptions>(settings) {
        public Builder() : this(new SubscriptionOptions()) { }

        public Builder FilterExpression(string expression) {
            var filter = new ExpressionFilter(expression);
            return With(s => s with { FilterExpression = expression, Filter = filter });
        }

        public Builder Filter(IRecordFilter filter) =>
            With(s => s with { Filter = filter });

        public Builder PartitionKeySelector(string selector) =>
            With(s => s with { PartitionKeySelector = selector });

        public Builder InitialPosition(SubscriptionInitialPosition position) =>
            With(s => s with { InitialPosition = position });

        public Builder StartFrom(RecordPosition startPosition) =>
            With(s => s with { StartPosition = startPosition });
    }

    public class Validator : Validator<Validator, SubscriptionOptions> {
        public Validator() {
            // InitialPosition validation - must be a valid enum value
            RuleFor(settings => settings.InitialPosition)
                .IsInEnum()
                .WithMessage("InitialPosition must be a valid SubscriptionInitialPosition value.");
        }
    }
}

/// <summary>
/// Configuration for record batching during processing.
/// </summary>
/// <remarks>
/// <para>
/// Batching controls how records are grouped before delivery to the processor.
/// Smaller batches reduce latency but increase overhead; larger batches improve
/// throughput at the cost of increased memory usage and processing delay.
/// </para>
/// </remarks>
[PublicAPI]
public record BatchingOptions {
    /// <summary>
    /// Maximum number of records per batch.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> 200 records</para>
    /// <para><b>Importance:</b> Medium</para>
    /// <para>
    /// Middle ground between low-latency (100) and high-throughput (500).
    /// </para>
    /// </remarks>
    public int MaxSize { get; init; } = 200;

    /// <summary>
    /// Maximum time to wait for a full batch (milliseconds).
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> 250 milliseconds</para>
    /// <para><b>Importance:</b> Medium</para>
    /// <para>
    /// Shorter timeouts reduce latency but may result in smaller batches.
    /// Longer timeouts allow larger batches but increase delivery delay.
    /// </para>
    /// </remarks>
    public long Timeout { get; init; } = 250;

    internal TimeSpan TimeoutTimeSpan => TimeSpan.FromMilliseconds(Timeout); // 00:00:00.250

    [PublicAPI]
    public sealed class Builder(BatchingOptions settings) : FluentBuilder<Builder, BatchingOptions>(settings) {
        public Builder() : this(new BatchingOptions()) { }

        public Builder MaxSize(int maxSize) =>
            With(s => s with { MaxSize = maxSize });

        public Builder Timeout(long timeout) =>
            With(s => s with { Timeout = timeout });

        public Builder Timeout(TimeSpan timeout) =>
            With(s => s with { Timeout = (long)timeout.TotalMilliseconds });
    }

    public class Validator : Validator<Validator, BatchingOptions> {
        public Validator() {
            // MaxSize validation (1-10000)
            RuleFor(settings => settings.MaxSize)
                .InclusiveBetween(1, 10000)
                .WithMessage("Batching MaxSize must be between 1 and 10000.");

            // Timeout validation (1-300000ms, up to 5 minutes)
            RuleFor(settings => settings.Timeout)
                .InclusiveBetween(1L, 300000L)
                .WithMessage("Batching Timeout must be between 1 and 300000 milliseconds.");
        }
    }
}

/// <summary>
/// Configuration for checkpoint behavior.
/// </summary>
[PublicAPI]
public record CheckpointOptions {
    public static readonly CheckpointOptions Default = new() {
        Interval = 30000,
        Timeout  = 30000
    };

    /// <summary>
    /// Time between automatic checkpoints (milliseconds).
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> 30000 milliseconds (30 seconds)</para>
    /// <para><b>Importance:</b> High</para>
    /// <para>
    /// Balances fault tolerance with I/O overhead.
    /// </para>
    /// </remarks>
    public long Interval { get; init; } = 30000;

    internal TimeSpan IntervalTimeSpan => TimeSpan.FromMilliseconds(Interval);

    /// <summary>
    /// Maximum time to wait for workers to acknowledge checkpoint (milliseconds).
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> 30000 milliseconds (30 seconds)</para>
    /// <para><b>Importance:</b> Medium</para>
    /// <para>
    /// Faster detection of stuck workers. If a worker doesn't acknowledge within this time,
    /// it's considered failed and partitions can be reassigned.
    /// </para>
    /// </remarks>
    public long Timeout { get; init; } = 30000;

    internal TimeSpan TimeoutTimeSpan => TimeSpan.FromMilliseconds(Timeout);

    [PublicAPI]
    public sealed class Builder(CheckpointOptions settings) : FluentBuilder<Builder, CheckpointOptions>(settings) {
        public Builder() : this(new CheckpointOptions()) { }

        public Builder Interval(long interval) =>
            With(s => s with { Interval = interval });

        public Builder Interval(TimeSpan interval) =>
            With(s => s with { Interval = (long)interval.TotalMilliseconds });

        public Builder Timeout(long timeout) =>
            With(s => s with { Timeout = timeout });

        public Builder Timeout(TimeSpan timeout) =>
            With(s => s with { Timeout = (long)timeout.TotalMilliseconds });

        public Builder RetentionCount(int count) =>
            With(s => s with { RetentionCount = count });

        public Builder FailurePolicy(CheckpointFailurePolicy policy) =>
            With(s => s with { FailurePolicy = policy });
    }

    /// <summary>
    /// Number of completed checkpoints to retain.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> 3 checkpoints</para>
    /// <para><b>Importance:</b> Low</para>
    /// </remarks>
    public int RetentionCount { get; init; } = 3;

    /// <summary>
    /// What to do when a checkpoint fails.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> <see cref="CheckpointFailurePolicy.Continue"/></para>
    /// <para><b>Importance:</b> High</para>
    /// </remarks>
    public CheckpointFailurePolicy FailurePolicy { get; init; } = CheckpointFailurePolicy.Continue;

    public class Validator : Validator<Validator, CheckpointOptions> {
        public Validator() {
            // Interval validation (1-3600000ms, up to 1 hour)
            RuleFor(settings => settings.Interval)
                .InclusiveBetween(1L, 3600000L)
                .WithMessage("Checkpoint Interval must be between 1 and 3600000 milliseconds.");

            // Timeout validation (1-300000ms, up to 5 minutes)
            RuleFor(settings => settings.Timeout)
                .InclusiveBetween(1L, 300000L)
                .WithMessage("Checkpoint Timeout must be between 1 and 300000 milliseconds.");

            // RetentionCount validation (1-100)
            RuleFor(settings => settings.RetentionCount)
                .InclusiveBetween(1, 100)
                .WithMessage("Checkpoint RetentionCount must be between 1 and 100.");

            // FailurePolicy validation - must be a valid enum value
            RuleFor(settings => settings.FailurePolicy)
                .IsInEnum()
                .WithMessage("FailurePolicy must be a valid CheckpointFailurePolicy value.");
        }
    }
}

/// <summary>
/// Defines what happens when a checkpoint fails.
/// </summary>
/// <remarks>
/// <para>
/// A checkpoint fails if any worker doesn't acknowledge within the timeout.
/// The timed-out worker is always disconnected regardless of this policy.
/// This setting controls what happens to the OTHER workers.
/// </para>
/// </remarks>
[PublicAPI]
public enum CheckpointFailurePolicy {
    /// <summary>
    /// Unspecified failure policy. The server will use the default behavior.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// Abort this checkpoint, continue processing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <list type="bullet">
    ///   <item>The checkpoint is marked as failed</item>
    ///   <item>Other workers continue processing from last successful checkpoint</item>
    ///   <item>Failed worker's partitions are reassigned to remaining workers</item>
    ///   <item>Next checkpoint triggers normally on interval/threshold</item>
    /// </list>
    /// </para>
    /// <para>Use when: High availability is priority, some data loss acceptable.</para>
    /// </remarks>
    Continue = 1,

    /// <summary>
    /// Stop all processing on checkpoint failure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <list type="bullet">
    ///   <item>All workers are disconnected</item>
    ///   <item>Processor goes to <c>Idle</c> state</item>
    ///   <item>Requires manual intervention (reconnect workers to resume)</item>
    /// </list>
    /// </para>
    /// <para>Use when: Data consistency is critical, prefer downtime over data loss.</para>
    /// </remarks>
    FailProcessor = 2
}

/// <summary>
/// Configuration for worker runtime behavior.
/// </summary>
/// <remarks>
/// <para>
/// These settings control how the worker instance operates locally,
/// independent of the processor definition. They are not sent to the server.
/// </para>
/// </remarks>
[PublicAPI]
public record WorkerOptions {
    /// <summary>
    /// Number of records to buffer in memory.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> 1000 records</para>
    /// <para><b>Importance:</b> Medium</para>
    /// <para><b>Valid Range:</b> 1 to 10,000 records</para>
    /// <para>
    /// Larger buffers improve throughput but increase memory usage.
    /// The buffer capacity should be at least as large as the batch size.
    /// </para>
    /// </remarks>
    public int BufferCapacity { get; init; } = 1000;

    /// <summary>
    /// Skip record data deserialization for performance-critical scenarios.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> false (decode records)</para>
    /// <para><b>Importance:</b> Low</para>
    /// <para>
    /// When true, records are returned as raw bytes without deserialization,
    /// reducing CPU usage for metadata-only operations.
    /// </para>
    /// </remarks>
    public bool SkipDecoding { get; init; }

    [PublicAPI]
    public sealed class Builder(WorkerOptions settings) : FluentBuilder<Builder, WorkerOptions>(settings) {
        public Builder() : this(new WorkerOptions()) { }

        public Builder BufferCapacity(int capacity) =>
            With(s => s with { BufferCapacity = capacity });

        public Builder SkipDecoding(bool skip = true) =>
            With(s => s with { SkipDecoding = skip });
    }

    public class Validator : Validator<Validator, WorkerOptions> {
        public Validator() {
            RuleFor(settings => settings.BufferCapacity)
                .InclusiveBetween(1, 10000)
                .WithMessage("BufferCapacity must be between 1 and 10000.");
        }
    }
}
