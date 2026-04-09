using System.Runtime.CompilerServices;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Admin;

/// <summary>
/// Reads the <c>$scavenges</c> index stream, folds events into cached <see cref="ScavengeDetails"/>,
/// and tracks position so subsequent calls only read new events.
/// </summary>
sealed class ScavengeProjector {
    const string ScavengesStream = "$scavenges";

    static string ScavengeStream(string scavengeId) => $"{ScavengesStream}-{scavengeId}";

    readonly StreamsClient _streams;
    readonly Dictionary<string, ScavengeDetails> _cache = new();
    readonly List<string> _order = []; // insertion order for reverse iteration

    Revision _checkpoint = Revision.Unset;

    internal ScavengeProjector(StreamsClient streams) => _streams = streams;

    #region Cached operations (from $scavenges index only)

    /// <summary>
    /// Reads new events from <c>$scavenges</c>, updates the cache, and yields
    /// the last <paramref name="limit"/> scavenges as summaries (most recent first).
    /// </summary>
    public async IAsyncEnumerable<ScavengeSummary> ListScavenges(
        int limit,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        await ReadIndex(ct).ConfigureAwait(false);

        var yielded = 0;

        for (var i = _order.Count - 1; i >= 0 && yielded < limit; i--) {
            if (_cache.TryGetValue(_order[i], out var details)) {
                yield return details.ToSummary();
                yielded++;
            }
        }
    }

    /// <summary>
    /// Returns the most recently completed scavenge, or null if none.
    /// </summary>
    public async ValueTask<ScavengeDetails?> GetLastCompleted(CancellationToken ct) {
        await foreach (var summary in ListScavenges(int.MaxValue, ct)) {
            if (summary.State != ScavengeState.Running)
                return _cache.GetValueOrDefault(summary.ScavengeId);
        }

        return null;
    }

    /// <summary>
    /// Returns the currently running scavenge, or null if idle.
    /// </summary>
    public async ValueTask<ScavengeDetails?> GetCurrent(CancellationToken ct) {
        await foreach (var summary in ListScavenges(1, ct)) {
            if (summary.State == ScavengeState.Running)
                return _cache.GetValueOrDefault(summary.ScavengeId);
        }

        return null;
    }

    /// <summary>
    /// Returns cached details for a scavenge ID, or null if unknown.
    /// </summary>
    public ScavengeDetails? GetCached(string scavengeId) =>
        _cache.GetValueOrDefault(scavengeId);

    #endregion

    #region Uncached operations (from per-scavenge stream)

    /// <summary>
    /// Reads <c>$scavenges-{id}</c> and folds ALL events into a full <see cref="ScavengeDetails"/>
    /// with complete <see cref="ScavengeProgress"/>. No caching.
    /// </summary>
    public async ValueTask<ScavengeDetails> ProjectScavenge(string scavengeId, CancellationToken ct) {
        var details  = new ScavengeDetails { ScavengeId = scavengeId, State = ScavengeState.Running };
        var progress = new ScavengeProgress();

        await using var messages = await _streams
            .ReadRecords(
                ReadStream.From(ScavengeStream(scavengeId))
                    .WithCancellationToken(ct)
                    .Build()
            )
            .ConfigureAwait(false);

        await foreach (var msg in messages.WithCancellation(ct)) {
            if (!msg.IsRecord) continue;

            var record = msg.AsRecord;

            switch (record.Value) {
                case ScavengeStarted started:
                    details = details with {
                        NodeEndpoint  = started.NodeEndpoint,
                        StartedAt     = record.Timestamp,
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
                        : progress with { ChunksSkipped = progress.ChunksSkipped + 1 };
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

        if (details.State == ScavengeState.Running && details.StartedAt != default)
            details = details with { Duration = DateTime.UtcNow - details.StartedAt };

        return details with { Progress = progress };
    }

    #endregion

    #region Index reader

    async ValueTask ReadIndex(CancellationToken ct) {
        try {
            var query = ReadStream.From(ScavengesStream)
                .WithCancellationToken(ct);

            if (_checkpoint != Revision.Unset)
                query = query.FromRevision(_checkpoint + 1);

            await using var messages = await _streams
                .ReadRecords(query.Build())
                .ConfigureAwait(false);

            await foreach (var msg in messages.WithCancellation(ct)) {
                if (!msg.IsRecord) continue;

                var record = msg.AsRecord;

                var details = record.Value switch {
                    ScavengeStarted started => FoldStarted(started, record),
                    ScavengeCompleted completed => FoldCompleted(completed, record),
                    _ => null
                };

                if (details is not null) {
                    if (!_cache.ContainsKey(details.ScavengeId))
                        _order.Add(details.ScavengeId);

                    _cache[details.ScavengeId] = details;
                }

                // Track position in the index stream (link revision, not resolved event revision)
                if (record.Link is { IsUnset: false })
                    _checkpoint = record.Link.Revision;
                else if (record.Revision != Revision.Unset)
                    _checkpoint = record.Revision;
            }
        }
        catch (StreamNotFoundException) {
            // $scavenges doesn't exist yet — no scavenges have ever run
        }
    }

    ScavengeDetails FoldStarted(ScavengeStarted started, Record record) {
        var existing = _cache.GetValueOrDefault(started.ScavengeId)
            ?? new ScavengeDetails { ScavengeId = started.ScavengeId, State = ScavengeState.Running };

        return existing with {
            NodeEndpoint  = started.NodeEndpoint,
            StartedAt     = record.Timestamp,
            Configuration = new ScavengeConfiguration {
                Threads        = started.Threads ?? 1,
                StartFromChunk = started.StartFromChunk ?? 0,
                MergeChunks    = started.MergeChunks ?? true
            }
        };
    }

    ScavengeDetails FoldCompleted(ScavengeCompleted completed, Record record) {
        var existing = _cache.GetValueOrDefault(completed.ScavengeId)
            ?? new ScavengeDetails { ScavengeId = completed.ScavengeId };

        return existing with {
            State       = completed.Result,
            CompletedAt = record.Timestamp,
            Duration    = completed.TimeTaken,
            Error       = completed.Error,
            Progress    = existing.Progress with { SpaceSaved = completed.SpaceSaved }
        };
    }

    #endregion
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
