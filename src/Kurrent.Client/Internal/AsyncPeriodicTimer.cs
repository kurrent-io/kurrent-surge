namespace Kurrent;

public delegate Task OnPeriodicTimerTick(CancellationToken cancellationToken);

sealed class AsyncPeriodicTimer : IAsyncDisposable {
    readonly Func<CancellationToken, Task> _getTikTok;

    CancellationTokenSource? _cancellator;
    Task?                    _completion;

    public AsyncPeriodicTimer(TimeSpan period, OnPeriodicTimerTick onTick, TimeProvider? timeProvider = null) {
        _getTikTok = TikTok;

        async Task TikTok(CancellationToken stoppingToken = default) {
            using var timer = new PeriodicTimer(period, timeProvider ?? TimeProvider.System);
            try {
                while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                    await onTick(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                // expected on cancellation
            }
        }
    }
    
    public AsyncPeriodicTimer(TimeSpan period, TimeProvider timeProvider, OnPeriodicTimerTick onTick) : this(period, onTick, timeProvider) { }
    
    public void Start(CancellationToken stoppingToken = default) {
        if (_completion is not null)
            throw new InvalidOperationException("Timer has already been started.");

        _cancellator = stoppingToken is { CanBeCanceled: true }
            ? CancellationTokenSource.CreateLinkedTokenSource(stoppingToken)
            : new CancellationTokenSource();

        _completion = _getTikTok(_cancellator.Token);
    }

    public async ValueTask DisposeAsync() {
        var cancellator = Interlocked.Exchange(ref _cancellator, null);
        if (cancellator is null)
            return;

        if (!cancellator.IsCancellationRequested)
            await cancellator.CancelAsync().ConfigureAwait(false);

        if (_completion is { } completion)
            await completion.ConfigureAwait(false);

        cancellator.Dispose();
    }
}