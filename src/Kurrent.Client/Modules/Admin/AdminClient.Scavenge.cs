#pragma warning disable CS8524

using System.Runtime.CompilerServices;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;
using KurrentDB.Protocol.V1.Operations;

namespace Kurrent.Client.Admin;

public partial class AdminClient {
    const int DefaultScavengeLimit = 3;

    ScavengeProjector? _scavengeProjector;

    ScavengeProjector Projector => _scavengeProjector ??= new ScavengeProjector(Source.Streams);

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
            throw ex.AsWellKnownException();
        }
    }

    public async ValueTask<ScavengeResponse> StopCurrentScavenge(CancellationToken cancellationToken = default) {
        var current = await GetCurrentScavenge(cancellationToken).ConfigureAwait(false);

        if (current is null)
            throw new InvalidOperationException("No scavenge is currently running.");

        return await StopScavenge(current.ScavengeId, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Queries

    public ValueTask<ScavengeDetails?> GetCurrentScavenge(CancellationToken cancellationToken = default) =>
        Projector.GetCurrent(cancellationToken);

    public ValueTask<ScavengeDetails?> GetLastScavenge(CancellationToken cancellationToken = default) =>
        Projector.GetLastCompleted(cancellationToken);

    public IAsyncEnumerable<ScavengeSummary> ListScavenges(int limit = DefaultScavengeLimit, CancellationToken cancellationToken = default) =>
        Projector.ListScavenges(limit, cancellationToken);

    public ValueTask<ScavengeDetails> GetScavengeDetails(string scavengeId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(scavengeId);
        return Projector.Project(scavengeId, cancellationToken);
    }

    /// <summary>
    /// Low-level read of the <c>$scavenges</c> index stream. Returns raw records
    /// without projection or caching.
    /// </summary>
    public ValueTask<RecordStream> ReadScavengeIndex(CancellationToken cancellationToken = default) =>
        Source.Streams.ReadRecords(
            ReadStream.From("$scavenges")
                .WithCancellationToken(cancellationToken)
                .Build()
        );

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

    #endregion
}
