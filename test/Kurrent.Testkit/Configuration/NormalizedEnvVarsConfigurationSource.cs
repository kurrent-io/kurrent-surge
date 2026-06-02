using System.Collections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace Kurrent.Testkit.Configuration;

/// <summary>
/// Configuration source that treats single underscores as hierarchy separators,
/// allowing <c>MYAPP_SECTION_KEY</c> to bind as <c>Section:Key</c> after prefix stripping.
/// </summary>
/// <remarks>
/// <para>
/// The standard <see cref="EnvironmentVariablesConfigurationProvider"/> uses double underscores (<c>__</c>)
/// as hierarchy separators. This source uses single underscores (<c>_</c>) instead, which is more natural
/// for CI systems and shell scripts where double underscores are easy to mistype.
/// </para>
/// <para>
/// Environment variables that use double underscores after the prefix are skipped, since those are
/// handled by the standard provider. This avoids conflicts when both formats coexist. Precedence
/// between the two formats is controlled by the order in which configuration sources are added —
/// sources added later override earlier sources.
/// </para>
/// <para>
/// Example with prefix <c>KURRENTDB_</c>:
/// <list type="bullet">
///   <item><c>KURRENTDB_IMAGEREF</c> → <c>ImageRef</c></item>
///   <item><c>KURRENTDB_CONTAINER_REGISTRY</c> → <c>Container:Registry</c></item>
///   <item><c>KURRENTDB__CONTAINER__REGISTRY</c> → skipped (double underscore after prefix)</item>
/// </list>
/// </para>
/// </remarks>
public class NormalizedEnvVarsConfigurationSource : IConfigurationSource {
    /// <summary>
    /// Gets or sets the prefix that environment variable names must start with.
    /// The prefix is stripped from the resulting configuration keys.
    /// If <see langword="null"/>, all environment variables are included.
    /// </summary>
    public string? Prefix { get; init; }

    /// <summary>
    /// Builds the <see cref="NormalizedEnvVarsConfigurationProvider"/> for this source.
    /// </summary>
    /// <param name="builder">The <see cref="IConfigurationBuilder"/>.</param>
    /// <returns>A <see cref="NormalizedEnvVarsConfigurationProvider"/>.</returns>
    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new NormalizedEnvVarsConfigurationProvider(this);
}

/// <summary>
/// Configuration provider that loads environment variables using single underscores as hierarchy separators.
/// Strips the configured prefix, replaces <c>_</c> with <c>:</c> to produce standard configuration keys,
/// and skips variables that use double underscores (which the standard provider handles).
/// </summary>
sealed class NormalizedEnvVarsConfigurationProvider(
    NormalizedEnvVarsConfigurationSource source
) : ConfigurationProvider {
    /// <summary>
    /// Loads matching environment variables into the configuration data dictionary.
    /// </summary>
    public override void Load() {
        var prefix = (source.Prefix ?? "").Trim().AsSpan();
        var data   = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        
        Span<char> buffer = stackalloc char[256];

        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables()) {
            var key = ((string)entry.Key).AsSpan();

            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var remaining = key[prefix.Length..];

            // Skip variables that use double underscores anywhere after the prefix — both a
            // leading "__" (e.g. KURRENTDB__CONTAINER__REGISTRY) and an internal one
            // (e.g. KURRENTDB_LICENSING__LICENSE_KEY). Those follow the server's "__" hierarchy
            // convention and are left to the standard EnvironmentVariablesConfigurationProvider,
            // which avoids mangling them into junk keys like "LICENSING::LICENSE:KEY".
            if (remaining.Length == 0 || remaining.Length > buffer.Length ||
                remaining[0] == '_' || remaining.Contains("__", StringComparison.Ordinal))
                continue;

            var span = buffer[..remaining.Length];
            remaining.Replace(span, '_', ':');

            data[span.ToString()] = (string?)entry.Value;
        }

        Data = data;
    }
}

/// <summary>
/// Extension methods for adding <see cref="NormalizedEnvVarsConfigurationSource"/>
/// to an <see cref="IConfigurationBuilder"/>.
/// </summary>
public static class NormalizedEnvironmentVariablesExtensions {
    extension(IConfigurationBuilder builder) {
        /// <summary>
        /// Adds a configuration source that reads environment variables with the specified prefix,
        /// treating single underscores as hierarchy separators.
        /// </summary>
        /// <param name="prefix">
        /// Optional prefix that environment variable names must start with. The prefix is stripped
        /// from the resulting configuration keys. For example, with prefix <c>"KURRENTDB_"</c>,
        /// the variable <c>KURRENTDB_CONTAINER_TAG</c> produces key <c>Container:Tag</c>.
        /// If <see langword="null"/>, all environment variables are included.
        /// </param>
        /// <returns>The <see cref="IConfigurationBuilder"/>.</returns>
        public IConfigurationBuilder AddNormalizedEnvironmentVariables(string? prefix = null) =>
            builder.Add(new NormalizedEnvVarsConfigurationSource { Prefix = prefix });
    }
}
