using Kurrent.Client;
using Kurrent.Client.Schema.NameStrategies;
using Kurrent.Processors.Models;
using Kurrent.Validation;
using Microsoft.Extensions.Configuration;

namespace Kurrent.Processors.Options;

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
///     opts.Checkpointing(c => c
///         .Interval(TimeSpan.FromSeconds(10))
///         .FailurePolicy(CheckpointFailurePolicy.FailProcessor));
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
    /// Number of records to buffer in memory. Default: 1000. Range: 1–10,000.
    /// </summary>
    public int BufferCapacity { get; set; } = 1000;

    /// <summary>
    /// Skip record data deserialization for performance-critical scenarios. Default: false.
    /// </summary>
    public bool SkipDecoding { get; set; }

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

    #region ->> Connection <<-

    /// <summary>
    /// Client options used to build the drone's <see cref="KurrentClient"/>. Set via
    /// one of the <see cref="Connection(string)"/> overloads. The drone always builds
    /// and owns its own <see cref="KurrentClient"/> — client instances are never shared.
    /// </summary>
    public KurrentClientOptions? ClientOptions { get; private set; }

    /// <summary>
    /// Sets the KurrentDB connection string. Parsed immediately into <see cref="ClientOptions"/>.
    /// </summary>
    public void Connection(string connectionString) =>
        ClientOptions = KurrentClientOptions.Parse(connectionString);

    /// <summary>
    /// Sets a connection string and applies additional options configuration. The connection
    /// string is parsed immediately and the configure delegate runs against the parsed options.
    /// </summary>
    public void Connection(string connectionString, Func<KurrentClientOptions, KurrentClientOptions> configure) =>
        ClientOptions = configure(KurrentClientOptions.Parse(connectionString));

    /// <summary>
    /// Sets the client options directly. The drone will use these options when building
    /// its <see cref="KurrentClient"/>.
    /// </summary>
    public void Connection(KurrentClientOptions clientOptions) =>
        ClientOptions = clientOptions;

    #endregion

    #region ->> Convenience Builders <<-

    /// <summary>
    /// Configures checkpoint options via a convenience builder with fluent chaining and ms/TimeSpan overloads.
    /// </summary>
    public void Checkpointing(Action<CheckpointOptions.Builder> configure) =>
        configure(new CheckpointOptions.Builder(Checkpoint));

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
            ProcessorId = sectionName.ToSnakeCase();

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

            RuleFor(o => o.BufferCapacity)
                .InclusiveBetween(1, 10000)
                .WithMessage("BufferCapacity must be between 1 and 10000.");

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
