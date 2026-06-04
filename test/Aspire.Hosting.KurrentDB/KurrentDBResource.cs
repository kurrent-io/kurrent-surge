// ReSharper disable CheckNamespace

#pragma warning disable ASPIREATS001 // AspireExport is experimental

using Aspire.Hosting.KurrentDB;

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

    public void ConfigureEnvironment(EnvironmentCallbackContext ctx, KurrentDBSettings settings) {
        var variables = settings.TlsEnabled
            ? NodeConfiguration.Secure
            : NodeConfiguration.Insecure;
        
        foreach (var (key, value) in variables) {
            if (value is not null)
                ctx.EnvironmentVariables[key] = value;
            else
                ctx.EnvironmentVariables.Remove(key);
        }

        ctx.EnvironmentVariables["KURRENTDB_CLUSTER_SIZE"]                     = settings.Topology.Nodes.ToString();
        ctx.EnvironmentVariables["KURRENTDB_DISCOVER_VIA_DNS"]                 = "false";
        ctx.EnvironmentVariables["KURRENTDB_GOSSIP_SEED"]                      = Parent.GossipSeeds;
        ctx.EnvironmentVariables["KURRENTDB_NODE_HOST_ADVERTISE_AS"]           = Name;
        ctx.EnvironmentVariables["KURRENTDB_REPLICATION_HOST_ADVERTISE_AS"]    = Name;
        ctx.EnvironmentVariables["KURRENTDB_ADVERTISE_HOST_TO_CLIENT_AS"]      = "localhost";
        ctx.EnvironmentVariables["KURRENTDB_ADVERTISE_NODE_PORT_TO_CLIENT_AS"] = ReferenceExpression.Create($"{PrimaryEndpoint.Property(EndpointProperty.Port)}");
        ctx.EnvironmentVariables["KURRENTDB_READ_ONLY_REPLICA"]                = IsReadOnlyReplica.ToString().ToLowerInvariant();

        if (Priority != 0)
            ctx.EnvironmentVariables["KURRENTDB_NODE_PRIORITY"] = Priority.ToString();

        // The license key is a secret, so it flows as a secret ParameterResource (see
        // KurrentDBServerResource) — Aspire masks it in the dashboard and emits it as a secret
        // input in the publish manifest rather than inlining the raw value.
        if (!string.IsNullOrEmpty(settings.LicenseKey))
            ctx.EnvironmentVariables["KURRENTDB_LICENSING__LICENSE_KEY"] = Parent.LicenseKeyParameter;

        ctx.EnvironmentVariables["KURRENTDB_USER_CERTIFICATES__ENABLED"] =
            settings.UserCertificatesEnabled.ToString().ToLowerInvariant();

        if (settings.OpenTelemetry.IsEnabled) {
            ctx.EnvironmentVariables["KURRENTDB_OPEN_TELEMETRY__OTLP__ENDPOINT"]   = settings.OpenTelemetry.Endpoint;
            ctx.EnvironmentVariables["KURRENTDB_OPEN_TELEMETRY__LOGS__ENABLED"]    = settings.OpenTelemetry.LogsEnabled.ToString().ToLowerInvariant();
            ctx.EnvironmentVariables["KURRENTDB_OPEN_TELEMETRY__METRICS__ENABLED"] = settings.OpenTelemetry.MetricsEnabled.ToString().ToLowerInvariant();
            ctx.EnvironmentVariables["KURRENTDB_OPEN_TELEMETRY__TRACES__ENABLED"]  = settings.OpenTelemetry.TracesEnabled.ToString().ToLowerInvariant();
        }
    }

    IEnumerable<KeyValuePair<string, ReferenceExpression>> IResourceWithConnectionString.GetConnectionProperties() {
        yield return new("Host", ReferenceExpression.Create($"{Host}"));
        yield return new("Port", ReferenceExpression.Create($"{Port}"));
        yield return new("Uri", ConnectionStringExpression);
    }
}
