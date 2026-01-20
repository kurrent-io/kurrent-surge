using Kurrent.Client;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Processors;
using Kurrent.Client.Streams;
using static System.Threading.CancellationTokenSource;

namespace Kurrent.Processors.Leases;

public class LeaseManager : ILeaseManager  {
    public LeaseManager(StreamsClient streamsClient, IClock? clock = null, StreamTemplate? streamTemplate = null, ILogger<LeaseManager>? logger = null) {
        StreamsClient  = streamsClient;
        StreamTemplate = streamTemplate ?? "$leases/{0}";
        Clock          = clock  ?? SystemClock.Instance;
        Logger         = logger ?? NullLogger<LeaseManager>.Instance;
    }

    StreamsClient  StreamsClient  { get; }
    StreamTemplate StreamTemplate { get; }
    IClock         Clock          { get; }
    ILogger        Logger         { get; }

    public async ValueTask<AcquireOrRenewLeaseResult> AcquireOrRenew(AcquireOrRenewLease request, CancellationToken token = default) {
        var now      = Clock.GetCurrentInstant();
        var streamId = StreamTemplate.GetStream(request.ResourceId);

        var newLease = new VersionedLease {
            ResourceId        = request.ResourceId,
            AcquiredBy        = request.OwnerId,
            AcquiredAt        = now,
            Duration          = request.Duration
        };

        // try to quickly acquire the lease by assuming the stream
        // does not exist when the version is not provided
        if (request.Version.IsUnset) {
            try {
                var acquireResult = await StoreLease(streamId, newLease, token).ConfigureAwait(false);
                Logger.LogLeaseAcquired(newLease.ResourceId, acquireResult.StreamRevision, request.OwnerId, newLease.ExpiresAt);
                return AcquireOrRenewLeaseResult.Acquired(newLease with { Version = LeaseVersion.From(acquireResult.StreamRevision) });
            }
            catch (StreamRevisionConflictException) {
                // Stream exists, fall through to load and evaluate
            }
        }

        // at this point we know the stream exists, so we load the lease and evaluate it
        VersionedLease existingLease;

        try {
            existingLease = await LoadLease(streamId, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            return AcquireOrRenewLeaseResult.OperationCanceled();
        }

        // !!! edge case alert !!!
        // meanwhile the lease could have been deleted.
        // retrying the operation automatically will let the algorithm decide what to do next.
        if (existingLease.IsNone)
            return await AcquireOrRenew(request, token).ConfigureAwait(false);

        if (existingLease.HasNotExpired(now)) {
            if (existingLease.IsNotOwnedBy(request.OwnerId)) {
                Logger.LogAcquireLeaseResultTaken(existingLease.ResourceId, existingLease.Version, request.OwnerId, existingLease.AcquiredBy, existingLease.ExpiresAt);
                return AcquireOrRenewLeaseResult.Taken(existingLease);
            }

            if (existingLease.VersionMismatch(request.Version)) {
                Logger.LogAcquireLeaseResultVersionMismatch(existingLease.ResourceId, request.Version.Value, request.OwnerId, existingLease.Version);
                return AcquireOrRenewLeaseResult.VersionMismatch(existingLease);
            }
        }

        // the lease can now be acquired or renewed
        try {
            var result = await StoreLease(streamId, newLease with { Version = existingLease.Version }, token).ConfigureAwait(false);

            if (existingLease.IsOwnedBy(request.OwnerId)) {
                Logger.LogLeaseRenewed(existingLease.ResourceId, result.StreamRevision, request.OwnerId, newLease.ExpiresAt);
                return AcquireOrRenewLeaseResult.Renewed(newLease with { Version = LeaseVersion.From(result.StreamRevision) });
            }
            else {
                Logger.LogLeaseAcquired(newLease.ResourceId, result.StreamRevision, request.OwnerId, newLease.ExpiresAt);
                return AcquireOrRenewLeaseResult.Acquired(newLease with { Version = LeaseVersion.From(result.StreamRevision) });
            }
        }
        catch (StreamRevisionConflictException) {
            // !!! edge case alert !!!
            // meanwhile the lease could have been updated.
            // retrying the operation automatically will let the algorithm decide what to do next.
            return await AcquireOrRenew(request, token).ConfigureAwait(false);
        }
    }

    public async ValueTask<AcquireOrRenewLeaseResult> AcquireOrRenew(AcquireOrRenewLease request, AcquireOrRenewLeaseRetryOptions retryOptions, CancellationToken token = default) {
        using var cancellator = CreateLinkedTokenSource(token).With(x => x.CancelAfter(retryOptions.Timeout.ToTimeSpan()));

        var lastVersion = request.Version;

        while (!cancellator.IsCancellationRequested) {
            try {
                var result = await AcquireOrRenew(request, cancellator.Token).ConfigureAwait(false);

                lastVersion = result.Lease.Version;

                if (result.Type is AcquireOrRenewLeaseResultType.VersionMismatch)
                    return result;

                if (!result.Failure)
                    return result;

                // retry the operation with a delay that is fuzzed to avoid contention
                // and to prevent a thundering herd problem
                // https://en.wikipedia.org/wiki/Thundering_herd_problem
                var delay = retryOptions.Delay.ToTimeSpan()
                    .GetFuzzyInterval(retryOptions.Fuzziness);

                await Task.Delay(delay, cancellator.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested){
                Logger.LogAcquireLeaseResultOperationCanceled(request.ResourceId, request.Version, request.OwnerId);
                return AcquireOrRenewLeaseResult.OperationCanceled();
            }
            catch (OperationCanceledException) when (cancellator.IsCancellationRequested) {
                Logger.LogAcquireLeaseResultTimeout(request.ResourceId, lastVersion, request.OwnerId);
                return AcquireOrRenewLeaseResult.Timeout();
            }
            catch (Exception ex) {
                Logger.LogError(ex, "{ResourceId} lease acquisition by {OwnerId} failed with an unexpected error", request.ResourceId, request.OwnerId);
                throw;
            }
        }

        Logger.LogAcquireLeaseResultOperationCanceled(request.ResourceId, request.Version, request.OwnerId);
        return AcquireOrRenewLeaseResult.OperationCanceled();
    }

    public async ValueTask<ReleaseLeaseResult> Release(ReleaseLease request, CancellationToken token = default) {
        var now      = Clock.GetCurrentInstant();
        var streamId = StreamTemplate.GetStream(request.ResourceId);

        VersionedLease loadedLease;

        try {
            loadedLease = await LoadLease(streamId, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            return ReleaseLeaseResult.OperationCanceled();
        }

        if (loadedLease.IsNone) {
            Logger.LogReleaseLeaseResultNotFound(request.ResourceId, request.Version, request.OwnerId);
            return ReleaseLeaseResult.NotFound();
        }

        if (loadedLease.IsNotOwnedBy(request.OwnerId)) {
            Logger.LogReleaseLeaseResultTaken(loadedLease.ResourceId, loadedLease.Version, request.OwnerId, loadedLease.AcquiredBy, loadedLease.ExpiresAt);
            return ReleaseLeaseResult.Taken(loadedLease);
        }

        if (loadedLease.VersionMismatch(request.Version)) {
            Logger.LogReleaseLeaseResultVersionMismatch(loadedLease.ResourceId, request.Version, request.OwnerId, loadedLease.Version);
            return ReleaseLeaseResult.VersionMismatch(loadedLease);
        }

        if (loadedLease.HasExpired(now)) {
            Logger.LogReleaseLeaseResultExpired(loadedLease.ResourceId, loadedLease.Version, request.OwnerId);
            return ReleaseLeaseResult.Expired(loadedLease);
        }

        // expire lease
        try {
            await StoreLease(streamId, loadedLease with { Duration = Duration.Zero }, token).ConfigureAwait(false);
            Logger.LogLeaseReleased(loadedLease.ResourceId, loadedLease.Version, request.OwnerId);
            return ReleaseLeaseResult.Released();
        }
        catch (StreamRevisionConflictException conflict) {
            // edge case: meanwhile the lease could have been updated.
            // retrying the operation automatically will let the algorithm decide what to do next.
            return await Release(request with {
                Version = LeaseVersion.From(conflict.ActualRevision)
            }, token).ConfigureAwait(false);
        }
    }

    async Task<VersionedLease> LoadLease(string leaseStreamId, CancellationToken token) {
        try {
            var leaseRecord = await StreamsClient
                .ReadLastStreamRecord(leaseStreamId, skipDecoding: false, token)
                .UnwrapAsync()
                .ConfigureAwait(false);

            return leaseRecord.Value is Lease lease
                ? new VersionedLease {
                    ResourceId = lease.ResourceId,
                    AcquiredBy = lease.AcquiredBy,
                    AcquiredAt = lease.AcquiredAt,
                    Duration   = lease.Duration,
                    Version    = LeaseVersion.From(leaseRecord.StreamRevision)
                }
                : VersionedLease.None;
        }
        catch (StreamNotFoundException) {
            return VersionedLease.None;
        }
        catch (StreamDeletedException) {
            return VersionedLease.None;
        }
        catch (StreamTombstonedException) {
            return VersionedLease.None;
        }
    }

    async Task<AppendStreamSuccess> StoreLease(string leaseStreamId, VersionedLease newLease, CancellationToken token) {
        var lease = new Lease {
            ResourceId = newLease.ResourceId,
            AcquiredBy = newLease.AcquiredBy,
            AcquiredAt = newLease.AcquiredAt,
            Duration   = newLease.Duration
        };

        var expectedState = newLease.Version.IsUnset
            ? ExpectedStreamState.NoStream
            : new ExpectedStreamState(newLease.Version.Value);

        var record = AppendRecord.Create(lease);

        return await StreamsClient
            .Append(leaseStreamId, expectedState, [record], token)
            .UnwrapAsync()
            .ConfigureAwait(false);
    }
}

static partial class LeaseManagerAcquireOrRenewLogMessages {
    [LoggerMessage(
        LogLevel.Debug,
        "{ResourceId} lease v{Version} acquired by {OwnerId} until {ExpiresAt}"
    )]
    internal static partial void LogLeaseAcquired(
        this ILogger logger, string resourceId, long version, string ownerId, Instant expiresAt
    );

    [LoggerMessage(
        LogLevel.Trace,
        "{ResourceId} lease v{Version} renewed by {OwnerId} until {ExpiresAt}"
    )]
    internal static partial void LogLeaseRenewed(
        this ILogger logger, string resourceId, long version, string ownerId, Instant expiresAt
    );

    [LoggerMessage(
        LogLevel.Error,
        "{ResourceId} lease v{Version} acquisition by {OwnerId} failed: taken by {TakenBy} until {ExpiresAt}"
    )]
    internal static partial void LogAcquireLeaseResultTaken(
        this ILogger logger, string resourceId, long version, string ownerId, string takenBy, Instant expiresAt
    );

    [LoggerMessage(
        LogLevel.Error,
        "{ResourceId} lease v{Version} acquisition by {OwnerId} failed: expected v{ActualVersion}"
    )]
    internal static partial void LogAcquireLeaseResultVersionMismatch(
        this ILogger logger, string resourceId, long version, string ownerId, long actualVersion
    );

    [LoggerMessage(
        LogLevel.Error,
        "{ResourceId} lease v{Version} acquisition by {OwnerId} failed: operation timed out"
    )]
    internal static partial void LogAcquireLeaseResultTimeout(
        this ILogger logger, string resourceId, long version, string ownerId
    );

    [LoggerMessage(
        LogLevel.Debug,
        "{ResourceId} lease v{Version} renewal cancelled by {OwnerId}"
    )]
    internal static partial void LogAcquireLeaseResultOperationCanceled(
        this ILogger logger, string resourceId, long version, string ownerId
    );
}

static partial class LeaseManagerReleaseLogMessages {
    [LoggerMessage(
        LogLevel.Debug,
        "{ResourceId} lease v{Version} released by {OwnerId}"
    )]
    internal static partial void LogLeaseReleased(
        this ILogger logger, string resourceId, long version, string ownerId
    );

    [LoggerMessage(
        LogLevel.Error,
        "{ResourceId} lease v{Version} release by {OwnerId} failed: not found"
    )]
    internal static partial void LogReleaseLeaseResultNotFound(
        this ILogger logger, string resourceId, long version, string ownerId
    );

    [LoggerMessage(
        LogLevel.Warning,
        "{ResourceId} lease v{Version} release by {OwnerId} failed: taken by {TakenBy} until {ExpiresAt}"
    )]
    internal static partial void LogReleaseLeaseResultTaken(
        this ILogger logger, string resourceId, long version, string ownerId, string takenBy, Instant expiresAt
    );

    [LoggerMessage(
        LogLevel.Error,
        "{ResourceId} lease v{Version} release by {OwnerId} failed: expected v{ActualVersion}"
    )]
    internal static partial void LogReleaseLeaseResultVersionMismatch(
        this ILogger logger, string resourceId, long version, string ownerId, long actualVersion
    );

    [LoggerMessage(
        LogLevel.Warning,
        "{ResourceId} lease v{Version} release by {OwnerId} failed: expired"
    )]
    internal static partial void LogReleaseLeaseResultExpired(
        this ILogger logger, string resourceId, long version, string ownerId
    );

    [LoggerMessage(
        LogLevel.Warning,
        "{ResourceId} lease v{Version} release by {OwnerId} failed: operation was canceled"
    )]
    internal static partial void LogReleaseLeaseResultOperationCanceled(
        this ILogger logger, string resourceId, long version, string ownerId
    );
}
