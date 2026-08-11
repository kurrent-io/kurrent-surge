using Kurrent.Surge.Client;
using KurrentDB.Client;

namespace Kurrent.Surge.Client.LoadBalancing;

/// <summary>
/// Owns the write and read <see cref="ClusterChannel"/> pair for a client. Write is always
/// leader-pinned. Read collapses onto the write channel when the configured read preference
/// is <see cref="NodeReadPreference.Leader"/> or the connection scheme is <see cref="KurrentConnectionScheme.Direct"/>;
/// otherwise it is a separate channel honoring <see cref="KurrentClientGossipOptions.ReadPreference"/>.
/// </summary>
sealed class ClusterChannels : IAsyncDisposable {
    ClusterChannels(ClusterChannel write, ClusterChannel read) {
        Write = write;
        Read  = read;
    }

    public ClusterChannel Write { get; }
    public ClusterChannel Read  { get; }

    public static ClusterChannels Create(KurrentClientOptions options) {
        var write = ClusterChannel.Create(options, NodeReadPreference.Leader);

        var collapse = options.Gossip.ReadPreference == NodeReadPreference.Leader
                    || options.ConnectionScheme == KurrentConnectionScheme.Direct;

        var read = collapse ? write : ClusterChannel.Create(options);

        return new(write, read);
    }

    public async ValueTask DisposeAsync() {
        await Write.DisposeAsync().ConfigureAwait(false);

        if (!ReferenceEquals(Write, Read))
            await Read.DisposeAsync().ConfigureAwait(false);
    }
}
