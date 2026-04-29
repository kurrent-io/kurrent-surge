namespace Kurrent.Client.Exceptions;

internal static class ErrorCodeCache<TValue> {
    public static readonly ErrorCode Value =
        ErrorCode.From($"INVALID_{typeof(TValue).Name.Underscore().ToUpper()}");
}