namespace Kurrent.Toolkit.Http;

static class HttpSecurity {
    /// <summary>
    /// Enables HTTP/2 over unencrypted connections.
    /// </summary>
    /// <remarks>
    /// By default, .NET requires TLS for HTTP/2 connections to align with browser security standards.
    /// However, in many scenarios services communicate over plain HTTP/2 without TLS encryption.
    /// <para/>
    /// This switch is necessary when:
    /// <para/>- Connecting to development/test services that don't have TLS certificates configured
    /// <para/>- Communicating with internal microservices behind a secure network perimeter
    /// <para/>- Working with gRPC services that use plain HTTP/2 (common in non-.NET ecosystems)
    /// <para/>- Developing on macOS where ASP.NET Core's Kestrel doesn't support HTTP/2 with TLS (pre-.NET 8)
    /// <para/>
    /// Without this switch, attempting to use HTTP/2 over plain HTTP will result in:
    /// <para/>- HttpRequestException: "The SSL connection could not be established"
    /// <para/>- Or the client falling back to HTTP/1.1, which may not be supported by the server
    /// <para/>
    /// IMPORTANT: This switch MUST be set before creating the HttpClient/HttpMessageHandler.
    /// Setting it afterward has no effect on already-created handlers.
    /// <para/>
    /// SECURITY NOTE: Only use unencrypted HTTP/2 in trusted environments. Production services
    /// exposed to the internet should always use TLS encryption.
    /// </remarks>
    public static void EnableHttp2UnencryptedSupport() =>
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
}
