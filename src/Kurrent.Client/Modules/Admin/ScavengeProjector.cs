#pragma warning disable CS8524

using System.Collections.Concurrent;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Admin;

/// <summary>
/// Internal projector that reads scavenge event streams, projects <see cref="ScavengeDetails"/>,
/// and caches completed scavenges. Tracks stream positions so subsequent calls only read deltas.
/// </summary>
sealed class ScavengeProjector {
    const string ScavengesStream = "$scavenges";

    static string ScavengeStream(string scavengeId) => $"{ScavengesStream}-{scavengeId}";

    readonly StreamsClient _streams;

    // Index state — tracks position in $scavenges so we only read new events
    readonly List<string> _knownIds = [];
    readonly HashSet<string> _startedIds = [];
    readonly HashSet<string> _completedIds = [];
    Revision _indexPosition = Revision.Unset;

    // Cache — completed scavenges are immutable, never re-read
    readonly ConcurrentDictionary<string, CachedScavenge> _cache = new();

    internal ScavengeProjector(StreamsClient streams) => _streams = streams;

    #region Public API

    /// <summary>
    /// Refreshes the index, projects the last <paramref name="limit"/> scavenges,
    /// and yields summaries as each projection completes.
    /// </summary>
    public async IAsyncEnumerable<ScavengeSummary> ListScavenges(
        int limit,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct
    ) {
        await RefreshIndex(ct).ConfigureAwait(false);

        var yielded = 0;

        // Walk backwards through known IDs to yield most recent first
        for (var i = _knownIds.Count - 1; i >= 0 && yielded < limit; i--) {
            var id = _knownIds[i];

            // Only yield scavenges that have at least started
            if (!_startedIds.Contains(id))
                continue;

            var details = await Project(id, ct).ConfigureAwait(false);
            yield return details.ToSummary();
            yielded++;
        }
    }

