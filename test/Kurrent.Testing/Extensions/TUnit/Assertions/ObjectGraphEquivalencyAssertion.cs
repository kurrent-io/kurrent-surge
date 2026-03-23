using TUnit.Assertions.Core;

namespace Kurrent.Testing.TUnit.Assertions;

public class ObjectGraphEquivalencyAssertion<TValue>(AssertionContext<TValue> context, object? expected, EquivalencyConfiguration? config = null) : Assertion<TValue>(context) {
    protected override Task<AssertionResult> CheckAsync(EvaluationMetadata<TValue> metadata) {
        if (metadata.Exception is { } exception)
            return Task.FromResult(AssertionResult.Failed($"threw {exception.GetType().Name}: {exception.Message}"));

        try {
            ObjectGraphComparer.AssertEquivalent(metadata.Value, expected, config);
            return Task.FromResult(AssertionResult.Passed);
        }
        catch (ObjectGraphComparisonException ex) {
            return Task.FromResult(AssertionResult.Failed(ex.Message));
        }
    }

    protected override string GetExpectation() => $"to be equivalent to {expected}";
}
