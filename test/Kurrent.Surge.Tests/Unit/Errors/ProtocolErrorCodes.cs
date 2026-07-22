using IndexesErrors = KurrentDB.Protocol.V2.Indexes.Errors;
using RegistryErrors = KurrentDB.Protocol.V2.Registry.Errors;
using ServerErrors = Kurrent.Rpc;
using StreamsErrors = KurrentDB.Protocol.V2.Streams.Errors;

namespace Kurrent.Surge.Tests.Errors;

/// <summary>
/// The error-code vocabulary each protocol enum defines, derived from the compiled protos. This is the
/// fixed point the client-side vocabulary and dispatch table are held against — the protos are visible
/// here and nowhere else that can also see <see cref="KurrentErrorCodes"/>.
/// </summary>
/// <remarks>
/// Connectors has no entry: its protocol lives under <c>proto/kurrentdb/protocol/v0</c>, which the client
/// excludes from compilation, so there is no <c>ConnectorsError</c> enum to derive from.
/// </remarks>
static class ProtocolErrorCodes {
    public static readonly IReadOnlyList<ErrorCode> Streams  = CodesOf<StreamsErrors.StreamsError>();
    public static readonly IReadOnlyList<ErrorCode> Registry = CodesOf<RegistryErrors.RegistryError>();
    public static readonly IReadOnlyList<ErrorCode> Indexes  = CodesOf<IndexesErrors.IndexesError>();
    public static readonly IReadOnlyList<ErrorCode> Server   = CodesOf<ServerErrors.ServerError>();

    /// <summary>
    /// <c>Unspecified</c> is the proto3 zero value and never travels as an error, so it carries no code.
    /// </summary>
    static IReadOnlyList<ErrorCode> CodesOf<T>() where T : struct, Enum => [
        .. Enum.GetNames<T>()
            .Where(name => name != "Unspecified")
            .Select(ErrorCode.From)
    ];
}
