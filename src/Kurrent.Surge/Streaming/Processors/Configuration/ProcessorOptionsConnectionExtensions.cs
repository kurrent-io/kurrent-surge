using Kurrent.Surge.Client;
using Kurrent.Surge.Processors.Configuration;
using Kurrent.Surge.Schema.Naming;
using Microsoft.Extensions.Configuration;

// Home the connection sugar in Kurrent.Surge.Streaming — the namespace of KurrentProcessor, the primary
// entry point — so callers already reach it via the enclosing namespace (all Kurrent.Surge.Streaming.Tests
// files) or their existing `using Kurrent.Surge.Streaming;`, avoiding a using addition per call site.
namespace Kurrent.Surge.Streaming;

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
        /// run before or after the caller's own configuration without losing either.
        /// </remarks>
        /// <param name="configuration">The configuration root.</param>
        /// <param name="sectionName">
        /// The processor's section name. PascalCase, kebab-case and snake_case all reach the same
        /// section. When configuration sets no processor id, it is derived from this name.
        /// </param>
        /// <returns>The options, together with anything configuration could not supply.</returns>
        public ConfigurationReadResult<ProcessorOptions> FromConfiguration(IConfiguration configuration, string sectionName) {
            var result = KurrentSurgeConfigurationReader.ReadProcessorOptions(configuration, sectionName, options);

            if (string.IsNullOrEmpty(options.ProcessorId))
                options.ProcessorId = sectionName.ToKebabCase();

            return result;
        }
    }

}
