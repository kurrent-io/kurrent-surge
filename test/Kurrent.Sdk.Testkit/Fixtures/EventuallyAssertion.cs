using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using TUnit.Assertions.Core;
using TUnit.Assertions.Exceptions;
using TUnit.Assertions.Sources;

namespace Kurrent.Sdk.Testkit;

/// <summary>
/// Polling assertion for eventually-consistent state, modeled on TUnit's <c>WaitsForAssertion</c>
/// but with transient-exception awareness.
/// <para>
/// TUnit's built-in <c>WaitsFor</c>/<c>Eventually</c> discards the exception captured from the
/// polled delegate and runs the nested assertion against <c>default(TValue)</c>. That loses the
/// real failure cause in timeout messages and false-passes when the nested assertion accepts the
/// default value (e.g. <c>IsEqualTo(0)</c>, <c>IsFalse()</c>, <c>IsNull()</c>). This assertion
/// inspects the captured exception instead: transient ones (not-found while the server catches up)
/// are retried, anything else surfaces immediately with the original stack trace.
/// </para>
/// </summary>
public sealed class EventuallyAssertion<TValue>(
    AssertionContext<TValue> context,
    Func<IAssertionSource<TValue>, Assertion<TValue>> assertionBuilder,
    Func<Exception, bool> isTransient,
    TimeSpan timeout,
    TimeSpan pollingInterval,
    CancellationToken cancellationToken
) : Assertion<TValue>(context) {
    protected override async Task<AssertionResult> CheckAsync(EvaluationMetadata<TValue> metadata) {
        var stopwatch     = Stopwatch.StartNew();
        var attemptCount  = 0;

        Exception? lastException = null;

        while (stopwatch.Elapsed < timeout) {
            cancellationToken.ThrowIfCancellationRequested();

            attemptCount++;

            // The source evaluator captures delegate exceptions into the tuple instead of
            // throwing (see TUnit's AsyncFuncAssertion), so this never throws on its own.
            var (value, exception) = await Context.Evaluation.ReevaluateAsync();

            if (exception is not null) {
                // Non-transient failures are real bugs — rethrow with the original stack
                // trace instead of letting them surface as a misleading default-value
                // assertion failure at timeout.
                if (!isTransient(exception))
                    ExceptionDispatchInfo.Capture(exception).Throw();

                lastException = exception;
            }
            else {
                try {
                    await assertionBuilder(new ValueAssertion<TValue>(value, "polled value")).AssertAsync();
                    return AssertionResult.Passed;
                }
                catch (AssertionException ex) {
                    lastException = ex;
                }
            }

            // Don't sleep past the deadline — fail now with the last observed error.
            if (stopwatch.Elapsed + pollingInterval >= timeout)
                break;

            await Task.Delay(pollingInterval, cancellationToken);
        }

        var lastError = lastException is not null
            ? $"Last error: {lastException.Message}"
            : "No attempts were made";

        return AssertionResult.Failed(
            $"assertion did not pass within {timeout.TotalMilliseconds:F0}ms after {attemptCount} attempts. {lastError}");
    }

    /// <summary>
    /// Returns the value from the last (successful) poll, enabling:
    /// <c>var schema = await Assert.That(getSchema).EventuallyPasses(s =&gt; s.IsNotNull());</c>
    /// </summary>
    public override async Task<TValue?> AssertAsync() {
        await base.AssertAsync();

        // base.AssertAsync returns the value cached by the FIRST evaluation; ReevaluateAsync
        // updated the cache on each poll, so re-read to get the value that actually passed.
        var (value, _) = await Context.GetAsync();
        return value;
    }

    protected override string GetExpectation() =>
        $"assertion to pass within {timeout.TotalMilliseconds:F0}ms (polling every {pollingInterval.TotalMilliseconds:F0}ms)";
}

public static class EventuallyAssertionExtensions {
    extension<TValue>(IAssertionSource<TValue> source) {
        /// <summary>
        /// Polls the source until the nested assertion passes, retrying transient exceptions
        /// thrown by the source delegate (assertion failures and not-found errors by default).
        /// Named <c>EventuallyPasses</c> because TUnit already ships an <c>Eventually</c>
        /// extension with the same signature — an identical name would be ambiguous.
        /// </summary>
        public EventuallyAssertion<TValue> EventuallyPasses(
            Func<IAssertionSource<TValue>, Assertion<TValue>> assertionBuilder,
            TimeSpan? timeout = null,
            TimeSpan? pollingInterval = null,
            Func<Exception, bool>? isTransient = null,
            CancellationToken cancellationToken = default,
            [CallerArgumentExpression(nameof(timeout))] string? timeoutExpression = null
        ) {
            source.Context.ExpressionBuilder.Append($".EventuallyPasses(..., timeout: {timeoutExpression ?? "default"})");

            return new EventuallyAssertion<TValue>(
                source.Context,
                assertionBuilder,
                isTransient ?? EventualConsistency.IsTransient,
                timeout ?? EventualConsistency.DefaultTimeout,
                pollingInterval ?? EventualConsistency.DefaultInterval,
                cancellationToken
            );
        }
    }
}
