using Microsoft.Extensions.Time.Testing;

namespace Kurrent.Surge.Tests.Internal;

public class AsyncPeriodicTimerTests {
    static readonly TimeSpan Period = TimeSpan.FromSeconds(1);

    #region ->> Tick Behavior <<-

    [Test]
    public async ValueTask invokes_callback_on_each_tick() {
        // Arrange
        var tickCount = 0;
        var timeProvider = new FakeTimeProvider();

        await using var sut = new AsyncPeriodicTimer(
            Period,
            _ => { Interlocked.Increment(ref tickCount); return Task.CompletedTask; },
            timeProvider
        );

        // Act
        sut.Start();

        timeProvider.Advance(Period);
        await Task.Delay(50);
        timeProvider.Advance(Period);
        await Task.Delay(50);
        timeProvider.Advance(Period);
        await Task.Delay(50);

        // Assert
        await Assert.That(tickCount).IsEqualTo(3);
    }

    [Test]
    public async ValueTask does_not_tick_before_period_elapses() {
        // Arrange
        var tickCount = 0;
        var timeProvider = new FakeTimeProvider();

        await using var sut = new AsyncPeriodicTimer(
            Period,
            _ => { Interlocked.Increment(ref tickCount); return Task.CompletedTask; },
            timeProvider
        );

        // Act
        sut.Start();

        timeProvider.Advance(Period - TimeSpan.FromMilliseconds(1));
        await Task.Delay(50);

        // Assert
        await Assert.That(tickCount).IsEqualTo(0);
    }

    [Test]
    public async ValueTask passes_cancellation_token_to_callback() {
        // Arrange
        var receivedToken = CancellationToken.None;
        var timeProvider = new FakeTimeProvider();

        await using var sut = new AsyncPeriodicTimer(
            Period,
            ct => { receivedToken = ct; return Task.CompletedTask; },
            timeProvider
        );

        // Act
        sut.Start();

        timeProvider.Advance(Period);
        await Task.Delay(50);

        // Assert
        await Assert.That(receivedToken.CanBeCanceled).IsTrue();
    }

    #endregion // Tick Behavior

    #region ->> Lifecycle <<-

    [Test]
    public async ValueTask throws_when_started_twice() {
        // Arrange
        var timeProvider = new FakeTimeProvider();

        await using var sut = new AsyncPeriodicTimer(
            Period,
            _ => Task.CompletedTask,
            timeProvider
        );

        sut.Start();

        // Act & Assert
        await Assert.That(() => sut.Start()).Throws<InvalidOperationException>();
    }

    [Test]
    public async ValueTask dispose_completes_when_never_started() {
        // Arrange
        var timeProvider = new FakeTimeProvider();

        var sut = new AsyncPeriodicTimer(
            Period,
            _ => Task.CompletedTask,
            timeProvider
        );

        // Act & Assert — should not throw
        await sut.DisposeAsync();
    }

    [Test]
    public async ValueTask double_dispose_after_start_does_not_throw() {
        // Arrange
        var timeProvider = new FakeTimeProvider();

        var sut = new AsyncPeriodicTimer(
            Period,
            _ => Task.CompletedTask,
            timeProvider
        );

        sut.Start();
        await sut.DisposeAsync();

        // Act & Assert — second dispose is a no-op
        await sut.DisposeAsync();
    }

    [Test]
    public async ValueTask double_dispose_without_start_does_not_throw() {
        // Arrange
        var timeProvider = new FakeTimeProvider();

        var sut = new AsyncPeriodicTimer(
            Period,
            _ => Task.CompletedTask,
            timeProvider
        );

        await sut.DisposeAsync();

        // Act & Assert — second dispose is a no-op
        await sut.DisposeAsync();
    }

    [Test]
    public async ValueTask dispose_waits_for_inflight_callback() {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var callbackStarted = new SemaphoreSlim(0);
        var callbackGate = new SemaphoreSlim(0);
        var callbackCompleted = false;

        var sut = new AsyncPeriodicTimer(
            Period,
            async _ => {
                callbackStarted.Release();
                await callbackGate.WaitAsync().ConfigureAwait(false);
                callbackCompleted = true;
            },
            timeProvider
        );

        sut.Start();

        timeProvider.Advance(Period);
        await callbackStarted.WaitAsync();

        // Act
        var disposeTask = sut.DisposeAsync();

        await Assert.That(callbackCompleted).IsFalse();

        callbackGate.Release();
        await disposeTask;

        // Assert
        await Assert.That(callbackCompleted).IsTrue();
    }

    [Test]
    public async ValueTask stops_ticking_after_dispose() {
        // Arrange
        var tickCount = 0;
        var timeProvider = new FakeTimeProvider();

        var sut = new AsyncPeriodicTimer(
            Period,
            _ => { Interlocked.Increment(ref tickCount); return Task.CompletedTask; },
            timeProvider
        );

        sut.Start();

        timeProvider.Advance(Period);
        await Task.Delay(50);
        timeProvider.Advance(Period);
        await Task.Delay(50);

        var countBeforeDispose = tickCount;

        // Act
        await sut.DisposeAsync();

        timeProvider.Advance(Period);
        await Task.Delay(50);
        timeProvider.Advance(Period);
        await Task.Delay(50);

        // Assert
        await Assert.That(countBeforeDispose).IsEqualTo(2);
        await Assert.That(tickCount).IsEqualTo(countBeforeDispose);
    }

    #endregion // Lifecycle

    #region ->> Cancellation <<-

    [Test]
    public async ValueTask stops_when_external_token_cancelled() {
        // Arrange
        var tickCount = 0;
        var timeProvider = new FakeTimeProvider();
        using var cts = new CancellationTokenSource();

        var sut = new AsyncPeriodicTimer(
            Period,
            _ => { Interlocked.Increment(ref tickCount); return Task.CompletedTask; },
            timeProvider
        );

        sut.Start(cts.Token);

        timeProvider.Advance(Period);
        await Task.Delay(50);

        // Act
        await cts.CancelAsync();
        await Task.Delay(50);

        timeProvider.Advance(Period);
        await Task.Delay(50);

        // Assert
        await Assert.That(tickCount).IsEqualTo(1);

        await sut.DisposeAsync();
    }

    [Test]
    public async ValueTask does_not_tick_when_started_with_cancelled_token() {
        // Arrange
        var tickCount = 0;
        var timeProvider = new FakeTimeProvider();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var sut = new AsyncPeriodicTimer(
            Period,
            _ => { Interlocked.Increment(ref tickCount); return Task.CompletedTask; },
            timeProvider
        );

        // Act
        sut.Start(cts.Token);

        timeProvider.Advance(Period);
        await Task.Delay(50);
        timeProvider.Advance(Period);
        await Task.Delay(50);

        // Assert
        await Assert.That(tickCount).IsEqualTo(0);

        await sut.DisposeAsync();
    }

    #endregion // Cancellation

    #region ->> Error Handling <<-

    [Test]
    public async ValueTask propagates_callback_exception_through_dispose() {
        // Arrange
        var timeProvider = new FakeTimeProvider();

        var sut = new AsyncPeriodicTimer(
            Period,
            _ => throw new InvalidOperationException("boom"),
            timeProvider
        );

        sut.Start();

        timeProvider.Advance(Period);
        await Task.Delay(50);

        // Act & Assert
        await Assert.That(async () => await sut.DisposeAsync()).Throws<InvalidOperationException>();
    }

    #endregion // Error Handling
}
