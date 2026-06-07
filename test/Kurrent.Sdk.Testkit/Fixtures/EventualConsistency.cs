using Kurrent.Client.Exceptions;
using TUnit.Assertions.Exceptions;

namespace Kurrent.Sdk.Testkit;

public static class EventualConsistency {
    static readonly TimeSpan DefaultTimeout  = TimeSpan.FromSeconds(3);
    static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(50);

    public static ValueTask Eventually(
        Func<ValueTask> assertion,
        TimeSpan? timeout = null,
        TimeSpan? interval = null,
        Func<Exception, bool>? isTransient = null,
        CancellationToken ct = default
    ) =>
        Poll(assertion, timeout, interval, isTransient, ct);

    public static async ValueTask<T> Eventually<T>(
        Func<ValueTask<T>> action,
        TimeSpan? timeout = null,
        TimeSpan? interval = null,
        Func<Exception, bool>? isTransient = null,
        CancellationToken ct = default
    ) {
        var result = default(T)!;
        await Poll(async () => result = await action(), timeout, interval, isTransient, ct);
        return result;
    }

    static async ValueTask Poll(
        Func<ValueTask> assertion,
        TimeSpan? timeout,
        TimeSpan? interval,
        Func<Exception, bool>? isTransient,
        CancellationToken ct
    ) {
        var retryUntil = DateTimeOffset.UtcNow + (timeout ?? DefaultTimeout);
        var poll       = interval    ?? DefaultInterval;
        var transient  = isTransient ?? IsTransient;

        while (true) {
            ct.ThrowIfCancellationRequested();

            try {
                await assertion();
                ct.ThrowIfCancellationRequested();
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
