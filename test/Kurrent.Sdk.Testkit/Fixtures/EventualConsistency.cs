using Kurrent.Client.Exceptions;
using TUnit.Assertions.Exceptions;

namespace Kurrent.Sdk.Testkit;

public static class EventualConsistency {
    static readonly TimeSpan DefaultTimeout  = TimeSpan.FromSeconds(3);
    static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(50);

    public static ValueTask Eventually(
        Func<ValueTask> assertion,
        CancellationToken ct = default,
        TimeSpan? timeout = null,
        TimeSpan? interval = null,
        Func<Exception, bool>? isTransient = null
    ) =>
        Poll(assertion, ct, timeout, interval, isTransient);

    public static async ValueTask<T> Eventually<T>(
        Func<ValueTask<T>> action,
        CancellationToken ct = default,
        TimeSpan? timeout = null,
        TimeSpan? interval = null,
        Func<Exception, bool>? isTransient = null
    ) {
        var result = default(T)!;
        await Poll(async () => result = await action(), ct, timeout, interval, isTransient);
        return result;
    }

    static async ValueTask Poll(
        Func<ValueTask> assertion,
        CancellationToken ct,
        TimeSpan? timeout,
        TimeSpan? interval,
        Func<Exception, bool>? isTransient
    ) {
        var retryUntil = DateTimeOffset.UtcNow + (timeout ?? DefaultTimeout);
        var poll       = interval    ?? DefaultInterval;
        var transient  = isTransient ?? IsTransient;

        while (true) {
            try {
                await assertion();
                return;
            } catch (Exception ex) when (transient(ex) && DateTimeOffset.UtcNow < retryUntil) {
                await Task.Delay(poll, ct);
            }
        }
    }

    static bool IsTransient(Exception ex) =>
        ex is
            BaseAssertionException or
            SchemaNotFoundException or
            ConnectorNotFoundException or
            Google.Rpc.Exceptions.NotFoundException;
}
