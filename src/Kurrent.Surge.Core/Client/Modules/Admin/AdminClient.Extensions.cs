namespace Kurrent.Surge.Client.Admin;

public static class AdminClientExtensions {
    extension(IAdminClient client) {
        /// <summary>
        /// Starts a scavenge with the default thread count, from the first chunk.
        /// </summary>
        public ValueTask<ScavengeResponse> StartScavenge(CancellationToken cancellationToken = default) =>
            client.StartScavenge(1, 0, cancellationToken);

        /// <summary>
        /// Returns the most recent scavenge, or null when none has ever run.
        /// </summary>
        public async ValueTask<ScavengeSummary?> GetLatestScavenge(CancellationToken cancellationToken = default) {
            await foreach (var summary in client.ListScavenges(cancellationToken).ConfigureAwait(false))
                return summary;

            return null;
        }

        /// <summary>
        /// Stops the currently running scavenge.
        /// </summary>
        /// <exception cref="InvalidOperationException">No scavenge is currently running.</exception>
        public async ValueTask<ScavengeResponse> StopCurrentScavenge(CancellationToken cancellationToken = default) {
            var latest = await client.GetLatestScavenge(cancellationToken).ConfigureAwait(false);

            if (latest is not { State: ScavengeState.Running })
                throw new InvalidOperationException("No scavenge is currently running.");

            return await client.StopScavenge(latest.ScavengeId, cancellationToken).ConfigureAwait(false);
        }
    }
}
