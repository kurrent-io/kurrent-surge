using Kurrent.Client.Exceptions;

namespace Kurrent.Validation;

[PublicAPI]
public class ValidationException : KurrentException {
    public ValidationException(ErrorCode errorCode, string message, params ValidationProblem[] errors) : base(errorCode, message) {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(errors.Length, 0);

        ErrorCode = errorCode;
        Errors    = errors;
    }

    public string ErrorCode { get; }

    public ValidationProblem[] Errors { get; }
}