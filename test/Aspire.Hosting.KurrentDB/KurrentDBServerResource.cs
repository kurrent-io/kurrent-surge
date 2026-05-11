// ReSharper disable CheckNamespace

using Aspire.Hosting.KurrentDB;

#pragma warning disable ASPIREATS001 // AspireExport is experimental

namespace Aspire.Hosting.ApplicationModel;

[AspireExport(ExposeProperties = true)]
public sealed class KurrentDBServerResource : Resource, IResourceWithConnectionString {
    public const string DefaultResourceName = "kurrentdb";
  
    public const int    DefaultHttpPort     = 2113;

    readonly List<KurrentDBResource> _nodes = [];
    
    public KurrentDBServerResource(string name, KurrentDBSettings settings) : base(name) {
        Settings = settings;
        
        var userNameParameter = new ParameterResource($"{name}-username", _ => settings.Credentials.UserName, secret: true);
        var passwordParameter = new ParameterResource($"{name}-password", _ => settings.Credentials.Password, secret: true);
        
        UserNameParameter = userNameParameter;
        PasswordParameter = passwordParameter;

        CertsHostPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "aspire", "kurrentdb", "certs", name);
    }
    
    KurrentDBSettings Settings { get; }

    public string CertsHostPath { get; }

    public IReadOnlyList<KurrentDBResource> Nodes => _nodes;

    public bool TlsEnabled => Settings.TlsEnabled;

    public ParameterResource UserNameParameter { get; }

    public ParameterResource PasswordParameter { get; }
    
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
