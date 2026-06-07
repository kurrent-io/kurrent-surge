using System.Diagnostics;
using System.Runtime.ExceptionServices;
using TUnit.Assertions.Core;
using TUnit.Assertions.Exceptions;
using TUnit.Assertions.Sources;

namespace Kurrent.Testkit.TUnit.Assertions;

/// <summary>
/// Polling assertion for eventually-consistent state — retries transient exceptions and
/// failing nested assertions until they pass or the timeout elapses.
/// </summary>
/// <remarks>
/// Modeled on TUnit's <c>WaitsForAssertion</c>, fixing three flaws: it discards the exception
/// captured from the polled delegate (the nested assertion then runs against <c>default(TValue)</c>,
/// false-passing when the assertion accepts the default and hiding the real cause in timeout
/// messages), and it double-evaluates the delegate on the first iteration. Here the captured
/// exception is inspected: transient ones are retried, anything else rethrows immediately
/// with the original stack trace.
/// </remarks>
public sealed class EventuallyAssertion<TValue>(
    AssertionContext<TValue> context,
    Func<IAssertionSource<TValue>, Assertion<TValue>> assertionBuilder,
    EventuallyOptions options
) : Assertion<TValue>(context) {
    protected override async Task<AssertionResult> CheckAsync(EvaluationMetadata<TValue> metadata) {
        var (timeout, pollingInterval, cancellationToken) = (
            options.Timeout, options.PollingInterval, options.CancellationToken);

        var stopwatch    = Stopwatch.StartNew();
        var attemptCount = 0;
        var firstAttempt = true;

        Exception? lastException = null;

        while (stopwatch.Elapsed < timeout) {
            cancellationToken.ThrowIfCancellationRequested();

            attemptCount++;

            // The base assertion flow already evaluated the delegate once before calling
            // CheckAsync (its result is in metadata) — reuse it for the first attempt so the
            // delegate runs exactly once per poll. TUnit's own WaitsFor re-evaluates here,
            // silently invoking non-idempotent delegates twice on the first iteration.
            // The source evaluator captures delegate exceptions into the tuple instead of
            // throwing (see TUnit's AsyncFuncAssertion), so ReevaluateAsync never throws.
            var (value, exception) = firstAttempt
                ? (metadata.Value, metadata.Exception)
                : await Context.Evaluation.ReevaluateAsync();

            firstAttempt = false;

            if (exception is not null) {
                // Non-transient failures are real bugs — rethrow with the original stack
                // trace instead of letting them surface as a misleading default-value
                // assertion failure at timeout.
                if (!options.IsTransient(exception))
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
        $"assertion to pass within {options.Timeout.TotalMilliseconds:F0}ms (polling every {options.PollingInterval.TotalMilliseconds:F0}ms)";
}

/// <summary>
/// Tuning knobs for <see cref="EventuallyAssertion{TValue}"/>.
/// </summary>
/// <remarks>
/// Framework-generic: the only default transient is <c>BaseAssertionException</c>.
/// Domain-specific transient sets are layered on by callers via <see cref="RetryOn(Type[])"/>.
/// </remarks>
public sealed class EventuallyOptions {
    // Kept so DescribeTransients can tell "still the default predicate" (render the type
    // list) apart from a caller-supplied one (render "<custom predicate>").
    readonly Func<Exception, bool> _defaultIsTransient;

    public EventuallyOptions() =>
        IsTransient = _defaultIsTransient = ex => TransientExceptions.Any(t => t.IsInstanceOfType(ex));

    // Settable so the Action<EventuallyOptions> configure overload can mutate a fresh
    // instance — mirrors the SDK's Create(Action<TOptions>) convention.
    public TimeSpan          Timeout           { get; set; } = TimeSpan.FromSeconds(3);
    public TimeSpan          PollingInterval   { get; set; } = TimeSpan.FromMilliseconds(50);
    public CancellationToken CancellationToken { get; set; }

    /// <summary>
    /// Exception types retried while polling. Rendered into assertion failure messages.
    /// </summary>
    public IReadOnlyList<Type> TransientExceptions { get; set; } = [typeof(BaseAssertionException)];

    /// <summary>
    /// Decides whether a delegate-thrown exception is retried. Defaults to matching against
    /// <see cref="TransientExceptions"/>. Replacing it bypasses the list entirely.
    /// </summary>
    public Func<Exception, bool> IsTransient { get; set; }

    /// <summary>
    /// Extends the transient set with extra exception types.
    /// </summary>
    public EventuallyOptions RetryOn(params Type[] exceptionTypes) {
        TransientExceptions = [.. TransientExceptions, .. exceptionTypes];
        return this;
    }

    /// <summary>
    /// Generic counterpart of <see cref="RetryOn(Type[])"/>. Chain for more types.
    /// </summary>
    public EventuallyOptions RetryOn<T>() where T : Exception
        => RetryOn(typeof(T));

    internal string DescribeTransients() =>
        ReferenceEquals(IsTransient, _defaultIsTransient)
            ? string.Join(", ", TransientExceptions.Select(t => t.Name))
            : "<custom predicate>";
}

public static class EventuallyAssertionExtensions {
    extension<TValue>(IAssertionSource<TValue> source) {
        /// <summary>
        /// Polls the source until the nested assertion passes, retrying transient exceptions
        /// thrown by the source delegate.
        /// </summary>
        public EventuallyAssertion<TValue> EventuallyPasses(
            Func<IAssertionSource<TValue>, Assertion<TValue>> assertionBuilder,
            EventuallyOptions? options = null
        ) {
            options ??= new();

            source.Context.ExpressionBuilder.Append(
                $".EventuallyPasses(..., timeout: {options.Timeout.TotalMilliseconds:F0}ms, retryOn: [{options.DescribeTransients()}])");

            return new(source.Context, assertionBuilder, options);
        }

        /// <summary>
        /// Convenience overload: configure a fresh <see cref="EventuallyOptions"/> via delegate.
        /// </summary>
        public EventuallyAssertion<TValue> EventuallyPasses(
            Func<IAssertionSource<TValue>, Assertion<TValue>> assertionBuilder,
            Action<EventuallyOptions> configure
        ) {
            var options = new EventuallyOptions();
            configure(options);
            return source.EventuallyPasses(assertionBuilder, options);
        }
    }

    // Delegate sources need their own overloads on the concrete types: AsyncDelegateAssertion
    // implements IAssertionSource twice (object? and Task), so the generic TValue extension
    // is ambiguous on it. Their evaluators always yield (null, exception?) — completing
    // without throwing is the pass condition, expressed as IsNull on the constant null value.
    extension(AsyncDelegateAssertion source) {
        /// <summary>
        /// Polls the delegate until it completes without throwing, retrying transient exceptions.
        /// </summary>
        public EventuallyAssertion<object?> EventuallyPasses(EventuallyOptions? options = null) =>
            CompletesEventually(source.Context, options);
    }

    extension(DelegateAssertion source) {
        /// <summary>
        /// Polls the delegate until it completes without throwing, retrying transient exceptions.
        /// </summary>
        public EventuallyAssertion<object?> EventuallyPasses(EventuallyOptions? options = null) =>
            CompletesEventually(source.Context, options);
    }

    static EventuallyAssertion<object?> CompletesEventually(AssertionContext<object?> context, EventuallyOptions? options) {
        options ??= new();

        context.ExpressionBuilder.Append(
            $".EventuallyPasses(timeout: {options.Timeout.TotalMilliseconds:F0}ms, retryOn: [{options.DescribeTransients()}])");

        return new EventuallyAssertion<object?>(context, static s => s.IsNull(), options);
    }
}
