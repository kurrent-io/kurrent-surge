// ReSharper disable CheckNamespace

#pragma warning disable ASPIREATS001 // AspireExport is experimental

namespace Aspire.Hosting.ApplicationModel;

[AspireExport(ExposeProperties = true)]
public sealed class KurrentDBResource : ContainerResource, IResourceWithConnectionString {
    public const string DefaultResourceName = "kurrentdb";
    public const string DefaultUserName     = "admin";
    public const string DefaultPassword     = "changeit";
    public const int    DefaultHttpPort     = 2113;

    public KurrentDBResource(string name, string? userName = null, string? password = null) : base(name) {
        PrimaryEndpoint   = new(this, "http");
        IsReadOnlyReplica = name.Contains("ro", StringComparison.OrdinalIgnoreCase);
        UserName          = userName ?? DefaultUserName;
        Password          = password ?? DefaultPassword;
    }

    public string? CertsHostPath { get; internal set; }

    public bool IsReadOnlyReplica { get; }

    public string UserName { get; }

    public string Password { get; }

    public EndpointReference PrimaryEndpoint { get; }

    public EndpointReferenceExpression Host =>
        PrimaryEndpoint.Property(EndpointProperty.Host);

    public EndpointReferenceExpression Port =>
        PrimaryEndpoint.Property(EndpointProperty.Port);

    public ReferenceExpression UserNameExpression => ReferenceExpression.Create($"{UserName}");

    public ReferenceExpression PasswordExpression => ReferenceExpression.Create($"{Password}");

    public ReferenceExpression ConnectionStringExpression {
        get {
            var host = PrimaryEndpoint.Property(EndpointProperty.Host);
            var port = PrimaryEndpoint.Property(EndpointProperty.Port);
            var tls  = PrimaryEndpoint.GetTlsValue(
                enabledValue:  ReferenceExpression.Create($"tls=true&tlsVerifyCert=false"),
                disabledValue: ReferenceExpression.Create($"tls=false"));

            return ReferenceExpression.Create($"kurrentdb://{UserNameExpression}:{PasswordExpression}@{host}:{port}?{tls}");
        }
    }

    IEnumerable<KeyValuePair<string, ReferenceExpression>> IResourceWithConnectionString.GetConnectionProperties() {
        yield return new("Host", ReferenceExpression.Create($"{Host}"));
        yield return new("Port", ReferenceExpression.Create($"{Port}"));
        yield return new("UserName", UserNameExpression);
        yield return new("Password", PasswordExpression);
        yield return new("Uri", ConnectionStringExpression);
    }
}
