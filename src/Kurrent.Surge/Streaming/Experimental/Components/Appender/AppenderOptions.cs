using System.Threading.Channels;
using Kurrent.Surge.Client;
using Kurrent.Validation;

namespace Kurrent.Surge.Experimental;

/// <summary>
/// Configuration for the <see cref="Appender"/>. Controls batching thresholds,
/// backpressure behavior, and the connection to KurrentDB.
/// </summary>
sealed class AppenderOptions : SelfValidatingClass<AppenderOptions, AppenderOptions.Validator> {
    /// <summary>
    /// Maximum number of records per batch. The flush loop sends a batch when this
    /// count is reached, even if the byte threshold hasn't been hit.
    /// Default: 500.
    /// </summary>
    public int MaxBatchCount { get; set; } = 500;

    /// <summary>
    /// Maximum serialized byte size per batch. The flush loop sends a batch when the
    /// cumulative serialized size of its records reaches this threshold. Size is exact —
    /// each record is serialized on the caller's thread before entering the channel.
    /// Default: 1 MB.
    /// </summary>
    public int MaxBatchBytes { get; set; } = 1_048_576;

    /// <summary>
    /// Maximum time to wait for a full batch before flushing a partial one.
    /// Prevents latency spikes when throughput is low.
    /// Default: 5 ms.
    /// </summary>
    public TimeSpan LingerTimeout { get; set; } = TimeSpan.FromMilliseconds(5);

    /// <summary>
    /// Bounded channel capacity. Controls how many records can be buffered before
    /// backpressure kicks in.
    /// Default: 10,000.
    /// </summary>
    public int BufferCapacity { get; set; } = 10_000;

    /// <summary>
    /// Number of parallel flush lanes for keyed records. Records with different ordering
    /// keys may be flushed in parallel across lanes. Records without an ordering key
    /// always use a single dedicated lane, preserving natural append order.
    /// Default: 2.
    /// </summary>
    public int Concurrency { get; set; } = 2;

    /// <summary>
    /// Behavior when the internal buffer is full. Only <see cref="BoundedChannelFullMode.Wait"/>
    /// is supported: a full buffer applies backpressure to the caller. The drop modes are
    /// rejected by validation.
    /// </summary>
    /// <remarks>
    /// The channel's drop modes evict records without completing their callbacks — a caller
    /// would wait forever for an acknowledgment that can no longer arrive. They stay rejected
    /// until drop semantics have a designed acknowledgment story.
    /// </remarks>
    public BoundedChannelFullMode FullMode { get; set; } = BoundedChannelFullMode.Wait;

    /// <summary>
    /// Global error handler invoked when a batch fails and individual records used
    /// the callback-based <see cref="Appender.Append(Client.Streams.AppendRecord, Action{AppendResult})"/>.
    /// </summary>
    public Action<AppendError>? OnError { get; set; }

    #region ->> Connection <<-

    /// <summary>
    /// Client options used to build the appender's internal <see cref="KurrentClient"/>.
    /// Set via one of the <see cref="Connection(string)"/> overloads.
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

    #region ->> Validation <<-

    public sealed class Validator : Validator<Validator, AppenderOptions> {
        public Validator() {
            RuleFor(o => o.MaxBatchCount)
                .InclusiveBetween(1, 10_000)
                .WithMessage("MaxBatchCount must be between 1 and 10,000.");

            RuleFor(o => o.MaxBatchBytes)
                .InclusiveBetween(1024, 100 * 1_048_576)
                .WithMessage("MaxBatchBytes must be between 1 KB and 100 MB.");

            RuleFor(o => o.LingerTimeout)
                .GreaterThan(TimeSpan.Zero)
                .WithMessage("LingerTimeout must be greater than zero.");

            RuleFor(o => o.Concurrency)
                .InclusiveBetween(1, 32)
                .WithMessage("Concurrency must be between 1 and 32.");

            RuleFor(o => o.BufferCapacity)
                .InclusiveBetween(1, 100_000)
                .WithMessage("BufferCapacity must be between 1 and 100,000.");

            RuleFor(o => o.FullMode)
                .Must(mode => mode is BoundedChannelFullMode.Wait,
                    "FullMode must be Wait. The drop modes evict buffered records without completing their callbacks.");

            RuleFor(o => o.ClientOptions)
                .Must(clientOptions => clientOptions is not null,
                    "No connection configured. Call Connection() to set a connection string or provide client options.");
        }
    }

    #endregion
}
