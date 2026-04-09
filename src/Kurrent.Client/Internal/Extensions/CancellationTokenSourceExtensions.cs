namespace Kurrent.Client;

public static class CancellationTokenSourceExtensions {
    extension(CancellationTokenSource) {
        public static CancellationTokenSource CreateShortLived(int delay, CancellationToken ct) {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(delay);
            return cts;
        }

        public static CancellationTokenSource CreateShortLived(TimeSpan delay, CancellationToken ct) {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(delay);
            return cts;
        }
    }
}
