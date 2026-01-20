using DotNext.Threading;
using Kurrent.Processors.Leases;
using Exception = System.Exception;
using Timeout = System.Threading.Timeout;

namespace Kurrent.Processors.Locks;

[PublicAPI]
public class ServiceLocker : IAsyncDisposable {
    volatile TokenCompletionSource?   TokenCompletionSource = new();
    volatile CancellationTokenSource? LockerLifetime;
    volatile CancellationTokenSource? BackgroundCancellator;

    public ServiceLocker(ServiceLockerOptions options, ILeaseManager leaseManager) {
        Options      = options;
        LeaseManager = leaseManager;

        LockerLifetime = new();
        LifetimeToken  = LockerLifetime.Token;
        BackgroundTask = Task.CompletedTask;
    }

    ServiceLockerOptions Options       { get; }
    ILeaseManager        LeaseManager  { get; }
    CancellationToken    LifetimeToken { get; }

    VersionedLease Lease          { get; set; } = VersionedLease.None;
    Task           BackgroundTask { get; set; }

    public async Task<CancellationToken> EnsureLocked(Func<VersionedLease, CancellationToken, Task> onLease, CancellationToken stoppingToken) {
        var request = new AcquireOrRenewLease {
            ResourceId = Options.ResourceId,
            OwnerId    = Options.OwnerId,
            Duration   = Options.LeaseDuration
        };

        var token       = LifetimeToken;
        var cancellator = BackgroundCancellator = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, token);

        BackgroundTask = Task.Run(async () => {
            while (!cancellator.IsCancellationRequested) {
                var result = await LeaseManager.AcquireOrRenew(
                    request with { Version = Lease.Version },
                    Options.Retry,
                    cancellator.Token
                ).ConfigureAwait(false);

                if (result.Failure) {
                    SignalLeaseLost(
                        TokenCompletionSource is { Task.IsCompleted: false }
                            ? new TimeoutException("Failed to lock service within the specified timeout.")
                            : new ApplicationException($"Failed to lock service: {result.Type}")
                    );

                    return;
                }

                // update the lease and execute the callback
                try {
                    await onLease(Lease = result.Lease, cancellator.Token).ConfigureAwait(false);
                }
                catch {
                    // ignore user exceptions and continue
                }

                // signal completion if the lease was acquired
                if (TokenCompletionSource is { Task.IsCompleted: false })
                    SignalLeaseAcquired();

                // wait for the next renewal interval and gracefully exit if the token is cancelled
                // fuzziness is applied to the delay to avoid thundering herd problem
                // https://en.wikipedia.org/wiki/Thundering_herd_problem
                var delay = result.Lease.Duration.ToTimeSpan()
                    .GetLowerFuzzyInterval(Options.Retry.Fuzziness);

                await Task.WhenAny(Task.Delay(delay, cancellator.Token)).ConfigureAwait(false);
            }

            SignalLeaseLost();
        }, cancellator.Token);

        // wait for the lease to be acquired
        // or throw an exception if the token is cancelled
        return await (
            TokenCompletionSource?.Task.WaitAsync(Timeout.InfiniteTimeSpan, stoppingToken) ??
            Task.FromException<CancellationToken>(new ObjectDisposedException(GetType().Name))
        ).ConfigureAwait(false);
    }

    public Task<CancellationToken> EnsureLocked(CancellationToken stoppingToken) =>
        EnsureLocked((_, _) => Task.CompletedTask, stoppingToken);

    public async ValueTask DisposeAsync() {
        // cancel the lifetime token
        if (Interlocked.Exchange(ref LockerLifetime, null) is { } lifetime) {
            lifetime.Cancel(throwOnFirstException: false);
            lifetime.Dispose();
        }

        // wait for the background task to complete
        await BackgroundTask.ConfigureAwait(false);

        // dispose the background cancellator
        if (Interlocked.Exchange(ref BackgroundCancellator, null) is { } cancellator) {
            cancellator.Dispose();
        }

        // release the lease
        if (Lease.IsNone)
            return;

        await LeaseManager.Release(
            new ReleaseLease {
                ResourceId = Options.ResourceId,
                OwnerId    = Options.OwnerId,
                Version    = Lease.Version
            },
            Options.ReleaseTimeout.ToTimeSpan()
        ).ConfigureAwait(false);

        // signal that the lease has been lost (even if the release fails)
        if (TokenCompletionSource is { Task.IsCompleted: true })
            SignalLeaseLost();
    }

    void SignalLeaseAcquired() =>
        TokenCompletionSource?.Complete();

    void SignalLeaseLost(Exception? exception = null) {
        using var old = Interlocked.Exchange(ref TokenCompletionSource, null);
        old?.Cancel(exception);
    }
}