    /// <summary>
    /// Projects a single scavenge by ID. Uses cache for completed scavenges,
    /// incremental reads for running ones.
    /// </summary>
    public async ValueTask<ScavengeDetails> Project(string scavengeId, CancellationToken ct) {
        if (_cache.TryGetValue(scavengeId, out var cached) && cached.Details.State != ScavengeState.Running)
            return cached.Details;

        return await ProjectFromStream(scavengeId, cached, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the most recently completed scavenge, or null if none.
    /// </summary>
    public async ValueTask<ScavengeDetails?> GetLastCompleted(CancellationToken ct) {
        await RefreshIndex(ct).ConfigureAwait(false);

        for (var i = _knownIds.Count - 1; i >= 0; i--) {
            var id = _knownIds[i];

            if (!_completedIds.Contains(id))
                continue;

            return await Project(id, ct).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>
    /// Returns the currently running scavenge, or null if idle.
    /// </summary>
    public async ValueTask<ScavengeDetails?> GetCurrent(CancellationToken ct) {
        await RefreshIndex(ct).ConfigureAwait(false);

        for (var i = _knownIds.Count - 1; i >= 0; i--) {
            var id = _knownIds[i];

            if (!_startedIds.Contains(id) || _completedIds.Contains(id))
                continue;

            // The index may lag behind the per-scavenge stream —
            // project to get the true state from the detail stream.
            var details = await Project(id, ct).ConfigureAwait(false);

            if (details.State == ScavengeState.Running)
                return details;

            // The scavenge completed but the index hasn't caught up yet — update our tracking.
            _completedIds.Add(id);
        }

        return null;
    }

    #endregion

    #region Internals

    /// <summary>
    /// Reads $scavenges from the last known position forward, updating the index.
    /// </summary>
    async ValueTask RefreshIndex(CancellationToken ct) {
        try {
            var query = ReadStream.From(ScavengesStream)
                .WithCancellationToken(ct);

            if (_indexPosition != Revision.Unset)
                query = query.FromRevision(_indexPosition + 1);

            await using var messages = await _streams
                .ReadRecords(query.Build())
                .ConfigureAwait(false);

            await foreach (var msg in messages.WithCancellation(ct)) {
                if (!msg.IsRecord) continue;

                var record = msg.AsRecord;

                switch (record.Value) {
                    case ScavengeStarted started:
                        if (_startedIds.Add(started.ScavengeId))
                            _knownIds.Add(started.ScavengeId);
                        break;

                    case ScavengeCompleted completed:
                        _completedIds.Add(completed.ScavengeId);

                        if (_startedIds.Add(completed.ScavengeId))
                            _knownIds.Add(completed.ScavengeId);
                        break;
                }

                // Track the link revision (the position in $scavenges itself)
                if (record.Link is { IsUnset: false })
                    _indexPosition = record.Link.Revision;
                else if (record.Revision != Revision.Unset)
                    _indexPosition = record.Revision;
            }
        }
        catch (StreamNotFoundException) {
            // $scavenges doesn't exist yet — no scavenges have ever run
        }
    }

    /// <summary>
    /// Reads $scavenges-{id} incrementally and folds events into ScavengeDetails.
    /// </summary>
    async ValueTask<ScavengeDetails> ProjectFromStream(string scavengeId, CachedScavenge? existing, CancellationToken ct) {
        var details  = existing?.Details ?? new ScavengeDetails { ScavengeId = scavengeId, State = ScavengeState.Running };
        var progress = existing?.Progress ?? new ScavengeProgress();

        var query = ReadStream.From(ScavengeStream(scavengeId))
            .WithCancellationToken(ct);

        // Incremental: start from where we left off
        if (existing is { LastRevision: { IsUnset: false } lastRev })
            query = query.FromRevision(lastRev + 1);

        Revision lastRevision = existing?.LastRevision ?? Revision.Unset;

        try {
            await using var messages = await _streams
                .ReadRecords(query.Build())
                .ConfigureAwait(false);

            await foreach (var msg in messages.WithCancellation(ct)) {
                if (!msg.IsRecord) continue;

                var record = msg.AsRecord;
                lastRevision = record.Revision;

                switch (record.Value) {
                    case ScavengeStarted started:
                        details = details with {
                            NodeEndpoint = started.NodeEndpoint,
                            StartedAt    = record.Timestamp,
                            Configuration = new ScavengeConfiguration {
                                Threads        = started.Threads ?? 1,
                                StartFromChunk = started.StartFromChunk ?? 0,
                                MergeChunks    = started.MergeChunks ?? true
                            }
                        };
                        break;

                    case ScavengeCompleted completed:
                        details = details with {
                            State       = completed.Result,
                            CompletedAt = record.Timestamp,
                            Duration    = completed.TimeTaken,
                            Error       = completed.Error
                        };
                        break;

                    case ScavengeChunksCompleted chunks:
                        progress = chunks.WasScavenged
                            ? progress with {
                                ChunksScavenged = progress.ChunksScavenged + 1,
                                SpaceSaved      = progress.SpaceSaved + chunks.SpaceSaved
                            }
                            : progress with {
                                ChunksSkipped = progress.ChunksSkipped + 1
                            };
                        break;

                    case ScavengeMergeCompleted merge:
                        if (merge.WasMerged)
                            progress = progress with {
                                ChunksMerged = progress.ChunksMerged + 1,
                                SpaceSaved   = progress.SpaceSaved + merge.SpaceSaved
                            };
                        break;

                    case ScavengeIndexCompleted index:
                        if (index.WasScavenged)
                            progress = progress with {
                                IndexTablesScavenged = progress.IndexTablesScavenged + 1,
                                IndexEntriesDeleted  = progress.IndexEntriesDeleted + index.EntriesDeleted,
                                SpaceSaved           = progress.SpaceSaved + index.SpaceSaved
                            };
                        break;
                }
            }
        }
        catch (StreamNotFoundException) {
            // Per-scavenge stream doesn't exist — scavenge was just started
        }

        if (details.State == ScavengeState.Running && details.StartedAt != default)
            details = details with { Duration = DateTime.UtcNow - details.StartedAt };

        details = details with { Progress = progress };

        _cache[scavengeId] = new CachedScavenge(details, progress, lastRevision);

        return details;
    }

    #endregion

    sealed record CachedScavenge(ScavengeDetails Details, ScavengeProgress Progress, Revision LastRevision);
}

static class ScavengeDetailsExtensions {
    public static ScavengeSummary ToSummary(this ScavengeDetails details) =>
        new() {
            ScavengeId   = details.ScavengeId,
            NodeEndpoint = details.NodeEndpoint,
            State        = details.State,
            StartedAt    = details.StartedAt,
            CompletedAt  = details.CompletedAt,
            Duration     = details.Duration,
            SpaceSaved   = details.Progress.SpaceSaved
        };
}
