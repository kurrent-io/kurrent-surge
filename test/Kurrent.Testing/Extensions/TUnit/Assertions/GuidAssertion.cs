using TUnit.Assertions.Core;

namespace Kurrent.Testing.TUnit.Assertions;

public class GuidAssertion<TValue>(AssertionContext<TValue> context) : Assertion<TValue>(context) {
    protected override Task<AssertionResult> CheckAsync(EvaluationMetadata<TValue> metadata) {
        return Task.FromResult(Check(metadata));

        static AssertionResult Check(EvaluationMetadata<TValue> metadata) {
            if (metadata.Exception is { })
                return AssertionResult.Failed($"threw {metadata.Exception.GetType().Name}: {metadata.Exception.Message}");

            if (metadata.Value is Guid)
                return AssertionResult.Passed;

            if (metadata.Value is null)
                return AssertionResult.Failed("was null");

            var value = metadata.Value.ToString();
            return Guid.TryParse(value, out _)
                ? AssertionResult.Passed
                : AssertionResult.Failed($"'{value}' is not a valid GUID");
        }
    }

    protected override string GetExpectation() => "to be a valid GUID";
}
