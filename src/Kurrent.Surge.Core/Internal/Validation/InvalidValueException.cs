using Kurrent.Surge.Validation;

namespace Kurrent.Surge.Client.Exceptions;

public sealed class InvalidValueException<TValue>(object? value, string reason, params ValidationProblem[] problems) 
    : ValidationException(ErrorCodeCache<TValue>.Value, FormatReason(value, reason), problems) {
    public object? Value { get; } = value;

    static string FormatReason(object? value, string reason) {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return $"{typeof(TValue).Name} {reason}. Got: {value ?? "<null>"}";
    }
}