namespace Kurrent.Client.Admin;

public interface IAdminClient {
    ValueTask ShutdownServer(CancellationToken cancellationToken = default);

    ValueTask ResignNode(CancellationToken cancellationToken = default);

    ValueTask SetNodePriority(int nodePriority, CancellationToken cancellationToken = default);

    ValueTask<ScavengeResponse> StartScavenge(int threadCount = 1, int startFromChunk = 0, CancellationToken cancellationToken = default);

    ValueTask<ScavengeResponse> StopScavenge(string scavengeId, CancellationToken cancellationToken = default);

    IAsyncEnumerable<ScavengeSummary> ListScavenges(CancellationToken cancellationToken = default);

    ValueTask<ScavengeDetails> GetScavengeDetails(string scavengeId, CancellationToken cancellationToken = default);
}
