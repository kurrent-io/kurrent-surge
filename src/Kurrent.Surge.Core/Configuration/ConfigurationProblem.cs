namespace Kurrent.Surge;

/// <summary>
/// How much a <see cref="ConfigurationProblem"/> matters.
/// </summary>
[PublicAPI]
public enum ConfigurationProblemSeverity {
    /// <summary>
    /// The setting was ignored. Reading continued and the value kept its previous state.
    /// </summary>
    Warning,

    /// <summary>
    /// The setting could not be applied and the configuration is not usable as written.
    /// </summary>
    Error
}

/// <summary>
/// One thing that went wrong while reading configuration, tied to the key that caused it.
/// </summary>
/// <param name="Path">
/// Full configuration path, for example <c>Kurrent:Surge:Processors:Fraud:Checkpoint:Interval</c>.
/// </param>
/// <param name="Message">What was wrong, and what was expected instead.</param>
/// <param name="Severity">Whether reading continued past it.</param>
[PublicAPI]
public readonly record struct ConfigurationProblem(
    string Path,
    string Message,
    ConfigurationProblemSeverity Severity
) {
    /// <summary>
    /// A setting that was read but could not be applied.
    /// </summary>
    public static ConfigurationProblem Error(string path, string message) =>
        new(path, message, ConfigurationProblemSeverity.Error);

    /// <summary>
    /// A setting that was ignored.
    /// </summary>
    public static ConfigurationProblem Warning(string path, string message) =>
        new(path, message, ConfigurationProblemSeverity.Warning);

    /// <summary>
    /// A key nobody claimed. Almost always a typo, since configuration ignores what it does not
    /// recognise and would otherwise leave the setting silently at its default.
    /// </summary>
    public static ConfigurationProblem UnrecognisedKey(string path) =>
        Warning(path, "Unrecognised setting. Check the spelling — it had no effect.");

    /// <summary>
    /// A key naming something the SDK deliberately does not expose to configuration.
    /// </summary>
    public static ConfigurationProblem NotConfigurable(string path) =>
        Warning(path, "This setting is not configurable and had no effect. Set it in code.");

    public override string ToString() => $"{Severity}: {Path} — {Message}";
}
