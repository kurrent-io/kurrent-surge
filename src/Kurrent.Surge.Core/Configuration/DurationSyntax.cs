using System.Globalization;

namespace Kurrent.Surge;

/// <summary>
/// The text form of a duration in configuration: whole milliseconds, or any standard
/// <see cref="TimeSpan"/> literal.
/// </summary>
/// <remarks>
/// <c>30000</c> is 30 seconds. <c>00:00:30</c> is the same 30 seconds. Both
/// <c>ConfigTimeSpanConverter</c> and <see cref="ConfigReader"/> must route here: a bare number is a
/// valid <see cref="TimeSpan"/> literal meaning days, so any path that parses it directly turns 30
/// seconds into 30000 days.
/// </remarks>
static class DurationSyntax {
    public static bool TryParse(string? text, out TimeSpan value) {
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds)) {
            value = TimeSpan.FromMilliseconds(milliseconds);
            return true;
        }

        return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Renders a duration back as whole milliseconds, the form this syntax prefers.
    /// </summary>
    public static string ToText(TimeSpan value) =>
        ((long)value.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
}
