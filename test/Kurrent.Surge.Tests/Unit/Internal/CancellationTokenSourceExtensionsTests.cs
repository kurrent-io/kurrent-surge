namespace Kurrent.Surge.Tests.Internal;

public class CancellationTokenSourceExtensionsTests {
    [Test]
    public async ValueTask returns_cancellable_token() {
        // Arrange
        using var cts = new CancellationTokenSource();

        // Act
        var token = cts.UnsafeRegister((_, _) => Task.CompletedTask, 0);

        // Assert
        await Assert.That(token.CanBeCanceled).IsTrue();
        await Assert.That(token.IsCancellationRequested).IsFalse();
    }

    [Test]
    public async ValueTask invokes_callback_with_state_on_cancellation() {
        // Arrange
        using var cts = new CancellationTokenSource();

        var receivedValue = 0;

        cts.UnsafeRegister(
            (state, _) => {
                receivedValue = state;
                return Task.CompletedTask;
            },
            42);

        // Act
        await cts.CancelAsync();

        // Assert
        await Assert.That(receivedValue).IsEqualTo(42);
    }

    [Test]
    public async ValueTask invokes_callback_with_cancelled_token() {
        // Arrange
        using var cts = new CancellationTokenSource();

        var receivedToken = CancellationToken.None;

        cts.UnsafeRegister(
            (_, ct) => {
                receivedToken = ct;
                return Task.CompletedTask;
            },
            0);

        // Act
        await cts.CancelAsync();

        // Assert
        await Assert.That(receivedToken.IsCancellationRequested).IsTrue();
    }

    [Test]
    public async ValueTask does_not_invoke_callback_before_cancellation() {
        // Arrange
        using var cts = new CancellationTokenSource();

        var callbackInvoked = false;

        cts.UnsafeRegister(
            (_, _) => {
                callbackInvoked = true;
                return Task.CompletedTask;
            },
            0);

        // Act
        await Task.Delay(50);

        // Assert
        await Assert.That(callbackInvoked).IsFalse();
    }

    [Test]
    public async ValueTask passes_complex_state_to_callback() {
        // Arrange
        using var cts = new CancellationTokenSource();

        var receivedState = (Name: "", Count: 0);

        cts.UnsafeRegister<(string Name, int Count)>(
            (state, _) => {
                receivedState = state;
                return Task.CompletedTask;
            },
            ("hello", 7));

        // Act
        await cts.CancelAsync();

        // Assert
        await Assert.That(receivedState.Name).IsEqualTo("hello");
        await Assert.That(receivedState.Count).IsEqualTo(7);
    }

    [Test]
    public async ValueTask multiple_registrations_all_fire_on_cancellation() {
        // Arrange
        using var cts = new CancellationTokenSource();

        var firstFired  = false;
        var secondFired = false;

        cts.UnsafeRegister(
            (_, _) => {
                firstFired = true;
                return Task.CompletedTask;
            },
            0);

        cts.UnsafeRegister(
            (_, _) => {
                secondFired = true;
                return Task.CompletedTask;
            },
            0);

        // Act
        await cts.CancelAsync();

        // Assert
        await Assert.That(firstFired).IsTrue();
        await Assert.That(secondFired).IsTrue();
    }

    [Test]
    public async ValueTask callback_exception_surfaces_on_cancellation() {
        // Arrange
        using var cts = new CancellationTokenSource();

        cts.UnsafeRegister(
            (_, _) => throw new InvalidOperationException("boom"),
            0);

        // Act
        AggregateException? caught = null;

        try {
            await cts.CancelAsync();
        }
        catch (AggregateException ex) {
            caught = ex;
        }

        // Assert
        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.InnerExceptions.Count).IsEqualTo(1);
        await Assert.That(caught.InnerExceptions[0]).IsTypeOf<InvalidOperationException>();
        await Assert.That(caught.InnerExceptions[0].Message).IsEqualTo("boom");
    }

    [Test]
    public async ValueTask executes_task_state_only_inside_callback() {
        // Arrange
        using var cts = new CancellationTokenSource();

        var executed = false;
        var task = new Task(() => executed = true);

        cts.UnsafeRegister(
            (state, _) => {
                state.RunSynchronously();
                return Task.CompletedTask;
            },
            task);

        // Act
        await Assert.That(executed).IsFalse();
        await cts.CancelAsync();

        // Assert
        await Assert.That(executed).IsTrue();
        await Assert.That(task.IsCompletedSuccessfully).IsTrue();
    }

    [Test]
    public async ValueTask executes_deferred_task_run_only_inside_callback() {
        // Arrange
        using var cts = new CancellationTokenSource();

        var executed = false;
        Func<Task> action = () => Task.Run(() => executed = true);

        cts.UnsafeRegister((state, _) => state(), action);

        // Act
        await Assert.That(executed).IsFalse();
        await cts.CancelAsync();

        // Assert
        await Assert.That(executed).IsTrue();
    }

    [Test]
    public async ValueTask fires_immediately_when_already_cancelled() {
        // Arrange
        using var cts = new CancellationTokenSource();

        await cts.CancelAsync();

        var callbackFired = false;

        // Act
        cts.UnsafeRegister(
            (_, _) => {
                callbackFired = true;
                return Task.CompletedTask;
            },
            0);

        // Assert
        await Assert.That(callbackFired).IsTrue();
    }
}