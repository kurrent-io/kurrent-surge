#pragma warning disable CS8524

using System.Runtime.CompilerServices;
using Grpc.Core;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;
using KurrentDB.Protocol.V1.Operations;

namespace Kurrent.Client.Admin;

public partial class AdminClient {
    const string ScavengesStream = "$scavenges";

    static string ScavengeStream(string id) => $"{ScavengesStream}-{id}";

    readonly Dictionary<string, ScavengeSummary> _scavengeCache = new();

    #region Commands

    public async ValueTask<ScavengeResponse> StartScavenge(
        int threadCount = 1,
        int startFromChunk = 0,
        CancellationToken cancellationToken = default
    ) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(threadCount);
        ArgumentOutOfRangeException.ThrowIfNegative(startFromChunk);

        try {
            var response = await AdminServiceClient
                .StartScavengeAsync(
                    new StartScavengeReq {
                        Options = new() {
                            ThreadCount    = threadCount,
                            StartFromChunk = startFromChunk
                        }
                    },
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);

            return MapScavengeResponse(response);
        }
        catch (Exception ex) {
            throw ex.AsWellKnownException();
        }
    }

    public ValueTask<ScavengeResponse> StartScavenge(CancellationToken cancellationToken = default) =>
        StartScavenge(1, 0, cancellationToken);

    public async ValueTask<ScavengeResponse> StopScavenge(string scavengeId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(scavengeId);

        try {
            var response = await AdminServiceClient
                .StopScavengeAsync(
                    new StopScavengeReq {
                        Options = new() { ScavengeId = scavengeId }
                    },
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);

            return MapScavengeResponse(response);
        }
        catch (Exception ex) {
            if (ex is RpcException { StatusCode: StatusCode.NotFound }) {
                // The server returns NotFound for both "already completed" and "never existed".
                // Check if the scavenge existed by reading its detail stream directly.
                try {
                    var details = await GetScavengeDetails(scavengeId, cancellationToken).ConfigureAwait(false);
                    if (details.State != ScavengeState.Running)
                        return new ScavengeResponse(scavengeId, ScavengeResponseStatus.StopRequested);
                }
                catch {
                    // Detail stream doesn't exist either — truly not found
                }
            }

            throw ex.AsWellKnownException();
        }
    }

    public async ValueTask<ScavengeResponse> StopCurrentScavenge(CancellationToken cancellationToken = default) {
        var latest = await GetLatestScavenge(cancellationToken).ConfigureAwait(false);

        if (latest is not { State: ScavengeState.Running })
            throw new InvalidOperationException("No scavenge is currently running.");

        return await StopScavenge(latest.ScavengeId, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Queries

    public async ValueTask<ScavengeSummary?> GetLatestScavenge(CancellationToken cancellationToken = default) {
        await foreach (var summary in ListScavenges(cancellationToken).ConfigureAwait(false))
            return summary;

        return null;
    }

    public async IAsyncEnumerable<ScavengeSummary> ListScavenges(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    ) {
        var pending = new Dictionary<string, ScavengeSummary>();

        var messages = Client.Streams.ReadStream(
            ReadStreamRequest.New
                .ForStream(ScavengesStream)
                .FromEnd()
                .Build(),
            cancellationToken
        );

        var enumerator = messages.GetAsyncEnumerator(cancellationToken);

        try {
            bool hasNext;

            try {
                hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
            }
            catch (StreamNotFoundException) {
                yield break;
            }

            while (hasNext) {
                var msg = enumerator.Current;

                if (msg.IsRecord) {
                    var record = msg.AsRecord;

                    switch (record.Value) {
                        case ScavengeCompleted completed:
                            pending[completed.ScavengeId] = FoldCompleted(completed, record, pending);
                            break;

                        case ScavengeStarted started:
                            if (_scavengeCache.TryGetValue(started.ScavengeId, out var cached)) {
                                yield return cached;
                            }
                            else {
                                var summary = FoldStarted(started, record, pending);
                                pending.Remove(started.ScavengeId);

                                if (summary.State != ScavengeState.Running)
                                    _scavengeCache[summary.ScavengeId] = summary;

                                yield return summary;
                            }

                            break;
                    }
                }

                hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
            }
        }
        finally {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask<ScavengeDetails> GetScavengeDetails(string scavengeId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(scavengeId);

        var details  = new ScavengeDetails { ScavengeId = scavengeId, State = ScavengeState.Running };
        var progress = new ScavengeProgress();

        var messages = Client.Streams.ReadStream(
            ReadStreamRequest.New
                .ForStream(ScavengeStream(scavengeId))
                .Build(),
            cancellationToken
        );

        await foreach (var msg in messages.WithCancellation(cancellationToken).ConfigureAwait(false)) {
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

    #region Internals

    static ScavengeResponse MapScavengeResponse(ScavengeResp response) =>
        new(
            response.ScavengeId,
            response.ScavengeResult switch {
                ScavengeResp.Types.ScavengeResult.Started    => ScavengeResponseStatus.Started,
                ScavengeResp.Types.ScavengeResult.InProgress => ScavengeResponseStatus.AlreadyRunning,
                ScavengeResp.Types.ScavengeResult.Stopped    => ScavengeResponseStatus.StopRequested
            }
        );

    static ScavengeSummary FoldStarted(ScavengeStarted started, Record record, Dictionary<string, ScavengeSummary> pending) {
        var existing = pending.GetValueOrDefault(started.ScavengeId)
            ?? new ScavengeSummary { ScavengeId = started.ScavengeId, State = ScavengeState.Running };

        return existing with {
            NodeEndpoint = started.NodeEndpoint,
            StartedAt    = record.Timestamp
        };
    }

    static ScavengeSummary FoldCompleted(ScavengeCompleted completed, Record record, Dictionary<string, ScavengeSummary> pending) {
        var existing = pending.GetValueOrDefault(completed.ScavengeId)
            ?? new ScavengeSummary { ScavengeId = completed.ScavengeId };

        return existing with {
            State       = completed.Result,
            CompletedAt = record.Timestamp,
            Duration    = completed.TimeTaken,
            SpaceSaved  = completed.SpaceSaved
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
