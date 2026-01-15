using Kurrent.Client.Streams;
using Kurrent.Client.Streams.Processing;

namespace Kurrent.Client.Processors.Gateway;

/// <summary>
/// Connection factory for processor gateway implementations.
/// Creates bidirectional worker sessions for message streaming.
/// <para>
/// Worker code interacts with the returned <see cref="IWorkerSession"/> for both
/// receiving messages and sending responses.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <b>Implementations:</b>
/// </para>
/// <list type="bullet">
///   <item><description><c>LegacyGateway</c> - V1 protocol using StreamsClient</description></item>
///   <item><description><c>GrpcGateway</c> - V2 bidirectional protocol (future)</description></item>
/// </list>
/// </remarks>
public interface IProcessorGateway : IAsyncDisposable {
    /// <summary>
    /// Establishes a bidirectional session with the gateway.
    /// </summary>
    /// <param name="settings">The processor settings for this session.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A worker session for consuming messages and sending responses.</returns>
    ValueTask<IWorkerSession> ConnectAsync(ProcessorSettings settings, CancellationToken ct);
}
