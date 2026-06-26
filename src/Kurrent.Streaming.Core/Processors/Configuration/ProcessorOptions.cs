using Kurrent.Client;
using Kurrent.Schema.Naming;
using Kurrent.Streaming;
using Kurrent.Streaming.Models;
using Kurrent.Streaming.Options;
using Kurrent.Validation;
using Microsoft.Extensions.Configuration;
using Polly;

namespace Kurrent.Processors.Configuration;

/// <summary>
/// Configuration for a Kurrent processor. Pure data — processor wiring lives on the
/// <see cref="KurrentProcessor"/> itself, not on options.
/// </summary>
/// <example>
/// <code>
/// KurrentProcessor.Create(new OrderProcessor(), opts => {
///     opts.Connection("kurrentdb://localhost:2113");
///     opts.ProcessorId                  = "order-processor";
///     opts.Subscription.Filter          = RecordFilter.ByStream("orders");
///     opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
///     opts.Checkpointing(c => {
///         c.IntervalFrom(TimeSpan.FromSeconds(10));
///         c.FailurePolicy = CheckpointFailurePolicy.Fail;
///     });
/// }).RunAsync(ct);
/// </code>
/// </example>
[PublicAPI]
public record ProcessorOptions : SelfValidating<ProcessorOptions, ProcessorOptions.Validator> {
    /// <summary>
    /// Unique identifier for the processor group. Used for checkpoint coordination.
    /// When not set, auto-derived from the processor type name.
    /// Setting this auto-derives <see cref="DroneId"/> as <c>{ProcessorId}-{short-guid}</c>
    /// unless DroneId was already assigned.
    /// </summary>
    public string ProcessorId {
        get;
        set {
            field = value;
            if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(DroneId))
                DroneId = $"{value}-{Guid.NewGuid().ToString("N")[^8..]}";
        }
    } = "";

    /// <summary>
    /// Unique identifier for this drone instance within the processor group.
    /// When left blank, auto-derived from <see cref="ProcessorId"/> as <c>{ProcessorId}-{short-guid}</c>.
    /// Use a consistent value (e.g., machine name) for sticky partitioning.
    /// </summary>
    public string DroneId { get; set; } = "";

    /// <summary>
    /// When true, all exceptions are re-thrown and additional diagnostic information is generated.
    /// </summary>
    public bool Debug { get; set; }

    /// <summary>
    /// Skip record data deserialization for performance-critical scenarios. Default: false.
    /// </summary>
    public bool SkipDecoding { get; set; }

    /// <summary>
    /// When true, the drone periodically reads the readable log head and reports the
    /// <c>kurrent_processor_source_lag_bytes</c> / <c>_seconds</c> gauges — how far behind the head
    /// the drone is. Default: false. Each refresh costs one extra unfiltered backward <c>$all</c> read,
    /// so it is opt-in; the local <c>uncommitted_*</c> gauges are always free.
    /// </summary>
    public bool TrackSourceLag { get; set; }

    /// <summary>
    /// Maximum number of partitions (key groups). Range: 1-32768.
    /// Reserved for partitioned processing — not yet exposed as a public knob.
    /// </summary>
    internal int MaxParallelism { get; set; } = 1;
    
    /// <summary>
    /// Subscription configuration (filter, initial position).
    /// </summary>
    public SubscriptionOptions Subscription { get; init; } = new();

    /// <summary>
    /// Checkpoint configuration (interval, failure policy).
    /// </summary>
    public CheckpointOptions Checkpoint { get; init; } = new();

    /// <summary>
    /// Batch-read tuning (batch size, window, buffer multiplier).
    /// </summary>
    public BatchOptions Batching { get; init; } = new();

    /// <summary>
    /// Knobs for the SDK's default reconnection pipeline. Tweak via
    /// <see cref="ConfigureResilience(Action{ProcessorResilienceOptions})"/> or bind from
    /// configuration; ignored when the pipeline is replaced via
    /// <see cref="ConfigureResilience(Action{ResiliencePipelineBuilder})"/>.
    /// </summary>
    public ProcessorResilienceOptions Resilience { get; init; } = new();

    #region ->> Connection <<-

    /// <summary>
    /// Client options used to build the drone's <see cref="KurrentClient"/>. Set via
    /// one of the <see cref="Connection(string)"/> overloads. The drone always builds
    /// and owns its own <see cref="KurrentClient"/> — client instances are never shared.
    /// </summary>
    internal KurrentClientOptions? ClientOptions { get; private set; }

    /// <summary>
    /// Sets the KurrentDB connection string.
    /// </summary>
    public void Connection(string connectionString) =>
        ClientOptions = KurrentClientOptions.Parse(connectionString);

    /// <summary>
    /// Sets a connection string and applies additional options configuration.
    /// </summary>
    public void Connection(string connectionString, Action<KurrentClientOptions> configure) {
        ClientOptions = KurrentClientOptions.Parse(connectionString);
        configure(ClientOptions);
    }

    /// <summary>
    /// Sets the client options directly.
    /// </summary>
    public void Connection(KurrentClientOptions clientOptions) =>
        ClientOptions = clientOptions;

    #endregion

    #region ->> Convenience Builders <<-

    /// <summary>
    /// Configures the processor's <see cref="CheckpointOptions"/> in place. Set properties directly
    /// or use the fluent <see cref="CheckpointOptions.IntervalFrom(TimeSpan)"/> helpers.
    /// </summary>
    public void Checkpointing(Action<CheckpointOptions> configure) =>
        configure(Checkpoint);

    #endregion

    #region ->> Resilience <<-

    /// <summary>
    /// User strategies prepended ahead of the SDK defaults. Mutually exclusive with a full
    /// replacement — setting one clears the other, last call wins.
    /// </summary>
    internal Action<ResiliencePipelineBuilder>? ResilienceExtension { get; private set; }

    /// <summary>
    /// Full pipeline replacement — the user owns the pipeline and the SDK defaults are skipped.
    /// </summary>
    internal Action<ResiliencePipelineBuilder>? ResilienceReplacement { get; private set; }

    /// <summary>
    /// Tweaks the default reconnection pipeline knobs (attempts, backoff, retryable status
    /// codes) without any Polly types. The SDK retry strategy stays in charge.
    /// </summary>
    public void ConfigureResilience(Action<ProcessorResilienceOptions> configure) =>
        configure(Resilience);

    /// <summary>
    /// Prepends user strategies (circuit breaker, hedging, …) ahead of the SDK's default retry,
    /// which is appended after them so its transient-error knowledge is preserved.
    /// </summary>
    public void ExtendResilience(Action<ResiliencePipelineBuilder> extend) {
        ResilienceExtension   = extend;
        ResilienceReplacement = null;
    }

    /// <summary>
    /// Replaces the pipeline entirely — the SDK defaults are skipped and the user owns every
    /// strategy. Append <see cref="ProcessorResilienceStrategy.DefaultRetry"/> to keep the SDK's
    /// transient-error knowledge.
    /// </summary>
    public void ConfigureResilience(Action<ResiliencePipelineBuilder> configure) {
        ResilienceReplacement = configure;
        ResilienceExtension   = null;
    }

    /// <summary>
    /// Builds the subscription reconnection pipeline from the configured mode: replacement wins,
    /// otherwise user extensions are prepended and the SDK default retry appended. Polly telemetry
    /// is wired here so every strategy reports through the processor's logging.
    /// </summary>
    internal ResiliencePipeline BuildResiliencePipeline(ILoggerFactory loggerFactory) {
        var builder = new ResiliencePipelineBuilder().ConfigureTelemetry(loggerFactory);

        if (ResilienceReplacement is not null) {
            ResilienceReplacement(builder);
            return builder.Build();
        }

        ResilienceExtension?.Invoke(builder);

        // MaxRetryAttempts = 0 means "retries disabled"; Polly rejects it at build time, so the
        // default strategy is simply not added.
        if (Resilience.MaxRetryAttempts > 0)
            builder.AddRetry(ProcessorResilienceStrategy.DefaultRetry(Resilience));

        return builder.Build();
    }

    #endregion

    #region ->> Configuration Binding <<-

    /// <summary>
    /// Loads options from an <see cref="IConfiguration"/> section.
    /// Section path: <c>Kurrent:Processors:{sectionName}</c>.
    /// Values loaded from config can be overridden by subsequent property assignments.
    /// </summary>
    /// <param name="configuration">The configuration root.</param>
    /// <param name="sectionName">
    /// The section name under <c>Kurrent:Processors</c>.
    /// If ProcessorId is not set in config, it will be auto-generated from this name.
    /// </param>
    public void FromConfiguration(IConfiguration configuration, string sectionName) {
        var sectionPath = $"Kurrent:Processors:{sectionName}";
        var section     = configuration.GetSection(sectionPath);

        if (!section.Exists()) return;

        section.Bind(this, o => o.BindNonPublicProperties = true);

        // Auto-generate ProcessorId from section name if not set in config
        if (string.IsNullOrEmpty(ProcessorId))
            ProcessorId = sectionName.ToKebabCase();

        // Check for connection string: ConnectionStrings:{sectionName} or ConnectionStrings:Default
        var connectionString = configuration.GetConnectionString(sectionName)
                            ?? configuration.GetConnectionString("Default");

        if (connectionString is not null)
            Connection(connectionString);
    }

    #endregion

    #region ->> Validation <<-

    public class Validator : Validator<Validator, ProcessorOptions> {
        public Validator() {
            RuleFor(o => o.ProcessorId)
                .NotEmpty()
                .WithMessage("ProcessorId is required for processor identification and checkpointing.");

            RuleFor(o => o.MaxParallelism)
                .InclusiveBetween(1, 32768)
                .WithMessage("MaxParallelism must be between 1 and 32768.");

            RuleFor(o => o.Batching.MaxBatchSize)
                .InclusiveBetween(1, 10000)
                .WithMessage("Batching.MaxBatchSize must be between 1 and 10000.");

            RuleFor(o => o.Batching.BatchWindow)
                .GreaterThan(TimeSpan.Zero)
                .WithMessage("Batching.BatchWindow must be greater than zero.");

            RuleFor(o => o.Batching.BufferMultiplier)
                .InclusiveBetween(1, 100)
                .WithMessage("Batching.BufferMultiplier must be between 1 and 100.");

            RuleFor(o => o.Checkpoint.Interval)
                .GreaterThan(TimeSpan.Zero)
                .WithMessage("Checkpoint.Interval must be greater than zero.");

            RuleFor(o => o.Checkpoint.RetentionCount)
                .InclusiveBetween(1, 100)
                .WithMessage("Checkpoint.RetentionCount must be between 1 and 100.");

            RuleFor(o => o.Checkpoint.FailurePolicy)
                .IsInEnum()
                .WithMessage("Checkpoint.FailurePolicy must be a valid CheckpointFailurePolicy value.");

            RuleFor(o => o.Subscription.InitialPosition)
                .IsInEnum()
                .WithMessage($"Subscription.InitialPosition must be one of: {string.Join(", ", Enum.GetNames<SubscriptionInitialPosition>())}.");

            RuleFor(o => o.Resilience.MaxRetryAttempts)
                .GreaterThanOrEqualTo(0)
                .WithMessage("Resilience.MaxRetryAttempts must be zero or greater.");

            RuleFor(o => o.Resilience.BaseDelay)
                .GreaterThan(TimeSpan.Zero)
                .WithMessage("Resilience.BaseDelay must be greater than zero.");

            RuleFor(o => o.Resilience.MaxDelay)
                .GreaterThanOrEqualTo(o => o.Resilience.BaseDelay)
                .WithMessage("Resilience.MaxDelay must be greater than or equal to Resilience.BaseDelay.");

            RuleFor(o => o.Resilience.RetryableStatusCodes)
                .Must(codes => codes.Length > 0, "Resilience.RetryableStatusCodes cannot be empty.");

            RuleFor(o => o.ClientOptions)
                .Must(clientOptions => clientOptions is not null,
                    "No connection configured. Call Connection() to set a connection string or provide client options.");

            When(o => o.MaxParallelism > 1,
                () => RuleFor(o => o.Subscription.PartitionKeySelector)
                    .NotEmpty()
                    .WithMessage("Subscription.PartitionKeySelector is required when MaxParallelism is greater than 1."));
        }
    }

    #endregion
}
