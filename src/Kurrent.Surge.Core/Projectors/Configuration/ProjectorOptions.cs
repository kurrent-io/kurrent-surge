using System.Diagnostics.Metrics;
using Kurrent.Processors.Configuration;
using Kurrent.Projectors.Transactional;
using Kurrent.Surge.Client;
using Kurrent.Surge.Streaming;
using Kurrent.Surge.Streaming.Models;
using Kurrent.Validation;
using Polly;

namespace Kurrent.Projectors;

/// <summary>
/// Options for running a projection. A backend supplies its own record over this one and nothing else:
/// <c>{Backend}ProjectorOptions : ProjectorOptions&lt;TClient&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// This does not derive from <see cref="ProcessorOptions"/>. It declares what a projection can configure
/// and nothing more, and <see cref="ToProcessorOptions"/> maps it onto the processor options the drone
/// runs on. Inheritance would mean carrying every processor knob and then trying to suppress the ones a
/// projection must not touch — and a suppressed member is only ever suppressed for callers holding the
/// right reference type, which is not a guarantee. Declaring nothing is a guarantee.
/// </para>
/// <para>
/// The identity a drone needs — <c>ProcessorId</c>, <c>DroneId</c> — is derived from
/// <see cref="ProjectionId"/> and <see cref="InstanceId"/> during the map, and partition parallelism is
/// left at its default: a projection's positions and its rows commit in one transaction, which is a
/// single-writer story.
/// </para>
/// </remarks>
/// <typeparam name="TClient">The backend's client type.</typeparam>
[PublicAPI]
public record ProjectorOptions<TClient> : SelfValidating<ProjectorOptions<TClient>, ProjectorOptions<TClient>.Validator>
    where TClient : IAsyncDisposable {
    /// <summary>
    /// Identifies this projection's rows in its checkpoint storage, and the group its resume position
    /// belongs to. Defaults to the projection's type name.
    /// </summary>
    public string ProjectionId { get; set; } = "";

    /// <summary>
    /// Identifies this instance within the projection group. Derived from <see cref="ProjectionId"/> when
    /// left blank.
    /// </summary>
    public string InstanceId { get; set; } = "";

    /// <summary>Supplies the client the projection writes through.</summary>
    public IProjectionClientProvider<TClient>? ClientProvider { get; set; }

    /// <summary>The time provider the projection's checkpoint timestamps are read from.</summary>
    public TimeProvider Time { get; set; } = TimeProvider.System;

    /// <summary>Subscription configuration (filter, initial position).</summary>
    public SubscriptionOptions Subscription { get; init; } = new();

    /// <summary>Batch-read tuning (batch size, window, buffer multiplier).</summary>
    public BatchOptions Batching { get; init; } = new();

    /// <summary>
    /// Checkpoint configuration. The interval decides how much a crash replays and how large a folding
    /// shape's working set grows before it drains.
    /// </summary>
    public ProjectorCheckpointOptions Checkpoint { get; init; } = new();

    /// <summary>Knobs for the SDK's default reconnection pipeline.</summary>
    public ProcessorResilienceOptions Resilience { get; init; } = new();

    /// <summary>When true, all exceptions are re-thrown and additional diagnostic information is generated.</summary>
    public bool Debug { get; set; }

    /// <summary>Skip record data deserialization for performance-critical scenarios.</summary>
    public bool SkipDecoding { get; set; }

    /// <summary>
    /// Report how far behind the readable log head this projection is. Costs one extra unfiltered
    /// backward <c>$all</c> read per refresh, so it is opt-in.
    /// </summary>
    public bool TrackSourceLag { get; set; }

    /// <summary>How often the source-lag probe reads the log head when <see cref="TrackSourceLag"/> is on.</summary>
    public TimeSpan SourceLagRefreshInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Factory used to create the <see cref="Meter"/> instruments are recorded on. Set this under
    /// dependency injection so the container owns meter lifetime; null is correct standalone.
    /// </summary>
    public IMeterFactory? MeterFactory { get; set; }

    /// <summary>Observers registered on the lifecycle seam, in registration order.</summary>
    public List<IProcessorObserver> Observers { get; init; } = [];

    /// <summary>
    /// Client options used to build the projection's source client. Set via one of the
    /// <c>Connection(...)</c> extension overloads, which live in the implementation package since
    /// connection configuration is transport-specific.
    /// </summary>
    internal IKurrentClientOptions? ClientOptions { get; set; }

    #region ->> Convenience Builders <<-

    /// <summary>Sets <see cref="ClientProvider"/> from a delegate.</summary>
    /// <remarks>
    /// A backend whose client carries a contract beyond <typeparamref name="TClient"/> cannot use a
    /// delegate-backed provider and will reject the result when it builds the projector.
    /// </remarks>
    public void ResolveClient(GetProjectionClient<TClient> resolve) =>
        ClientProvider = new DelegateClientProvider(resolve);

    /// <summary>Registers an observer for the lifecycle events, after the built-in telemetry observer.</summary>
    public ProjectorOptions<TClient> AddObserver(IProcessorObserver observer) {
        Observers.Add(observer);
        return this;
    }

    /// <summary>Configures the checkpoint options in place.</summary>
    public void Checkpointing(Action<ProjectorCheckpointOptions> configure) =>
        configure(Checkpoint);

    #endregion

    #region ->> Resilience <<-

    internal Action<ResiliencePipelineBuilder>? ResilienceExtension   { get; private set; }
    internal Action<ResiliencePipelineBuilder>? ResilienceReplacement { get; private set; }

    /// <summary>Tweaks the default reconnection pipeline without any Polly types.</summary>
    public void ConfigureResilience(Action<ProcessorResilienceOptions> configure) =>
        configure(Resilience);

    /// <summary>Prepends user strategies ahead of the SDK's default retry, which is kept.</summary>
    public void ExtendResilience(Action<ResiliencePipelineBuilder> extend) {
        ResilienceExtension   = extend;
        ResilienceReplacement = null;
    }

    /// <summary>Replaces the pipeline entirely — the SDK defaults are skipped.</summary>
    public void ConfigureResilience(Action<ResiliencePipelineBuilder> configure) {
        ResilienceReplacement = configure;
        ResilienceExtension   = null;
    }

    #endregion

    /// <summary>
    /// Projects these options onto the <see cref="ProcessorOptions"/> the drone runs on.
    /// </summary>
    /// <remarks>
    /// The mapping is where a projection's invariants are enforced, rather than by suppressing members
    /// a caller could reach anyway. A fresh <see cref="CheckpointOptions"/> carries only the interval, so
    /// <c>AutoTrack</c> and <c>AutoCheckpoint</c> keep their defaults: a projection commits from
    /// <c>OnCheckpoint</c> using the positions the drone tracked, and must not track its own, so
    /// disabling either would leave it storing nothing and replaying the subscription forever.
    /// <c>MaxParallelism</c> is left at one for the same reason.
    /// </remarks>
    public ProcessorOptions ToProcessorOptions() {
        var options = new ProcessorOptions {
            ProcessorId             = ProjectionId,
            Subscription            = Subscription,
            Batching                = Batching,
            Resilience              = Resilience,
            Observers               = Observers,
            MeterFactory            = MeterFactory,
            Debug                   = Debug,
            SkipDecoding            = SkipDecoding,
            TrackSourceLag          = TrackSourceLag,
            SourceLagRefreshInterval = SourceLagRefreshInterval,
            Time                    = Time,
            Checkpoint              = new CheckpointOptions { Interval = Checkpoint.Interval },
            ClientOptions           = ClientOptions
        };

        // Assigning ProcessorId derives a DroneId when none is set, so an unconditional assignment here
        // would overwrite that derivation with the blank InstanceId.
        if (!string.IsNullOrWhiteSpace(InstanceId))
            options.DroneId = InstanceId;

        if (ResilienceReplacement is { } replacement)
            options.ConfigureResilience(replacement);
        else if (ResilienceExtension is { } extension)
            options.ExtendResilience(extension);

        return options;
    }

    sealed class DelegateClientProvider(GetProjectionClient<TClient> resolve) : IProjectionClientProvider<TClient> {
        public ValueTask<TClient> GetClient(CancellationToken cancellationToken) => resolve(cancellationToken);
    }

    #region ->> Validation <<-

    public class Validator : Validator<Validator, ProjectorOptions<TClient>> {
        public override string ErrorCode => "INVALID_PROJECTOR_OPTIONS";

        public Validator() {
            RuleFor(o => o.ProjectionId)
                .NotEmpty()
                .WithMessage("ProjectionId is required for projection identification and checkpointing.");

            RuleFor(o => o.ClientProvider)
                .Must(provider => provider is not null,
                    "A client provider is required. Set ClientProvider, or call ResolveClient(...) with a delegate.");

            RuleFor(o => o.Checkpoint.Interval)
                .GreaterThan(TimeSpan.Zero)
                .WithMessage("Checkpoint.Interval must be greater than zero.");
        }
    }

    #endregion
}

/// <summary>
/// The checkpoint knobs a projection can set. The switches that gate automatic tracking and committing
/// are absent by construction — see <see cref="ProjectorOptions{TClient}.ToProcessorOptions"/>.
/// </summary>
[PublicAPI]
public record ProjectorCheckpointOptions {
    /// <summary>
    /// Time between checkpoints, and so how long the transaction between them stays open. Default: 30 seconds.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Sets <see cref="Interval"/> and returns this instance for fluent chaining.</summary>
    public ProjectorCheckpointOptions IntervalFrom(TimeSpan interval) {
        Interval = interval;
        return this;
    }

    /// <inheritdoc cref="IntervalFrom(TimeSpan)"/>
    public ProjectorCheckpointOptions IntervalFrom(long milliseconds) {
        Interval = TimeSpan.FromMilliseconds(milliseconds);
        return this;
    }
}
