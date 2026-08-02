using Kurrent.Surge.Client;
using Kurrent.Processors.Configuration;
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
        /// Loads options from an <see cref="IConfiguration"/> section.
        /// Section path: <c>Kurrent:Processors:{sectionName}</c>.
        /// Values loaded from config can be overridden by subsequent property assignments.
        /// </summary>
        /// <param name="configuration">The configuration root.</param>
        /// <param name="sectionName">
        /// The section name under <c>Kurrent:Processors</c>.
        /// If ProcessorId is not set in config, it will be auto-generated from this name.
        /// </param>
        public void FromConfiguration(IConfiguration configuration, string sectionName) {
            var sectionPath = $"Kurrent:Processors:{sectionName}";
            var section     = configuration.GetSection(sectionPath);

            if (!section.Exists()) return;

            section.Bind(options, o => o.BindNonPublicProperties = true);

            // Auto-generate ProcessorId from section name if not set in config
            if (string.IsNullOrEmpty(options.ProcessorId))
                options.ProcessorId = sectionName.ToKebabCase();

            // Check for connection string: ConnectionStrings:{sectionName} or ConnectionStrings:Default
            var connectionString = configuration.GetConnectionString(sectionName)
                                ?? configuration.GetConnectionString("Default");

            if (connectionString is not null)
                options.Connection(connectionString);
        }
    }

}
