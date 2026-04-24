namespace Aspire.Hosting.ApplicationModel;

public sealed class KurrentDBResource(string name, bool secure = false) : ContainerResource(name), IResourceWithConnectionString {
    internal const string HttpEndpointName  = "http";
    internal const int    DefaultHttpPort   = 2113;
    internal const string DefaultUserName   = "admin";
    internal const string DefaultPassword   = "changeit";

    public bool    Secure        { get; internal set; } = secure;
    public bool    IsPrimary     { get; internal set; } = true;
    public string  UserName      { get; set; }          = DefaultUserName;
    public string  Password      { get; set; }          = DefaultPassword;
    public string? CertsHostPath { get; internal set; }

    /// <summary>
    /// Shared by-reference list of every node in this cluster (primary first). <c>null</c>
    /// for standalone single-node resources.
    /// </summary>
    internal List<KurrentDBResource>? Cluster { get; set; }

    public EndpointReference PrimaryEndpoint => field ??= new EndpointReference(this, HttpEndpointName);

    public ReferenceExpression ConnectionStringExpression              => BuildConnectionString(authenticated: false);
    public ReferenceExpression AuthenticatedConnectionStringExpression => BuildConnectionString(authenticated: true);

    ReferenceExpression BuildConnectionString(bool authenticated) {
        var host = PrimaryEndpoint.Property(EndpointProperty.Host);
        var port = PrimaryEndpoint.Property(EndpointProperty.Port);
        var tls  = Secure ? "tls=true&tlsVerifyCert=false" : "tls=false";

        return authenticated
            ? ReferenceExpression.Create($"kurrentdb://{UserName}:{Password}@{host}:{port}?{tls}")
            : ReferenceExpression.Create($"kurrentdb://{host}:{port}?{tls}");
    }
}
