// ReSharper disable CheckNamespace

#pragma warning disable ASPIREATS001 // AspireExport is experimental

using Kurrent.Sdk.Testkit.Hosting;

namespace Aspire.Hosting.ApplicationModel;

[AspireExport(ExposeProperties = true)]
public sealed class KurrentDBServerResource : Resource, IResourceWithConnectionString {
    public const string DefaultResourceName = "kurrentdb";
    public const string DefaultUserName     = "admin";
    public const string DefaultPassword     = "changeit";
    public const int    DefaultHttpPort     = 2113;

    readonly List<KurrentDBResource> _nodes = [];

    public KurrentDBServerResource(string name, ParameterResource userName, ParameterResource password, bool tlsEnabled = true) : base(name) {
        TlsEnabled        = tlsEnabled;
        UserNameParameter  = userName;
        PasswordParameter  = password;
        
        CertsHostPath = Path.Combine(AppContext.BaseDirectory, "certs", name);
    }

    public string CertsHostPath { get; }

    public IReadOnlyList<KurrentDBResource> Nodes => _nodes;

    public bool TlsEnabled { get; }

    public ParameterResource UserNameParameter { get; }

    public ParameterResource PasswordParameter { get; }

    public OpenTelemetrySettings OpenTelemetry { get; internal set; } = new();

    public int ClusterSize => _nodes.Count;

    public string GossipSeeds => string.Join(", ", _nodes.Select(n => $"{n.Name}:{DefaultHttpPort}"));

    public KurrentDBResource AddNode(string nodeName, int priority = 0) {
        var node = new KurrentDBResource(nodeName, this, priority);
        _nodes.Add(node);
        return node;
    }

    public ReferenceExpression ConnectionStringExpression => BuildConnectionString(_nodes);

    internal ReferenceExpression BuildConnectionString(KurrentDBResource node) => BuildConnectionString([node]);

    ReferenceExpression BuildConnectionString(IReadOnlyList<KurrentDBResource> nodes) {
        var builder = new ReferenceExpressionBuilder();
        builder.Append($"kurrentdb://{UserNameParameter}:{PasswordParameter}@");

        for (var i = 0; i < nodes.Count; i++) {
            if (i > 0) builder.AppendLiteral(",");
            builder.Append($"{nodes[i].Host}:{nodes[i].Port}");
        }

        var tls = nodes[0].PrimaryEndpoint.GetTlsValue(
            enabledValue:  ReferenceExpression.Create($"tls=true&tlsVerifyCert=false"),
            disabledValue: ReferenceExpression.Create($"tls=false"));

        builder.Append($"?{tls}");

        return builder.Build();
    }

    IEnumerable<KeyValuePair<string, ReferenceExpression>> IResourceWithConnectionString.GetConnectionProperties() {
        yield return new("Uri", ConnectionStringExpression);
    }
}
