namespace Aspire.Hosting.ApplicationModel;

public class KurrentDBResource(string name, bool secure = false)
    : ContainerResource(name), IResourceWithConnectionString {
    internal const string HttpEndpointName = "http";
    internal const int    DefaultHttpPort  = 2113;

    public bool Secure { get; internal set; } = secure;

    public EndpointReference PrimaryEndpoint => field ??= new EndpointReference(this, HttpEndpointName);

    public EndpointReferenceExpression Host => PrimaryEndpoint.Property(EndpointProperty.Host);

    public EndpointReferenceExpression Port => PrimaryEndpoint.Property(EndpointProperty.Port);

    public ReferenceExpression UriExpression => ConnectionStringExpression;

    public ReferenceExpression ConnectionStringExpression => Secure
        ? ReferenceExpression.Create(
            $"kurrentdb://{PrimaryEndpoint.Property(EndpointProperty.Host)}:{PrimaryEndpoint.Property(EndpointProperty.Port)}?tls=true&tlsVerifyCert=false")
        : ReferenceExpression.Create(
            $"kurrentdb://{PrimaryEndpoint.Property(EndpointProperty.Host)}:{PrimaryEndpoint.Property(EndpointProperty.Port)}?tls=false");

    IEnumerable<KeyValuePair<string, ReferenceExpression>> IResourceWithConnectionString.GetConnectionProperties() {
        yield return new KeyValuePair<string, ReferenceExpression>("Host", ReferenceExpression.Create($"{Host}"));
        yield return new KeyValuePair<string, ReferenceExpression>("Port", ReferenceExpression.Create($"{Port}"));
        yield return new KeyValuePair<string, ReferenceExpression>("Uri", UriExpression);
    }
}
