namespace Aspire.Hosting.ApplicationModel;

public class KurrentDBResource(string name, bool secure = false)
    : ContainerResource(name), IResourceWithConnectionString {
    internal const string HttpEndpointName = "http";
    internal const int    DefaultHttpPort  = 2113;

    EndpointReference? _primaryEndpoint;

    public bool Secure { get; internal set; } = secure;

    public EndpointReference PrimaryEndpoint => _primaryEndpoint ??= new EndpointReference(this, HttpEndpointName);

    public EndpointReferenceExpression Host => PrimaryEndpoint.Property(EndpointProperty.Host);

    public EndpointReferenceExpression Port => PrimaryEndpoint.Property(EndpointProperty.Port);

    public ReferenceExpression UriExpression => ConnectionStringExpression;

    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create(
            $"kurrentdb://{PrimaryEndpoint.Property(EndpointProperty.Host)}:{PrimaryEndpoint.Property(EndpointProperty.Port)}?tls={Secure.ToString().ToLowerInvariant()}"
        );

    IEnumerable<KeyValuePair<string, ReferenceExpression>> IResourceWithConnectionString.GetConnectionProperties() {
        yield return new KeyValuePair<string, ReferenceExpression>("Host", ReferenceExpression.Create($"{Host}"));
        yield return new KeyValuePair<string, ReferenceExpression>("Port", ReferenceExpression.Create($"{Port}"));
        yield return new KeyValuePair<string, ReferenceExpression>("Uri", UriExpression);
    }
}
