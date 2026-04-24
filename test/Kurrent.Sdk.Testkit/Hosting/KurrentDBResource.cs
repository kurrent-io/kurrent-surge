namespace Aspire.Hosting.ApplicationModel;

public sealed class KurrentDBResource : ContainerResource, IResourceWithConnectionString {
    internal const string HttpEndpointName = "http";
    internal const int    DefaultHttpPort  = 2113;

    public KurrentDBResource(string name, KurrentDBTopology? topology = null) : base(name) {
        Topology = topology ?? new KurrentDBTopology();
    }

    public KurrentDBTopology Topology { get; }

    public bool   Secure   => Topology.Secure;
    public string UserName => Topology.UserName;
    public string Password => Topology.Password;

    internal string? CertsHostPath { get; set; }

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

/// <summary>
/// Immutable topology description consumed once by <c>AddKurrentDB</c>. Set
/// <see cref="NodeCount"/> &gt; 1 to spin up a cluster; set <see cref="Secure"/>
/// to enable TLS across every node.
/// </summary>
public sealed record KurrentDBTopology {
    public int    NodeCount { get; init; } = 1;
    public bool   Secure    { get; init; }
    public string UserName  { get; init; } = "admin";
    public string Password  { get; init; } = "changeit";
}
