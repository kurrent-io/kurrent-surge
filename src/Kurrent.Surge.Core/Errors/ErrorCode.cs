using System.Text.RegularExpressions;
using Kurrent.Client;
using static System.StringComparison;

namespace Kurrent;

/// <summary>
/// Represents a normalized error code. Error codes must:
/// <list type="bullet">
/// <item><description>Be between 3 and 63 characters in length.</description></item>
/// <item><description>Match the UPPER_SNAKE_CASE pattern (only uppercase letters, digits, and underscores; must start with a letter and end with a letter or digit).</description></item>
/// </list>
/// </summary>
public readonly partial record struct ErrorCode {
    public static readonly ErrorCode None    = new("");
    public static readonly ErrorCode Unknown = new("UNKNOWN");

    const string Pattern = "^[A-Z][A-Z0-9_]{1,61}[A-Z0-9]$";

    [GeneratedRegex(Pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex Regex();

    ErrorCode(string value) => Value = value;

    public string Value { get; }

    /// <summary>
    /// Creates an <see cref="ErrorCode"/> from the given string value, normalizing it to UPPER_SNAKE_CASE
    /// and validating its format.
    /// </summary>
    public static ErrorCode From(string value) {
        var code = value
            .Trim()
            .Replace("Exception", "", OrdinalIgnoreCase)
            .Replace("ErrorCode", "", OrdinalIgnoreCase)
            .Replace("ErrorResult", "", OrdinalIgnoreCase)
            .Replace("ResultError", "", OrdinalIgnoreCase)
            .Replace("Error", "", OrdinalIgnoreCase)
            .Underscore()
            .ToUpperInvariant();

        // Regex now handles both pattern AND length validation
        if (!Regex().IsMatch(code))
            throw new InvalidErrorCodeException(
                $"Error code is invalid: {value}. " +
                $"It must be 3-63 characters and match UPPER_SNAKE_CASE pattern.");

        return new ErrorCode(code);
    }

    public static ErrorCode FromType(Type type) =>
        From(type.Name);

    public static ErrorCode FromType<T>() =>
        FromType(typeof(T));

    public static ErrorCode FromEx(Exception ex) =>
        FromType(ex.GetType());

    public static ErrorCode FromEnum<T>(T value) where T : struct, Enum =>
        From(Enum.GetName(value)!);

    public static ErrorCode FromEnum<T>(string value) where T : struct, Enum =>
        FromEnum(Enum.Parse<T>(value));

    public static ErrorCode FromEnum<T>(int value) where T : struct, Enum =>
        FromEnum<T>(value.ToString());

    public static implicit operator string(ErrorCode code)  => code.Value;
    public static implicit operator ErrorCode(string value) => From(value);
}

public sealed class InvalidErrorCodeException(string message) : ArgumentException(message);
