using System.Runtime.CompilerServices;
using Grpc.Core.Interceptors;

namespace Kurrent.Client;

/// <summary>
/// Implementation-side attachment of gRPC interceptors to the transport-agnostic client options.
/// The contract dropped the knob — <c>Grpc.Core</c> must not leak into the contract package — so
/// the state rides a weak table keyed by options instance, preserving the
/// <c>client.Interceptors = [...]</c> configuration surface for the implementation and tests.
/// </summary>
/// <remarks>
/// Instance-keyed: the attachment does not survive record <c>with</c>-cloning. The client wiring
/// mutates a single options instance in place, so this never bites in practice; it would only
/// matter for code that clones options after attaching interceptors.
/// </remarks>
static class KurrentClientOptionsInterceptors {
    static readonly ConditionalWeakTable<KurrentClientOptions, IReadOnlyList<Interceptor>> Attached = new();

    extension(KurrentClientOptions options) {
        internal IReadOnlyList<Interceptor> Interceptors {
            get => Attached.TryGetValue(options, out var interceptors) ? interceptors : [];
            set => Attached.AddOrUpdate(options, value);
        }
    }
}
