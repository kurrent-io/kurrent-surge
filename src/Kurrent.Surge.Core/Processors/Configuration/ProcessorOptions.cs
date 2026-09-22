using System.Diagnostics.Metrics;
using Kurrent.Surge.Client;
using Kurrent.Surge.Processors.Models;
using Kurrent.Surge.Validation;
using Polly;

namespace Kurrent.Surge.Processors.Configuration;

/// <summary>
/// Configuration for a Kurrent processor. Pure data — processor wiring lives on
/// <c>AddKurrentSurge</c>, not on options.
/// </summary>
[PublicAPI]
[WithValidation]
public sealed partial class ProcessorOptions {
    bool    _droneIdDerived;
    string? _droneIdSuffix;

    /// <summary>
    /// Unique identifier for the processor group. Used for checkpoint coordination. Assigned at
    /// registration: the id passed, else the processor type name in kebab-case. A blank id reads as
    /// none given, so it also takes the type name. Setting it here does not change which
    /// configuration section was read.
    /// </summary>
    /// <remarks>
    /// Until <see cref="DroneId"/> is set explicitly, setting this derives it as
    /// <c>{ProcessorId}-{short-guid}</c>, and a later change of this id re-derives it with the same
    /// short guid.
    /// </remarks>
    public string ProcessorId {
        get;
        set {
            field = value;

            if (!string.IsNullOrWhiteSpace(value) && (string.IsNullOrWhiteSpace(DroneId) || _droneIdDerived)) {
                _droneIdSuffix  ??= Guid.NewGuid().ToString("N")[^8..];
                DroneId           = $"{value}-{_droneIdSuffix}";
                _droneIdDerived   = true;
            }
        }
    } = "";

    /// <summary>
    /// Unique identifier for this drone instance within the processor group. Derived from
    /// <see cref="ProcessorId"/> as <c>{ProcessorId}-{short-guid}</c> until set explicitly, and follows a
    /// later <see cref="ProcessorId"/> change. Use a consistent value (e.g., machine name) for sticky
    /// partitioning.
    /// </summary>
    /// <remarks>
    /// Assigning the current value again does not count as setting it explicitly, so configuration
    /// binding that writes back an unconfigured value keeps the derivation.
    /// </remarks>
    public string DroneId {
        get;
        set {
            if (!string.IsNullOrWhiteSpace(value) && !string.Equals(field, value, StringComparison.Ordinal))
                _droneIdDerived = false;

            field = value;
        }
    } = "";

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
    /// How often the source-lag probe reads the log head when <see cref="TrackSourceLag"/> is on.
    /// Default: 10 seconds. Internal — a tuning knob for tests and future exposure, not yet a public
    /// surface.
    /// </summary>
    public TimeSpan SourceLagRefreshInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Factory used to create the <see cref="Meter"/> the processor records its instruments on.
    /// Default: null, in which case the processor constructs and owns its own meter.
    /// </summary>
    /// <remarks>
    /// Set this under dependency injection so the container owns meter lifetime and keeps
    /// identically-named meters isolated per service collection — <c>AddKurrentSurge</c> wires it
    /// automatically. Outside a container leave it null: no public
    /// <see cref="IMeterFactory"/> implementation exists there. Mirrors <c>HttpClientHandler.MeterFactory</c>.
    /// </remarks>
    public IMeterFactory? MeterFactory { get; set; }

    /// <summary>
    /// Observers registered on the processor's lifecycle seam, in registration order.
    /// </summary>
    public List<IProcessorObserver> Observers { get; set; } = [];
    
    /// <summary>
    /// Maximum number of partitions (key groups). Range: 1-32768.
    /// Reserved for partitioned processing — not yet exposed as a public knob.
    /// </summary>
    internal int MaxParallelism { get; set; } = 1;
    
    /// <summary>
    /// Subscription configuration (filter, initial position).
    /// </summary>
    public SubscriptionOptions Subscription { get; set; } = new();

    /// <summary>
    /// Checkpoint configuration (interval, failure policy).
    /// </summary>
    public CheckpointOptions Checkpoint { get; set; } = new();

    /// <summary>
    /// Batch-read tuning (batch size, window, buffer multiplier).
    /// </summary>
    public BatchOptions Batching { get; set; } = new();

    /// <summary>
    /// Knobs for the SDK's default reconnection pipeline. Tweak via
    /// <see cref="ConfigureResilience(Action{ProcessorResilienceOptions})"/> or bind from
    /// configuration; ignored when the pipeline is replaced via
    /// <see cref="ConfigureResilience(Action{ResiliencePipelineBuilder})"/>.
    /// </summary>
    public ProcessorResilienceOptions Resilience { get; set; } = new();

