using System.Runtime.CompilerServices;
using TUnit.Assertions.Core;

namespace Kurrent.Testing.Assertions;

public static class KurrentAssertionExtensions {
    public static GuidAssertion<TValue> IsGuid<TValue>(this IAssertionSource<TValue> source) {
        source.Context.ExpressionBuilder.Append(".IsGuid()");
        return new GuidAssertion<TValue>(source.Context);
    }
}
