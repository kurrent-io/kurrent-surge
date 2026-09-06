using Grpc.Core;

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

    /// <summary>
    /// Pre-warms both channels by running discovery and connecting ahead of the first call.
    /// </summary>
    public async ValueTask ConnectAsync(CancellationToken ct = default) {
        await Write.ConnectAsync(ct).ConfigureAwait(false);

        if (!ReferenceEquals(Write, Read))
            await Read.ConnectAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Completes with <see langword="true"/> when the write channel first reaches Ready —
    /// the first moment the cluster is provably reachable. Passive: it never dials, so a
    /// client that makes no calls never connects and this never completes. Returns
    /// <see langword="false"/> on cancellation or channel disposal.
    /// </summary>
    public async Task<bool> WaitForConnectedAsync(CancellationToken ct = default) {
        try {
            var state = Write.Channel.State;

            while (state != ConnectivityState.Ready) {
                await Write.Channel.WaitForStateChangedAsync(state, ct).ConfigureAwait(false);
                state = Write.Channel.State;
            }

            return true;
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException) {
            return false;
        }
    }

    public async ValueTask DisposeAsync() {
        await Write.DisposeAsync().ConfigureAwait(false);

        if (!ReferenceEquals(Write, Read))
            await Read.DisposeAsync().ConfigureAwait(false);
    }
}
