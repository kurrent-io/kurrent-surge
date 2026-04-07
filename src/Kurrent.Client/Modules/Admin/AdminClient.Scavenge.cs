#pragma warning disable CS8524

using Kurrent.Client.Exceptions;
using KurrentDB.Protocol.V1.Operations;

namespace Kurrent.Client.Admin;

public partial class AdminClient {
    public async ValueTask<ScavengeResult> StartScavenge(
        int threadCount = 1,
        int startFromChunk = 0,
        CancellationToken cancellationToken = default
    ) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(threadCount);
        ArgumentOutOfRangeException.ThrowIfNegative(startFromChunk);

        try {
            var request = new StartScavengeReq {
                Options = new StartScavengeReq.Types.Options {
                    ThreadCount    = threadCount,
                    StartFromChunk = startFromChunk
                }
            };

            var response = await AdminServiceClient
                .StartScavengeAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return response.ScavengeResult switch {
                ScavengeResp.Types.ScavengeResult.Started    => ScavengeResult.Started(response.ScavengeId),
                ScavengeResp.Types.ScavengeResult.Stopped    => ScavengeResult.Stopped(response.ScavengeId),
                ScavengeResp.Types.ScavengeResult.InProgress => ScavengeResult.InProgress(response.ScavengeId)
            };
        }
        catch (Exception ex) {
            throw ex.ToException();
        }
    }

    public async ValueTask<ScavengeResult> StopScavenge(string scavengeId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(scavengeId);

        try {
            var request = new StopScavengeReq {
                Options = new StopScavengeReq.Types.Options {
                    ScavengeId = scavengeId
                }
            };

            var response = await AdminServiceClient
                .StopScavengeAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return response.ScavengeResult switch {
                ScavengeResp.Types.ScavengeResult.Started    => ScavengeResult.Started(response.ScavengeId),
                ScavengeResp.Types.ScavengeResult.Stopped    => ScavengeResult.Stopped(response.ScavengeId),
                ScavengeResp.Types.ScavengeResult.InProgress => ScavengeResult.InProgress(response.ScavengeId)
            };
        }
        catch (Exception ex) {
            throw ex.ToException();
        }
    }
}
