using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Microsoft.Extensions.Configuration;
// ReSharper disable ClassNeverInstantiated.Global

namespace Kurrent.Streaming.Sinks;

/// <summary>
/// Base context for sink operations.
/// </summary>
public abstract record SinkContext {
    public ILogger           Logger            { get; internal init; } = NullLogger.Instance;
    public CancellationToken CancellationToken { get; internal init; } = CancellationToken.None;
}

/// <summary>
/// Opaque snapshot of sink state for recovery.
/// </summary>
public sealed record SinkStateSnapshot(ReadOnlyMemory<byte> Data) {
    public static readonly SinkStateSnapshot None = new(ReadOnlyMemory<byte>.Empty);

    public bool IsEmpty => Data.IsEmpty;

    public static implicit operator ReadOnlyMemory<byte>(SinkStateSnapshot snapshot) => snapshot.Data;
    public static implicit operator SinkStateSnapshot(ReadOnlyMemory<byte> data)     => new(data);
}

public record SinkOpenContext : SinkContext {
    /// <summary>
    /// Initial state snapshot for the sink, if any.
    /// If a previous transaction was interrupted, this will contain the state at the time of interruption.
    /// In a normal situation, it might be empty or, depending on the sink implementation, contain the last committed state.
    /// </summary>
    public SinkStateSnapshot StateSnapshot { get; internal init; } = SinkStateSnapshot.None;

    /// <summary>
    /// Configuration settings for the sink.
    /// </summary>
    public IConfiguration Configuration { get; internal init; } = null!;

    /// <summary>
    /// Service provider for resolving dependencies.
    /// </summary>
    public IServiceProvider ServiceProvider { get; internal init; } = null!;
}

/// <summary>
/// Context provided when writing a message to the sink.
/// </summary>
/// <param name="Message"></param>
/// <typeparam name="T"></typeparam>
public record SinkWriteContext<T>(T Message) : SinkContext;

/// <summary>
/// Context provided when closing the sink.
/// </summary>
public record SinkCloseContext : SinkContext;

/// <summary>
/// Context provided when flushing the sink.
/// </summary>
public record SinkFlushContext : SinkContext {
    public Checkpoint Checkpoint { get; internal init; }
}

/// <summary>
/// Defines the contract for a sink in the EventStore system.
/// </summary>
public interface ISink {
    /// <summary>
    /// Initializes the sink with the provided SinkContext. This method is optional and can be overridden to perform any necessary setup.
    /// </summary>
    /// <param name="context">The context for the sink's operation, providing configuration settings and services.</param>
    ValueTask Open(SinkOpenContext context);

    /// <summary>
    /// Writes a record to the sink. This method is asynchronous, allowing it to perform I/O operations without blocking the calling thread.
    /// </summary>
    /// <param name="context">The record to be written and the context for the operation.</param>
    ValueTask Write<T>(SinkWriteContext<T> context);

    /// <summary>
    /// Performs any necessary cleanup when the sink is no longer needed.
    /// This method is optional and can be overridden to close resources like file handles or network connections.
    /// </summary>
    /// <param name="context">The context for the sink's operation</param>
    ValueTask Close(SinkCloseContext context) => ValueTask.CompletedTask;

    /// <summary>
    /// Begin a flush operation to ensure all buffered messages are sent to the external system.
    /// Called by the runtime on checkpoint.
    /// </summary>
    ValueTask<ISinkFlush> Flush(SinkFlushContext context);
}

/// <summary>
/// Represents an ongoing flush operation or a coordinator for two-phase commit sinks.
/// </summary>
public interface ISinkFlush {
    /// <summary>
    /// Opaque checkpoint data representing the state of the sink at the time of flush.
    /// </summary>
    /// <remarks>
    /// This data is stored in the checkpoint and provided to the sink during recovery.
    /// The sink can use this data to restore its state to ensure exactly-once semantics.
    /// </remarks>
    SinkStateSnapshot StateSnapshot { get; }

    /// <summary>
    /// Ensure all buffered messages were handled successfully and sent to the external system, completing the flush operation.
    /// </summary>
    ValueTask Complete();

    /// <summary>
    /// Abort the flush operation, discarding any buffered messages.
    /// </summary>
    ValueTask Abort();
}

/// <summary>
/// Represents a transaction for two-phase commit systems.
/// </summary>
public interface ITransaction {
    /// <summary>
    /// Complete the transaction, ensuring all operations were successful.
    /// </summary>
    ValueTask Complete();

    /// <summary>
    /// Abort the transaction, ideally rolling back any partial changes.
    /// </summary>
    /// <returns></returns>
    ValueTask Abort();
}
