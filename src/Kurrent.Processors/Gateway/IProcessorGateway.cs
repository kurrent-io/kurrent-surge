using Kurrent.Client.Streams;

namespace Kurrent.Processors.Gateway;

/// <summary>
/// Connection factory for processor gateway implementations.
/// Creates bidirectional worker sessions for message streaming.
/// <para>
/// Worker code interacts with the returned <see cref="IWorkerSession"/> for both
/// receiving messages and sending responses.
/// </para>
/// </summary>
public interface IProcessorGateway {
    /// <summary>
    /// Establishes a bidirectional session with the gateway.
    /// </summary>
    /// <param name="settings">The processor settings for this session.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A worker session for consuming messages and sending responses.</returns>
    ValueTask<IWorkerSession> ConnectAsync(ProcessorSettings settings, CancellationToken ct);
}
