using TUnit.Assertions.Core;

namespace Kurrent.Testing.Assertions;

public class GuidAssertion<TValue> : Assertion<TValue> {
    public GuidAssertion(AssertionContext<TValue> context) : base(context) { }

    protected override Task<AssertionResult> CheckAsync(EvaluationMetadata<TValue> metadata) {
        if (metadata.Exception is not null)
            return Task.FromResult(
                AssertionResult.Failed($"threw {metadata.Exception.GetType().Name}: {metadata.Exception.Message}")
            );

        var value = metadata.Value;

        if (value is null)
            return Task.FromResult(AssertionResult.Failed("was null"));

        var str = value is string s ? s : value.ToString();

        return Task.FromResult(
            Guid.TryParse(str, out _)
                ? AssertionResult.Passed
                : AssertionResult.Failed($"'{str}' is not a valid GUID")
        );
    }

    protected override string GetExpectation() => "to be a valid GUID";
}
