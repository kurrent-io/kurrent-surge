using TUnit.Assertions.Exceptions;

namespace Kurrent.Testkit.Tests;

[Category("Testing")]
public class EventuallyAssertionTests {
    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    // Stand-in for a domain transient error (e.g. a not-found while the server catches up).
    sealed class TransientTestException(string message) : Exception(message);

    // Tight polling so every test completes in milliseconds.
    // Property (not field): options are mutable, each test needs its own instance.
    static EventuallyOptions TestOptions =>
        new EventuallyOptions {
            Timeout         = TimeSpan.FromMilliseconds(500),
            PollingInterval = TimeSpan.FromMilliseconds(10)
        }.RetryOn<TransientTestException>();

    [Test]
    public async ValueTask retries_transient_exception_until_value_resolves() {
        // Arrange
        var expected = Faker.Lorem.Word();
        var attempts = 0;

        // First two polls throw a transient error, third resolves.
        Func<Task<string>> action = () => ++attempts < 3
            ? throw new TransientTestException(expected)
            : Task.FromResult(expected);

        // Act
        var result = await Assert.That(action)
            .EventuallyPasses(s => s.IsEqualTo(expected), TestOptions);

        // Assert
        await Assert.That(result).IsEqualTo(expected);
        await Assert.That(attempts).IsEqualTo(3);
    }

