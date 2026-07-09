// ReSharper disable CheckNamespace

namespace Kurrent.Surge.Client.Exceptions;

/// <summary>
/// Thrown when <c>AddKurrentClient()</c> is called with no usable configuration source —
/// no <see cref="KurrentClientOptions"/> registered, no connection string at
/// <c>ConnectionStrings:KurrentDB</c>, and no connection string passed directly.
/// </summary>
[PublicAPI]
public sealed class KurrentClientNotConfiguredException()
    : KurrentException(
        ErrorCode.FromType<KurrentClientNotConfiguredException>(),
        "KurrentDB client is not configured. "
      + "Register a KurrentClientOptions instance, "
      + "set a connection string at 'ConnectionStrings:KurrentDB' in your IConfiguration, "
      + "or pass a connection string to AddKurrentClient(...).");
