using Kurrent.Toolkit;
namespace Kurrent.Surge.Validation;

static class ErrorCodeCache<TValue> {
    public static readonly Errors.ErrorCode Value =
        Errors.ErrorCode.From($"INVALID_{typeof(TValue).Name.Underscore().ToUpper()}");
}