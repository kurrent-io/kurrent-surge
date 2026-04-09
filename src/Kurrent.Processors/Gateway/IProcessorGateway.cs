using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Processors.Options;

namespace Kurrent.Processors.Gateway;

/// <summary>
/// Gateway for processor control message streaming and worker communication.
/// </summary>
public interface IProcessorGateway : IAsyncDisposable {
    /// <summary>
    /// Connects to the gateway and returns an async stream of control messages.
    /// </summary>
    /// <param name="settings">The processor settings for this connection.</param>
    /// <param name="ct">Cancellation token.</param>
    IAsyncEnumerable<ProcessorControlMessage> ConnectAsync(ProcessorSettings settings, CancellationToken ct);

    /// <summary>
    /// Sends a message from the worker to the gateway.
    /// </summary>
    /// <param name="message">The message to send.</param>
    ValueTask SendAsync(WorkerMessage message);
}
