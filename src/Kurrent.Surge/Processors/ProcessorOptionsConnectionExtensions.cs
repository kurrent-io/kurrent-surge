using Kurrent.Surge.Client;
using Kurrent.Surge.Processors.Configuration;
using Microsoft.Extensions.Configuration;

namespace Kurrent.Surge.Processors;

/// <summary>
/// Connection configuration for <see cref="ProcessorOptions"/>. These overloads live in the
/// implementation package rather than on the options type itself: a connection string and the
/// concrete <see cref="KurrentClientOptions"/> are gRPC-transport concepts, while
/// <see cref="ProcessorOptions"/> holds the transport-agnostic <see cref="IKurrentClientOptions"/>.
/// </summary>
[PublicAPI]
public static class ProcessorOptionsConnectionExtensions {
    extension(ProcessorOptions options) {
        /// <summary>
        /// Sets the KurrentDB connection string.
        /// </summary>
        public void Connection(string connectionString) =>
            options.ClientOptions = KurrentClientOptions.Parse(connectionString);

        /// <summary>
        /// Sets a connection string and applies additional options configuration.
        /// </summary>
        public void Connection(string connectionString, Action<KurrentClientOptions> configure) {
            var clientOptions = KurrentClientOptions.Parse(connectionString);
            configure(clientOptions);
            options.ClientOptions = clientOptions;
        }

        /// <summary>
        /// Sets the client options directly.
        /// </summary>
        public void Connection(KurrentClientOptions clientOptions) =>
            options.ClientOptions = clientOptions;

        /// <summary>
        /// Loads options from <c>Kurrent:Surge:Processors:{sectionName}</c>.
        /// </summary>
        /// <remarks>
        /// Settings already assigned survive when configuration does not mention them, so this can
        /// run before or after the caller's own configuration without losing either. The processor
        /// id is never read: a <c>ProcessorId</c> key is reported as an unrecognised setting.
        /// </remarks>
        /// <param name="configuration">The configuration root.</param>
        /// <param name="sectionName">
        /// The processor's section name. PascalCase, kebab-case and snake_case all reach the same
        /// section.
        /// </param>
        /// <returns>The options, together with anything configuration could not supply.</returns>
        public ConfigurationReadResult<ProcessorOptions> FromConfiguration(IConfiguration configuration, string sectionName) =>
            KurrentSurgeConfigurationReader.ReadProcessorOptions(configuration, sectionName, options);
    }

}
