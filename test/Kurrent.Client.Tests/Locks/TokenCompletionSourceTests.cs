using System.Runtime.ExceptionServices;

namespace Kurrent.Client.Tests.Locks;

[Category("Locks")]
public class TokenCompletionSourceTests {
	[Test]
	public async ValueTask complete_returns_valid_cancellation_token() {
		// Arrange
		using var sut = new TokenCompletionSource();

		// Act
		sut.Complete();
		var token = await sut.Task;

		// Assert
		await Assert.That(token.CanBeCanceled).IsTrue();
		await Assert.That(token.IsCancellationRequested).IsFalse();
	}

	[Test]
	public async ValueTask complete_sets_task_to_completed_state() {
		// Arrange
		using var sut = new TokenCompletionSource();

		// Act
		sut.Complete();
		await sut.Task;

		// Assert
		await Assert.That(sut.Task.IsCompleted).IsTrue();
		await Assert.That(sut.Task.IsCompletedSuccessfully).IsTrue();
		await Assert.That(sut.Task.IsFaulted).IsFalse();
		await Assert.That(sut.Task.IsCanceled).IsFalse();
	}

	[Test, Skip("should the task return immediately on cancel an cancellation token?'")]
	public async ValueTask cancel_without_exception_completes_task_successfully() {
		// Arrange
		using var sut = new TokenCompletionSource();

		// Act
		sut.Cancel();
		var token = await sut.Task;

		// Assert
		await Assert.That(token.IsCancellationRequested).IsTrue();
	}

	[Test]
	public async ValueTask cancel_with_exception_faults_task_on_dispose() {
		// Arrange
		var sut = new TokenCompletionSource();
		var expectedException = new InvalidOperationException("Test exception");

		// Act
		sut.Cancel(expectedException);
		sut.Dispose();

		// Assert
		await Assert.That(sut.Task.IsFaulted).IsTrue();
		await Assert.That(async () => await sut.Task)
			.Throws<InvalidOperationException>()
			.WithMessage("Test exception");
	}

	[Test]
	public async ValueTask dispose_without_cancel_completes_task_and_cancels_token() {
		// Arrange
		var sut = new TokenCompletionSource();

		// Act
		sut.Dispose();
		var token = await sut.Task;

		// Assert
		await Assert.That(sut.Task.IsCompletedSuccessfully).IsTrue();
		await Assert.That(token.IsCancellationRequested).IsTrue();
	}

	[Test]
	public async ValueTask dispose_after_complete_does_not_preserve_completed_state() {
		// Arrange
		var sut = new TokenCompletionSource();
		sut.Complete();
		var token = await sut.Task;

		// Act
		sut.Dispose();

		// Assert
		await Assert.That(sut.Task.IsCompletedSuccessfully).IsTrue();
		await Assert.That(token.IsCancellationRequested).IsTrue();
	}

	[Test]
	public async ValueTask multiple_complete_calls_are_idempotent() {
		// Arrange
		using var sut = new TokenCompletionSource();

		// Act
		sut.Complete();
		sut.Complete();
		sut.Complete();
		var token = await sut.Task;

		// Assert
		await Assert.That(sut.Task.IsCompletedSuccessfully).IsTrue();
		await Assert.That(token.IsCancellationRequested).IsFalse();
	}

	[Test]
	public async ValueTask cancel_after_complete_does_not_change_task_state() {
		// Arrange
		using var sut = new TokenCompletionSource();
		sut.Complete();

		// Act
		sut.Cancel();

		// Assert
		await Assert.That(sut.Task.IsCompletedSuccessfully).IsTrue();
	}

	[Test]
	public async ValueTask complete_after_cancel_does_not_change_task_state() {
		// Arrange
		using var sut = new TokenCompletionSource();
		sut.Cancel();

		// Act
		sut.Complete();

		// Assert
		await Assert.That(sut.Task.IsCompletedSuccessfully).IsTrue();
	}

	[Test]
	public async ValueTask token_remains_valid_after_completion() {
		// Arrange
		using var sut = new TokenCompletionSource();
		sut.Complete();

		// Act
		var token = await sut.Task;
		await Task.Delay(10);

		// Assert
		await Assert.That(token.CanBeCanceled).IsTrue();
		await Assert.That(token.IsCancellationRequested).IsFalse();
	}

	[Test]
	public async ValueTask can_await_task_multiple_times() {
		// Arrange
		using var sut = new TokenCompletionSource();
		sut.Complete();

		// Act
		var token1 = await sut.Task;
		var token2 = await sut.Task;
		var token3 = await sut.Task;

		// Assert
		await Assert.That(token1).IsEqualTo(token2);
		await Assert.That(token2).IsEqualTo(token3);
	}

	[Test]
	public async ValueTask dispose_with_cancel_exception_sets_task_exception() {
		// Arrange
		var sut = new TokenCompletionSource();
		var expectedException = new TimeoutException("Operation timed out");

		// Act
		sut.Cancel(expectedException);
		sut.Dispose();

		// Assert
		await Assert.That(async () => await sut.Task)
			.Throws<TimeoutException>()
			.WithMessage("Operation timed out");
	}

	[Test]
	public async ValueTask cancel_multiple_times_with_different_exceptions_uses_last_exception() {
		// Arrange
		var sut = new TokenCompletionSource();
		var firstException = new InvalidOperationException("First");
		var secondException = new InvalidOperationException("Second");

		// Act
		sut.Cancel(firstException);
		sut.Cancel(secondException);
		sut.Dispose();

		// Assert
		await Assert.That(async () => await sut.Task)
			.Throws<InvalidOperationException>()
			.WithMessage("Second");
	}

	[Test, Timeout(2000)]
	public async ValueTask task_completes_immediately_when_already_completed(CancellationToken cancellationToken) {
		// Arrange
		using var sut = new TokenCompletionSource();
		sut.Complete();

		// Act
        ExceptionDispatchInfo? captured = null;
        try {
            await sut.Task.ConfigureAwait(false);
        }
        catch (Exception ex) {
            captured = ExceptionDispatchInfo.Capture(ex);
        }

		// Assert
        await Assert.That(captured).IsNull();
	}
}
