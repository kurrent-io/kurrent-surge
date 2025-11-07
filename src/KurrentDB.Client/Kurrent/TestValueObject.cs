namespace Kurrent;

/// <summary>
/// Test value object - Simple case (no validation).
/// </summary>
[ValueObject<string>]
public readonly partial record struct SimpleTestValue;

/// <summary>
/// Test value object - With validation.
/// </summary>
[ValueObject<string>]
readonly partial record struct ValidatedTestValue {
    static ValidatedTestValue() {
        OnValidate = value => string.IsNullOrWhiteSpace(value)
            ? Validation.ValidationError.FromProblem("EMPTY", "Value cannot be empty")
            : default(Success);
    }
}

/// <summary>
/// Test value object - Numeric type.
/// </summary>
[ValueObject<int>]
readonly partial record struct NumericTestValue {
    static NumericTestValue() {
        OnValidate = value => value < 0
            ? Validation.ValidationError.FromProblem("NEGATIVE", "Value must be non-negative")
            : default(Success);
    }
}

[ValueObject<ErrorCode>]
readonly partial record struct FuckIt {
    static FuckIt() => OnValidate = value => value == ErrorCode.None
        ? Validation.ValidationError.FromProblem("NEGATIVE", "Fuck it")
        : default(Success);
}
