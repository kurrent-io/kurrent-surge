using System.Collections;
using Microsoft.Extensions.Configuration;

namespace Kurrent.Testkit.Configuration;

/// <summary>
/// Configuration source for normalized environment variables that converts single underscores to double underscores.
/// This allows environment variables like MYAPP_SECTION_KEY to be read as MyApp:Section:Key configuration paths.
/// </summary>
public class NormalizedEnvironmentVariablesConfigurationSource : IConfigurationSource {
    /// <summary>
    /// Gets or sets the prefix that environment variable names must start with.
    /// The prefix will be removed from the resulting configuration keys.
    /// If null, all environment variables will be normalized.
    /// </summary>
    public string? Prefix { get; set; }

    /// <summary>
    /// Builds the <see cref="NormalizedEnvironmentVariablesConfigurationProvider"/> for this source.
    /// </summary>
    /// <param name="builder">The <see cref="IConfigurationBuilder"/>.</param>
    /// <returns>A <see cref="NormalizedEnvironmentVariablesConfigurationProvider"/></returns>
    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new NormalizedEnvironmentVariablesConfigurationProvider(this);
}

/// <summary>
/// Configuration provider that loads normalized environment variables.
/// Normalizes single underscores (_) to double underscores (__) to support both formats.
/// When conflicts occur, double-underscore format takes precedence.
/// </summary>
public class NormalizedEnvironmentVariablesConfigurationProvider(
    NormalizedEnvironmentVariablesConfigurationSource source
) : ConfigurationProvider {
    /// <summary>
    /// Loads environment variables, normalizes keys, and populates the Data dictionary.
    /// </summary>
    public override void Load() {
        var data = Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .Where(MatchesPrefix)
            .Select(ToKeyValue)
            .Select(NormalizeKey)
            .GroupBy(x => x.NormalizedKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.MaxBy(x => x.HasDoubleUnderscore)!.Value,
                StringComparer.OrdinalIgnoreCase
            );

        Data = data;
    }

    bool MatchesPrefix(DictionaryEntry entry) =>
        source.Prefix is null ||
        ((string)entry.Key).StartsWith(source.Prefix, StringComparison.OrdinalIgnoreCase);

    static (string Key, string? Value) ToKeyValue(DictionaryEntry entry) => ((string)entry.Key, entry.Value?.ToString());

    (string NormalizedKey, string? Value, bool HasDoubleUnderscore) NormalizeKey(
        (string Key, string? Value) entry
    ) {
        var normalizedKey = entry.Key.Replace("_", "__");

        if (source.Prefix is not null)
            normalizedKey = normalizedKey[source.Prefix.Length..];

        return (
            NormalizedKey: normalizedKey,
            entry.Value,
            HasDoubleUnderscore: entry.Key.Contains("__")
        );
    }
}

/// <summary>
/// Extension methods for adding <see cref="NormalizedEnvironmentVariablesConfigurationProvider"/>.
/// </summary>
public static class NormalizedEnvironmentVariablesExtensions {
    /// <param name="builder">The <see cref="IConfigurationBuilder"/> to add to.</param>
    extension(IConfigurationBuilder builder) {
        /// <summary>
        /// Adds normalized environment variables as a configuration source.
        /// Converts single underscores (_) to double underscores (__) to support both formats.
        /// </summary>
        /// <returns>The <see cref="IConfigurationBuilder"/>.</returns>
        /// <remarks>
        /// <para>
        /// This allows environment variables like:
        /// - MYAPP_SECTION_KEY or
        /// - MYAPP__SECTION__KEY
        ///
        /// Both map to configuration path: MyApp:Section:Key
        /// </para>
        /// <para>
        /// When both formats exist for the same key, the double-underscore format takes precedence.
        /// </para>
        /// <para>
        /// Precedence is controlled by the order in which configuration sources are added.
        /// Sources added later override earlier sources.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var builder = WebApplication.CreateBuilder(args);
        /// builder.Configuration.AddNormalizedEnvironmentVariables();
        /// </code>
        /// </example>
        public IConfigurationBuilder AddNormalizedEnvironmentVariables() =>
            builder.AddNormalizedEnvironmentVariables(null);

        /// <summary>
        /// Adds normalized environment variables with a specified prefix as a configuration source.
        /// Converts single underscores (_) to double underscores (__) to support both formats.
        /// The prefix will be removed from the resulting configuration keys.
        /// </summary>
        /// <param name="prefix">
        /// The prefix that environment variable names must start with. The prefix will be removed.
        /// For example, with prefix "MYAPP_", the variable "MYAPP_SECTION_KEY" becomes "Section:Key".
        /// </param>
        /// <returns>The <see cref="IConfigurationBuilder"/>.</returns>
        /// <remarks>
        /// <para>
        /// This allows environment variables like:
        /// - MYAPP_SECTION_KEY or
        /// - MYAPP__SECTION__KEY
        ///
        /// Both map to configuration path: Section:Key (prefix removed)
        /// </para>
        /// <para>
        /// When both formats exist for the same key, the double-underscore format takes precedence.
        /// </para>
        /// <para>
        /// Precedence is controlled by the order in which configuration sources are added.
        /// Sources added later override earlier sources.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var builder = WebApplication.CreateBuilder(args);
        /// builder.Configuration.AddNormalizedEnvironmentVariables("TESTCONTAINERS_");
        ///
        /// // Environment variable: TESTCONTAINERS_KURRENTDB_ENABLED=true
        /// // Configuration path: Testcontainers:KurrentDb:Enabled
        ///
        /// var enabled = builder.Configuration.GetValue&lt;bool&gt;("Testcontainers:KurrentDb:Enabled");
        /// </code>
        /// </example>
        public IConfigurationBuilder AddNormalizedEnvironmentVariables(string? prefix) =>
            builder.Add(new NormalizedEnvironmentVariablesConfigurationSource { Prefix = prefix });
    }
}
