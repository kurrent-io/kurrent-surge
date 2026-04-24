namespace Aspire.Hosting.ApplicationModel;

public sealed class KurrentDBResource(string name, KurrentDBTopology? topology = null) : ContainerResource(name), IResourceWithConnectionString {
    internal const string HttpEndpointName = "http";
    internal const int    DefaultHttpPort  = 2113;

    const string DefaultUserName = "admin";
    const string DefaultPassword = "changeit";

    KurrentDBTopology Topology { get; } = topology ?? KurrentDBTopology.Insecure;

    public bool Secure => Topology.Tls;

    internal string? CertsHostPath { get; set; }

    public EndpointReference PrimaryEndpoint => field ??= new EndpointReference(this, HttpEndpointName);

    public ReferenceExpression ConnectionStringExpression              => BuildConnectionString(authenticated: false);
    public ReferenceExpression AuthenticatedConnectionStringExpression => BuildConnectionString(authenticated: true);

    ReferenceExpression BuildConnectionString(bool authenticated) {
        var host = PrimaryEndpoint.Property(EndpointProperty.Host);
        var port = PrimaryEndpoint.Property(EndpointProperty.Port);
        var tls  = Secure ? "tls=true&tlsVerifyCert=false" : "tls=false";

        return authenticated
            ? ReferenceExpression.Create($"kurrentdb://{DefaultUserName}:{DefaultPassword}@{host}:{port}?{tls}")
            : ReferenceExpression.Create($"kurrentdb://{host}:{port}?{tls}");
    }
}

public sealed record KurrentDBTopology(int NodeCount, bool Tls) {
    public static KurrentDBTopology Insecure { get; } = new(1, false);
    public static KurrentDBTopology Secure   { get; } = new(1, true);
    public static KurrentDBTopology Cluster  { get; } = new(3, true);
}