    /// <summary>
    /// Client options used to build the processor's client. Set via one of the
    /// <c>Connection(...)</c> extension overloads, which live in the implementation package
    /// since connection configuration is transport-specific. The processor always builds and
    /// owns its own client — client instances are never shared.
    /// </summary>
    internal IKurrentClientOptions? ClientOptions { get; set; }

    #region ->> Convenience Builders <<-

    /// <summary>
    /// Registers an observer for the processor's lifecycle events.
    /// </summary>
    /// <remarks>
    /// Observers are invoked synchronously on the processing thread in registration order, after the
    /// built-in telemetry observer. An observer that throws is logged and skipped — it never disturbs
    /// the pipeline or the remaining observers. Observers that transform or replace records do not
    /// belong here; wrap the processor in a decorator instead.
    /// </remarks>
    public ProcessorOptions AddObserver(IProcessorObserver observer) {
        Observers.Add(observer);
        return this;
    }
    
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
    /// strategy. Append <c>ProcessorResilienceStrategy.DefaultRetry</c> to keep the SDK's
    /// transient-error knowledge.
    /// </summary>
    public void ConfigureResilience(Action<ResiliencePipelineBuilder> configure) {
        ResilienceReplacement = configure;
        ResilienceExtension   = null;
    }

    #endregion
    
    public TimeProvider Time { get; set; } = TimeProvider.System;

    #region ->> Validation <<-

    static partial void ConfigureRules(Validator rules) {
        rules.RuleFor(o => o.ProcessorId)
            .NotEmpty()
            .WithMessage("ProcessorId is required for processor identification and checkpointing.");

        rules.RuleFor(o => o.MaxParallelism)
            .InclusiveBetween(1, 32768)
            .WithMessage("MaxParallelism must be between 1 and 32768.");

        rules.RuleFor(o => o.Batching.MaxBatchSize)
            .InclusiveBetween(1, 10000)
            .WithMessage("Batching.MaxBatchSize must be between 1 and 10000.");

        rules.RuleFor(o => o.Batching.BatchWindow)
            .GreaterThan(TimeSpan.Zero)
            .WithMessage("Batching.BatchWindow must be greater than zero.");

        rules.RuleFor(o => o.Batching.BufferMultiplier)
            .InclusiveBetween(1, 100)
            .WithMessage("Batching.BufferMultiplier must be between 1 and 100.");

        rules.RuleFor(o => o.Checkpoint.Interval)
            .GreaterThan(TimeSpan.Zero)
            .WithMessage("Checkpoint.Interval must be greater than zero.");

        rules.RuleFor(o => o.Checkpoint.RetentionCount)
            .InclusiveBetween(1, 100)
            .WithMessage("Checkpoint.RetentionCount must be between 1 and 100.");

        rules.RuleFor(o => o.Checkpoint.FailurePolicy)
            .IsInEnum()
            .WithMessage("Checkpoint.FailurePolicy must be a valid CheckpointFailurePolicy value.");

        rules.RuleFor(o => o.Subscription.InitialPosition)
            .IsInEnum()
            .WithMessage($"Subscription.InitialPosition must be one of: {string.Join(", ", Enum.GetNames<SubscriptionInitialPosition>())}.");

        rules.RuleFor(o => o.Resilience.MaxRetryAttempts)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Resilience.MaxRetryAttempts must be zero or greater.");

        rules.RuleFor(o => o.Resilience.BaseDelay)
            .GreaterThan(TimeSpan.Zero)
            .WithMessage("Resilience.BaseDelay must be greater than zero.");

        rules.RuleFor(o => o.Resilience.MaxDelay)
            .GreaterThanOrEqualTo(o => o.Resilience.BaseDelay)
            .WithMessage("Resilience.MaxDelay must be greater than or equal to Resilience.BaseDelay.");

        rules.RuleFor(o => o.ClientOptions)
            .Must(clientOptions => clientOptions is not null,
                "No connection configured. Call Connection() to set a connection string or provide client options.");

        rules.When(o => o.MaxParallelism > 1,
            () => rules.RuleFor(o => o.Subscription.PartitionKeySelector)
                .NotEmpty()
                .WithMessage("Subscription.PartitionKeySelector is required when MaxParallelism is greater than 1."));
    }

    #endregion
}
