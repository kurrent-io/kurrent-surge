using Kurrent.Surge.Client;
using Microsoft.Extensions.Configuration;

namespace Kurrent.Surge;

/// <summary>
/// Loads <see cref="KurrentClientOptions"/> from configuration.
/// </summary>
[PublicAPI]
public static class KurrentClientOptionsConfigurationExtensions {
    /// <param name="options">The options to fill.</param>
    extension(KurrentClientOptions options) {
        /// <summary>
        /// Loads client settings from <c>Kurrent:Surge</c>.
        /// </summary>
        /// <remarks>
        /// Settings already assigned survive when configuration does not mention them, so this can
        /// run before or after the caller's own configuration without losing either.
        /// </remarks>
        /// <param name="configuration">The configuration root.</param>
        /// <returns>The options, together with anything configuration could not supply.</returns>
        public ConfigurationReadResult<KurrentClientOptions> FromConfiguration(IConfiguration configuration) =>
            KurrentSurgeConfigurationReader.ReadClientOptions(configuration, options);
    }
}
