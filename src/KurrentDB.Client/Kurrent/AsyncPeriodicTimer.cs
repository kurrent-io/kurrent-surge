namespace Kurrent;

public delegate Task OnPeriodicTimerTick(bool isLastTick, CancellationToken cancellationToken);

[PublicAPI]
public sealed class AsyncPeriodicTimer : IAsyncDisposable {
    public AsyncPeriodicTimer(TimeSpan period, OnPeriodicTimerTick onTick) {
        Period    = period;
        OnTick    = onTick;
        LazyTimer = new(() => new PeriodicTimer(Period));
    }

    Lazy<PeriodicTimer> LazyTimer { get; }
    TimeSpan            Period    { get; }
    OnPeriodicTimerTick OnTick    { get; }

    PeriodicTimer Timer => LazyTimer.Value;

    Task Completion { get; set; } = Task.CompletedTask;

    public Task Start(CancellationToken stoppingToken = default) {
        if (LazyTimer.IsValueCreated)
            return Task.CompletedTask;

        if (stoppingToken != CancellationToken.None)
            stoppingToken.Register(() => {
                try {
                    Completion.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) {
                    // ignore
                }
            });

        Completion = Task.Run(TickTock, stoppingToken);

        return Task.CompletedTask;

        async Task TickTock() {
            try {
                while (await Timer.WaitForNextTickAsync(stoppingToken))
                    await OnTick(isLastTick: false, stoppingToken);

                await OnTick(isLastTick: true, stoppingToken);
            }
            catch (OperationCanceledException) {
                await OnTick(isLastTick: true, stoppingToken);
            }
        }
    }

    public async ValueTask DisposeAsync() {
        Timer.Dispose();

        try {
            await Completion;
        }
        catch (OperationCanceledException) {
            // ignore
        }

        Completion.Dispose();
    }
}
