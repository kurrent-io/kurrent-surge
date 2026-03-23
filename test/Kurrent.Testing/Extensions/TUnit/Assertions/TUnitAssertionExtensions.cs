using System.Runtime.CompilerServices;
using TUnit.Assertions.Core;

namespace Kurrent.Testing.TUnit.Assertions;

public static class TUnitAssertionExtensions {
    extension<TValue>(IAssertionSource<TValue> source) {
        public GuidAssertion<TValue> IsGuid() {
            source.Context.ExpressionBuilder.Append(".IsGuid()");
            return new GuidAssertion<TValue>(source.Context);
        }

        public ObjectGraphEquivalencyAssertion<TValue> IsDeepEquivalentTo(
            object? expected,
            [CallerArgumentExpression(nameof(expected))]
            string? expression = null
        ) {
            source.Context.ExpressionBuilder.Append($".IsDeepEquivalentTo({expression})");
            return new ObjectGraphEquivalencyAssertion<TValue>(source.Context, expected);
        }

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
