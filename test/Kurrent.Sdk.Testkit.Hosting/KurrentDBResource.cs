// ReSharper disable CheckNamespace

#pragma warning disable ASPIREATS001 // AspireExport is experimental

namespace Aspire.Hosting.ApplicationModel;

[AspireExport(ExposeProperties = true)]
public sealed class KurrentDBResource : ContainerResource, IResourceWithConnectionString, IResourceWithParent<KurrentDBServerResource> {
    internal KurrentDBResource(string name, KurrentDBServerResource parent, int priority = 0) : base(name) {
        Parent            = parent;
        PrimaryEndpoint   = new(this, "http");
        IsReadOnlyReplica = name.Contains("-ro", StringComparison.OrdinalIgnoreCase);
        Priority          = priority;
    }

    public KurrentDBServerResource Parent { get; }

    public bool IsReadOnlyReplica { get; }

    public int Priority { get; }

    public EndpointReference PrimaryEndpoint { get; }

    public EndpointReferenceExpression Host =>
        PrimaryEndpoint.Property(EndpointProperty.Host);

    public EndpointReferenceExpression Port =>
        PrimaryEndpoint.Property(EndpointProperty.Port);

    public ReferenceExpression ConnectionStringExpression =>
        Parent.BuildConnectionString(this);

    public void ConfigureEnvironment(EnvironmentCallbackContext ctx) {
        var variables = Parent.TlsEnabled
            ? NodeConfiguration.Secure
            : NodeConfiguration.Insecure;
        
        foreach (var (key, value) in variables) {
            if (value is not null)
                ctx.EnvironmentVariables[key] = value;
            else
                ctx.EnvironmentVariables.Remove(key);
        }

        ctx.EnvironmentVariables["KURRENTDB_CLUSTER_SIZE"]                     = Parent.ClusterSize.ToString();
        ctx.EnvironmentVariables["KURRENTDB_DISCOVER_VIA_DNS"]                 = "false";
        ctx.EnvironmentVariables["KURRENTDB_GOSSIP_SEED"]                      = Parent.GossipSeeds;
        ctx.EnvironmentVariables["KURRENTDB_NODE_HOST_ADVERTISE_AS"]           = Name;
        ctx.EnvironmentVariables["KURRENTDB_REPLICATION_HOST_ADVERTISE_AS"]    = Name;
        ctx.EnvironmentVariables["KURRENTDB_ADVERTISE_HOST_TO_CLIENT_AS"]      = "localhost";
        ctx.EnvironmentVariables["KURRENTDB_ADVERTISE_NODE_PORT_TO_CLIENT_AS"] = ReferenceExpression.Create($"{PrimaryEndpoint.Property(EndpointProperty.Port)}");
        ctx.EnvironmentVariables["KURRENTDB_READ_ONLY_REPLICA"]                = IsReadOnlyReplica.ToString().ToLowerInvariant();

        if (Priority != 0)
            ctx.EnvironmentVariables["KURRENTDB_NODE_PRIORITY"] = Priority.ToString();

        var otel = Parent.OpenTelemetry;

        if (otel.IsEnabled) {
            ctx.EnvironmentVariables["KURRENTDB_OPEN_TELEMETRY__OTLP__ENDPOINT"]   = otel.Endpoint;
            ctx.EnvironmentVariables["KURRENTDB_OPEN_TELEMETRY__LOGS__ENABLED"]    = otel.LogsEnabled.ToString().ToLowerInvariant();
            ctx.EnvironmentVariables["KURRENTDB_OPEN_TELEMETRY__METRICS__ENABLED"] = otel.MetricsEnabled.ToString().ToLowerInvariant();
            ctx.EnvironmentVariables["KURRENTDB_OPEN_TELEMETRY__TRACES__ENABLED"]  = otel.TracesEnabled.ToString().ToLowerInvariant();
        }
    }

    IEnumerable<KeyValuePair<string, ReferenceExpression>> IResourceWithConnectionString.GetConnectionProperties() {
        yield return new("Host", ReferenceExpression.Create($"{Host}"));
        yield return new("Port", ReferenceExpression.Create($"{Port}"));
        yield return new("Uri", ConnectionStringExpression);
    }
}
