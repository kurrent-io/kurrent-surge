using Kurrent.Surge.Errors;

namespace Kurrent.Surge.Validation;

[PublicAPI]
public class ValidationException : KurrentException {
    public ValidationException(Errors.ErrorCode errorCode, string reason, params ValidationProblem[] errors) : base(errorCode, reason) {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(errors.Length, 0);
        Errors = errors;
    }

    public string ErrorCode => ErrorDetails.ErrorCode;

    public ValidationProblem[] Errors { get; }
}