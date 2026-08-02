using Kurrent.Surge.Client;

// Homed in Kurrent.Projectors — the namespace of KurrentProjector and an ancestor of every backend's
// own namespace — so a caller reaches these without adding a using.
namespace Kurrent.Projectors;

/// <summary>
/// Connection configuration for <see cref="ProjectorOptions{TClient}"/>. These overloads live in the
/// implementation package rather than on the options type: a connection string and the concrete
/// <see cref="KurrentClientOptions"/> are gRPC-transport concepts, while the options record holds the
/// transport-agnostic <see cref="IKurrentClientOptions"/>.
/// </summary>
/// <remarks>
/// A projection needs its own overloads rather than inheriting the processor's:
/// <see cref="ProjectorOptions{TClient}"/> does not derive from <c>ProcessorOptions</c>, it is mapped
/// onto one.
/// </remarks>
[PublicAPI]
public static class ProjectorOptionsConnectionExtensions {
    extension<TClient>(ProjectorOptions<TClient> options) where TClient : class {
        /// <summary>
        /// Sets the KurrentDB connection string the projection reads from.
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
    }
}
