using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Kurrent.Surge;

/// <summary>
/// Reads settings off an <see cref="IConfigurationSection"/> one at a time, recording anything it
/// cannot use instead of throwing or ignoring it.
/// </summary>
/// <remarks>
/// <para>
/// Every getter takes the current value as its fallback, so an absent key leaves the setting exactly
/// as the caller left it. A key that is present but unusable records a problem and also falls back,
/// so one bad value never costs the rest of the section.
/// </para>
/// <para>
/// Convert through <see cref="IParsable{TSelf}"/> or a BCL <c>TryParse</c> only.
/// <c>TypeDescriptor.GetConverter</c> is annotated <c>RequiresUnreferencedCode</c> and breaks
/// trimming.
/// </para>
/// </remarks>
readonly struct ConfigReader(IConfigurationSection section, List<ConfigurationProblem> problems) {
    /// <summary>
    /// Converts a configuration value, for a type that cannot carry its own
    /// <see cref="IParsable{TSelf}"/> — a BCL type, for instance.
    /// </summary>
    internal delegate bool TryConvert<T>(string? text, out T value);

    public string Path   => section.Path;
    public bool   Exists => section.Exists();

    /// <summary>
    /// The section's own value. Non-null when configuration supplied a scalar rather than an object,
    /// which is how a caller writes the short form of a setting that also has a long form.
    /// </summary>
    public string? ScalarValue => section.Value;

    public ConfigReader Child(string name) => new(section.GetSection(name), problems);

    /// <summary>
    /// The elements of an array section, in configuration order.
    /// </summary>
    /// <remarks>
    /// Arrays arrive as children keyed by index — <c>Endpoints:0</c>, <c>Endpoints:1</c> — which is
    /// what makes them reachable from environment variables and command-line arguments as well as
    /// from JSON.
    /// </remarks>
    public IEnumerable<ConfigReader> Items() {
        foreach (var child in section.GetChildren())
            yield return new ConfigReader(child, problems);
    }

    #region ->> Values <<-

    public string Text(string key, string fallback) => section[key] ?? fallback;

    public string? TextOrNull(string key, string? fallback) => section[key] ?? fallback;

    public bool Flag(string key, bool fallback) =>
        Convert(key, fallback, bool.TryParse, "true or false");

    public int Number(string key, int fallback) =>
        Convert(key, fallback, TryParseInt, "a whole number");

    public double Number(string key, double fallback) =>
        Convert(key, fallback, TryParseDouble, "a number");

    public TimeSpan Duration(string key, TimeSpan fallback) =>
        Convert(key, fallback, DurationSyntax.TryParse, "milliseconds (30000) or a duration (00:00:30)");

    public T Choice<T>(string key, T fallback) where T : struct, Enum =>
        Convert(key, fallback, TryParseEnum, $"one of: {string.Join(", ", Enum.GetNames<T>())}");

    /// <summary>
    /// Reads any type that can parse itself from its text form.
    /// </summary>
    /// <remarks>
    /// <see cref="IParsable{TSelf}"/> is the trim-safe counterpart to a <c>TypeConverter</c>: the
    /// same canonical string, resolved statically rather than through <c>TypeDescriptor</c>.
    /// </remarks>
    public T Parsed<T>(string key, T fallback, string expected) where T : IParsable<T> =>
        Convert(key, fallback, TryParseSelf, expected);

    /// <summary>
    /// Reads a child key with a caller-supplied conversion.
    /// </summary>
    public T ValueOf<T>(string key, T fallback, TryConvert<T> tryConvert, string expected) =>
        Convert(key, fallback, tryConvert, expected);

    /// <summary>
    /// Converts this section's own value, for an element of an array of scalars.
    /// </summary>
    public T Self<T>(T fallback, TryConvert<T> tryConvert, string expected) {
        if (section.Value is not { } text)
            return fallback;

        if (tryConvert(text, out var value))
            return value;

        problems.Add(ConfigurationProblem.Error(
            section.Path,
            $"'{text}' is not valid here. Expected {expected}."));

        return fallback;
    }

    static bool TryParseInt(string? text, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    static bool TryParseDouble(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    static bool TryParseEnum<T>(string? text, out T value) where T : struct, Enum =>
        Enum.TryParse(text, ignoreCase: true, out value);

    static bool TryParseSelf<T>(string? text, out T value) where T : IParsable<T> {
        var parsed = T.TryParse(text, CultureInfo.InvariantCulture, out var result);
        value = result!;
        return parsed;
    }

    T Convert<T>(string key, T fallback, TryConvert<T> tryConvert, string expected) {
        if (section[key] is not { } text)
            return fallback;

        if (tryConvert(text, out var value))
            return value;

        problems.Add(ConfigurationProblem.Error(
            $"{section.Path}:{key}",
            $"'{text}' is not valid here. Expected {expected}."));

        return fallback;
    }

    #endregion // Values

    #region ->> Problems <<-

    /// <summary>
    /// Records a warning for every child key the caller did not claim.
    /// </summary>
    /// <remarks>
    /// Configuration ignores what it does not recognise, so a misspelled key otherwise leaves its
    /// setting silently at the default. Reporting the leftovers turns that into something visible.
    /// </remarks>
    public void ReportUnrecognised(params ReadOnlySpan<string> claimed) {
        foreach (var child in section.GetChildren()) {
            if (!IsClaimed(child.Key, claimed))
                problems.Add(ConfigurationProblem.UnrecognisedKey(child.Path));
        }

        return;

        static bool IsClaimed(string key, ReadOnlySpan<string> claimed) {
            foreach (var name in claimed) {
                if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Records a warning when a key names something deliberately not exposed to configuration.
    /// </summary>
    public void ReportNotConfigurable(string key) {
        if (section.GetSection(key).Exists())
            problems.Add(ConfigurationProblem.NotConfigurable($"{section.Path}:{key}"));
    }

    #endregion // Problems
}
