using System.Runtime.CompilerServices;
using TUnit.Assertions.Core;

namespace Kurrent.Testkit.TUnit.Assertions;

/// <summary>
/// A conditional assertion that evaluates one of two branches based on a condition.
/// Extends <see cref="Assertion{TValue}"/> so it participates in TUnit's assertion pipeline
/// natively — no ValueTask storage issues (CA2012), supports <c>.And</c> / <c>.Or</c> chaining.
/// </summary>
/// <example>
/// <code>
/// // Bool condition
/// await Assert.That(record.Value)
///     .When(!skipDecoding, x => x.IsEquivalentTo(expected))
///     .Otherwise(x => x.IsNull());
///
/// // Predicate condition
/// await Assert.That(record.Value)
///     .When(x => x is not null, x => x.IsEquivalentTo(expected))
///     .Otherwise(x => x.IsNull());
/// </code>
/// </example>
public class ConditionalAssertion<TValue> : Assertion<TValue> {
    readonly IAssertionSource<TValue>                              _source;
    readonly Func<TValue, bool>                                    _evaluate;
    readonly Func<IAssertionSource<TValue>, Assertion<TValue>>     _whenTrue;
    Func<IAssertionSource<TValue>, Assertion<TValue>>?             _whenFalse;

    internal ConditionalAssertion(
        AssertionContext<TValue>                                context,
        IAssertionSource<TValue>                               source,
        Func<TValue, bool>                                     evaluate,
        Func<IAssertionSource<TValue>, Assertion<TValue>>      whenTrue
    ) : base(context) {
        _source   = source;
        _evaluate = evaluate;
        _whenTrue = whenTrue;
    }

    /// <summary>
    /// Provides the assertion to evaluate when the condition is false.
    /// </summary>
    public ConditionalAssertion<TValue> Otherwise(
        Func<IAssertionSource<TValue>, Assertion<TValue>> whenFalse,
        [CallerArgumentExpression(nameof(whenFalse))] string? expression = null
    ) {
        _whenFalse = whenFalse;
        return this;
    }

    protected override async Task<AssertionResult> CheckAsync(EvaluationMetadata<TValue> metadata) {
        if (metadata.Exception is { } ex)
            return AssertionResult.Failed($"threw {ex.GetType().Name}: {ex.Message}");

        var conditionMet = _evaluate(metadata.Value!);
        var factory       = conditionMet ? _whenTrue : _whenFalse;

        if (factory is null)
            return AssertionResult.Passed;

        var assertion = factory(_source);

        try {
            await assertion.AssertAsync();
            return AssertionResult.Passed;
        }
        catch (Exception ex2) {
            return AssertionResult.Failed(ex2.Message);
        }
    }

    protected override string GetExpectation() => "conditional assertion";
}

/// <summary>
/// Extension methods for conditional assertions on TUnit assertion sources and assertions.
/// </summary>
public static class ConditionalAssertionExtensions {
    extension<TValue>(Assertion<TValue> assertion) {
        /// <summary>
        /// Conditionally evaluates a completed assertion chain.
        /// When <paramref name="condition"/> is false, the assertion is skipped.
        /// </summary>
        /// <example>
        /// <code>
        /// await Assert.That(record.Value).IsEquivalentTo(expected).When(!skipDecoding);
        /// </code>
        /// </example>
        public async ValueTask<TValue?> When(
            bool condition,
            [CallerArgumentExpression(nameof(condition))] string? expression = null
        ) {
            if (!condition) return default;

            return await assertion;
        }
    }

    extension<TValue>(IAssertionSource<TValue> source) {
        /// <summary>
        /// Conditionally applies an assertion based on a boolean condition.
        /// Can be awaited directly or chained with <see cref="ConditionalAssertion{TValue}.Otherwise"/>.
        /// </summary>
        /// <example>
        /// <code>
        /// await Assert.That(value).When(!skipDecoding, x => x.IsEquivalentTo(expected));
        /// </code>
        /// </example>
        public ConditionalAssertion<TValue> When(
            bool condition,
            Func<IAssertionSource<TValue>, Assertion<TValue>> assertion,
            [CallerArgumentExpression(nameof(condition))] string? expression = null
        ) {
            source.Context.ExpressionBuilder.Append($".When({expression}, ...)");
            return new ConditionalAssertion<TValue>(source.Context, source, _ => condition, assertion);
        }

        /// <summary>
        /// Conditionally applies an assertion based on a predicate evaluated against the actual value.
        /// Can be awaited directly or chained with <see cref="ConditionalAssertion{TValue}.Otherwise"/>.
        /// </summary>
        /// <example>
        /// <code>
        /// await Assert.That(record.Value)
        ///     .When(x => x is not null, x => x.IsEquivalentTo(expected))
        ///     .Otherwise(x => x.IsNull());
        /// </code>
        /// </example>
        public ConditionalAssertion<TValue> When(
            Predicate<TValue> condition,
            Func<IAssertionSource<TValue>, Assertion<TValue>> assertion,
            [CallerArgumentExpression(nameof(condition))] string? expression = null
        ) {
            source.Context.ExpressionBuilder.Append($".When({expression}, ...)");
            return new ConditionalAssertion<TValue>(source.Context, source, v => condition(v), assertion);
        }
    }
}
