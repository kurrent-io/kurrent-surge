using System.Runtime.CompilerServices;
using TUnit.Assertions.Core;

namespace Kurrent.Testkit.TUnit.Assertions;

/// <summary>
/// Custom TUnit assertion extensions for the Kurrent test suite.
/// </summary>
public static class TUnitAssertionExtensions {
    extension<TValue>(IAssertionSource<TValue> source) {
        /// <summary>
        /// Asserts that the value is a valid <see cref="Guid"/> or a string parseable as one.
        /// </summary>
        /// <example>
        /// <code>
        /// await Assert.That(record.Id).IsGuid();
        /// </code>
        /// </example>
        public GuidAssertion<TValue> IsGuid() {
            source.Context.ExpressionBuilder.Append(".IsGuid()");
            return new GuidAssertion<TValue>(source.Context);
        }

        /// <summary>
        /// Asserts deep structural equivalency between the actual value and <paramref name="expected"/>
        /// using recursive property-by-property comparison.
        /// </summary>
        /// <param name="expected">The expected object to compare against.</param>
        /// <param name="expression">Captured automatically — the source expression of <paramref name="expected"/>.</param>
        /// <example>
        /// <code>
        /// await Assert.That(actual).IsDeepEquivalentTo(expected);
        /// </code>
        /// </example>
        public ObjectGraphEquivalencyAssertion<TValue> IsDeepEquivalentTo(
            object? expected,
            [CallerArgumentExpression(nameof(expected))]
            string? expression = null
        ) {
            source.Context.ExpressionBuilder.Append($".IsDeepEquivalentTo({expression})");
            return new ObjectGraphEquivalencyAssertion<TValue>(source.Context, expected);
        }

        /// <summary>
        /// Asserts deep structural equivalency with custom configuration for controlling
        /// comparison behavior (e.g., excluding members, ignoring collection order, numeric tolerance).
        /// </summary>
        /// <param name="expected">The expected object to compare against.</param>
        /// <param name="configure">Action to configure the equivalency comparison.</param>
        /// <param name="expression">Captured automatically — the source expression of <paramref name="expected"/>.</param>
        /// <example>
        /// <code>
        /// await Assert.That(actual).IsDeepEquivalentTo(expected, cfg => cfg
        ///     .Excluding&lt;Order&gt;(x => x.Timestamp)
        ///     .IgnoringCollectionOrder());
        /// </code>
        /// </example>
        public ObjectGraphEquivalencyAssertion<TValue> IsDeepEquivalentTo(
            object?                          expected,
            Action<EquivalencyConfiguration> configure,
            [CallerArgumentExpression(nameof(expected))]
            string? expression = null
        ) {
            source.Context.ExpressionBuilder.Append($".IsDeepEquivalentTo({expression}, ...)");
            var config = new EquivalencyConfiguration();
            configure(config);
            return new ObjectGraphEquivalencyAssertion<TValue>(source.Context, expected, config);
        }
    }
}
