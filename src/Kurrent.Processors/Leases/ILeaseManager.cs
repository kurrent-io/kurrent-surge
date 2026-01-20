namespace Kurrent.Processors.Leases;

/// <summary>
/// The ILeaseManager interface defines the contract for managing leases in a distributed system.
/// A lease is a mechanism to ensure that a resource is not concurrently accessed by multiple entities in the system.
/// </summary>
public interface ILeaseManager {
    /// <summary>
    /// Acquires or renews a lease for a given resource.
    /// If the lease for the resource is currently not held by any entity or if it's held by the requesting entity, the lease is granted or renewed.
    /// </summary>
    /// <param name="request">The request to acquire or renew a lease. It includes the resource identifier, the owner identifier, and the desired lease duration.</param>
    /// <param name="token">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The result of the lease acquisition or renewal.</returns>
    ValueTask<AcquireOrRenewLeaseResult> AcquireOrRenew(AcquireOrRenewLease request, CancellationToken token);

    /// <summary>
    /// Acquires or renews a lease for a given resource with retry options.
    /// If the lease for the resource is currently not held by any entity or if it's held by the requesting entity, the lease is granted or renewed.
    /// </summary>
    /// <param name="request">The request to acquire or renew a lease. It includes the resource identifier, the client identifier, and the desired lease duration.</param>
    /// <param name="retryOptions">The options to use when retrying the acquire or renew operation in case of failure.</param>
    /// <param name="token">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The result of the lease acquisition or renewal.</returns>
    ValueTask<AcquireOrRenewLeaseResult> AcquireOrRenew(AcquireOrRenewLease request, AcquireOrRenewLeaseRetryOptions retryOptions, CancellationToken token);

    /// <summary>
    /// Releases a lease for a given resource.
    /// The lease can only be released if it's currently held by the requesting entity.
    /// </summary>
    /// <param name="request">The request to release a lease. It includes the resource identifier, the owner identifier, and the current lease version.</param>
    /// <param name="token">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The result of the lease release.</returns>
    ValueTask<ReleaseLeaseResult> Release(ReleaseLease request, CancellationToken token);

    /// <summary>
    /// Releases a lease for a given resource.
    /// The lease can only be released if it's currently held by the requesting entity.
    /// </summary>
    /// <param name="request">The request to release a lease. It includes the resource identifier, the owner identifier, and the current lease version.</param>
    /// <returns>The result of the lease release.</returns>
    async ValueTask<ReleaseLeaseResult> Release(ReleaseLease request, TimeSpan timeout) {
        CancellationTokenSource? cancellator = null;
        try {
            cancellator = new CancellationTokenSource(timeout);
            return await Release(request, cancellator.Token).ConfigureAwait(false);
        }
        finally {
            cancellator?.Dispose();
        }
    }
}
