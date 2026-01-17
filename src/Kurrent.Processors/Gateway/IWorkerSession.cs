using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Processors.Gateway;

/// <summary>
/// Represents a bidirectional session between a worker and the processor gateway.
/// Provides both inbound message streaming and outbound message sending capabilities.
/// </summary>
[PublicAPI]
public interface IWorkerSession : IAsyncEnumerable<ProcessorControlMessage>, IAsyncDisposable {
    /// <summary>
    /// Unique identifier for this processing session.
    /// </summary>
    SessionId SessionId { get; }

    /// <summary>
    /// Sends a message to the server through this session.
    /// </summary>
    /// <param name="message">The message to send.</param>
    ValueTask SendAsync(WorkerMessage message);
}