    [Test]
    public async ValueTask retries_failing_assertion_until_value_changes() {
        // Arrange
        var expected = Faker.Random.Int(min: 10);
        var current  = 0;

        // Value grows each poll; the assertion only passes once it reaches expected.
        Func<Task<int>> action = () => Task.FromResult(current = Math.Min(current + expected / 2, expected));

        // Act
        var result = await Assert.That(action)
            .EventuallyPasses(s => s.IsEqualTo(expected), TestOptions);

        // Assert
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask rethrows_non_transient_exception_immediately() {
        // Arrange
        var message  = Faker.Lorem.Sentence();
        var attempts = 0;

        Func<Task<int>> action = () => {
            attempts++;
            throw new InvalidOperationException(message);
        };

        // Act & Assert: InvalidOperationException is not in the transient set — no retries.
        await Assert.That(async () => await Assert.That(action)
                .EventuallyPasses(s => s.IsEqualTo(0), TestOptions))
            .Throws<InvalidOperationException>()
            .WithMessageContaining(message);

        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async ValueTask does_not_false_pass_when_throwing_delegate_default_satisfies_assertion() {
        // Arrange: TUnit's built-in WaitsFor would pass here — it discards the captured
        // exception and runs the nested assertion against default(int) == 0. Ours must not.
        var expected = 0;

        Func<Task<int>> action = () => throw new TransientTestException(Faker.Lorem.Word());

        // Act & Assert: delegate always throws transient; assertion accepts the default value.
        await Assert.That(async () => await Assert.That(action)
                .EventuallyPasses(s => s.IsEqualTo(expected), TestOptions))
            .Throws<AssertionException>();
    }

    [Test]
    public async ValueTask timeout_failure_reports_last_transient_exception_message() {
        // Arrange
        var message = Faker.Lorem.Word();

        Func<Task<string>> action = () => throw new TransientTestException(message);

        // Act & Assert: the timeout message must carry the REAL cause, not "found null".
        await Assert.That(async () => await Assert.That(action)
                .EventuallyPasses(s => s.IsNotNull(), TestOptions))
            .Throws<AssertionException>()
            .WithMessageContaining(message);
    }

    [Test]
    public async ValueTask retry_on_extends_default_transient_set_without_replacing_it() {
        // Arrange
        var expected = Faker.Random.Int(min: 1);
        var attempts = 0;

        // Throws a RetryOn-added type first, then a delegate-thrown AssertionException —
        // the latter is only retried if the default transient set (BaseAssertionException)
        // survived the RetryOn call, proving RetryOn extends instead of replacing.
        Func<Task<int>> action = () => ++attempts switch {
            1 => throw new TransientTestException(Faker.Lorem.Word()),
            2 => throw new AssertionException("synthetic assertion failure"),
            _ => Task.FromResult(expected)
        };

        // Act
        var result = await Assert.That(action)
            .EventuallyPasses(s => s.IsEqualTo(expected), TestOptions);

        // Assert
        await Assert.That(result).IsEqualTo(expected);
        await Assert.That(attempts).IsEqualTo(3);
    }

    [Test]
    public async ValueTask retries_multi_value_assert_block_with_assert_multiple() {
        // Arrange
        var expectedAttempts = Faker.Random.Int(2, 4);
        var attempts         = 0;

        // Act: the block asserts two different values; Assert.Multiple aggregates their
        // failures into one AssertionException, which the polling assertion retries as a unit.
        await Assert.That(async () => {
            attempts++;

            using var _ = Assert.Multiple();

            await Assert.That(attempts).IsGreaterThanOrEqualTo(expectedAttempts);
            await Assert.That(attempts % 2 == 0 || attempts >= expectedAttempts).IsTrue();
        }).EventuallyPasses(TestOptions);

        // Assert
        await Assert.That(attempts).IsEqualTo(expectedAttempts);
    }

    [Test]
    public async ValueTask retries_sync_delegate_until_it_completes() {
        // Arrange
        var expectedAttempts = Faker.Random.Int(2, 4);
        var attempts         = 0;

        // Act: sync Action source — first attempts throw a transient error, then completes.
        await Assert.That(() => {
            if (++attempts < expectedAttempts)
                throw new TransientTestException("not yet visible");
        }).EventuallyPasses(TestOptions);

        // Assert
        await Assert.That(attempts).IsEqualTo(expectedAttempts);
    }

    [Test]
    public async ValueTask cancellation_stops_polling_with_operation_canceled() {
        // Arrange
        using var cts = new CancellationTokenSource();

        var attempts = 0;

        // Always-transient delegate; cancels its own polling on the second attempt.
        Func<Task<int>> action = () => {
            if (++attempts == 2)
                cts.Cancel();

            throw new TransientTestException("not yet visible");
        };

        var options = TestOptions;
        options.CancellationToken = cts.Token;

        // Act & Assert: cancellation surfaces as OperationCanceledException — neither
        // retried nor converted into an assertion failure.
        await Assert.That(async () => await Assert.That(action)
                .EventuallyPasses(s => s.IsEqualTo(1), options))
            .Throws<OperationCanceledException>();

        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async ValueTask block_form_rethrows_non_transient_exception_immediately() {
        // Arrange
        var message  = Faker.Lorem.Sentence();
        var attempts = 0;

        // Act & Assert: bare overload, fatal exception inside the block — no retries.
        await Assert.That(async () => await Assert.That(async () => {
                attempts++;
                throw new InvalidOperationException(message);
            }).EventuallyPasses(TestOptions))
            .Throws<InvalidOperationException>()
            .WithMessageContaining(message);

        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async ValueTask timeout_failure_reports_last_assertion_error() {
        // Arrange: the value never satisfies the assertion, so the timeout message must
        // carry the last nested assertion error (expected vs found), not a generic line.
        var expected = Faker.Random.Int(min: 10);
        var wrong    = expected - 1;

        Func<Task<int>> action = () => Task.FromResult(wrong);

        var options = TestOptions;
        options.Timeout = TimeSpan.FromMilliseconds(100);

        // Act & Assert
        await Assert.That(async () => await Assert.That(action)
                .EventuallyPasses(s => s.IsEqualTo(expected), options))
            .Throws<AssertionException>()
            .WithMessageContaining(wrong.ToString());
    }

    [Test]
    public async ValueTask returns_value_when_assertion_passes_on_first_poll() {
        // Arrange
        var expected = Faker.Lorem.Word();
        var attempts = 0;

        Func<Task<string>> action = () => {
            attempts++;
            return Task.FromResult(expected);
        };

        // Act
        var result = await Assert.That(action)
            .EventuallyPasses(s => s.IsEqualTo(expected), TestOptions);

        // Assert
        await Assert.That(result).IsEqualTo(expected);
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async ValueTask custom_transient_predicate_overrides_default_set() {
        // Arrange
        var expected = Faker.Random.Int();
        var attempts = 0;

        // InvalidOperationException is normally fatal; the custom predicate retries it.
        Func<Task<int>> action = () => ++attempts < 2
            ? throw new InvalidOperationException()
            : Task.FromResult(expected);

        // Act: uses the configure-delegate overload.
        var result = await Assert.That(action)
            .EventuallyPasses(s => s.IsEqualTo(expected), options => {
                options.Timeout         = TimeSpan.FromMilliseconds(500);
                options.PollingInterval = TimeSpan.FromMilliseconds(10);
                options.IsTransient     = ex => ex is InvalidOperationException or BaseAssertionException;
            });

        // Assert
        await Assert.That(result).IsEqualTo(expected);
        await Assert.That(attempts).IsEqualTo(2);
    }
}
