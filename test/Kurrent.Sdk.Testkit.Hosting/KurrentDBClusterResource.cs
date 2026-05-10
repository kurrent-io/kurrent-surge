// ReSharper disable CheckNamespace

#pragma warning disable ASPIREATS001 // AspireExport is experimental

namespace Aspire.Hosting.ApplicationModel;

[AspireExport(ExposeProperties = true)]
public sealed class KurrentDBClusterResource(string name, IReadOnlyList<KurrentDBResource> nodes) : Resource(name), IResourceWithConnectionString {
    public ReferenceExpression ConnectionStringExpression {
        get {
            var builder = new ReferenceExpressionBuilder();
            builder.Append($"kurrentdb://{nodes[0].UserNameExpression}:{nodes[0].PasswordExpression}@");

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
    }

    IEnumerable<KeyValuePair<string, ReferenceExpression>> IResourceWithConnectionString.GetConnectionProperties() {
        yield return new("Uri", ConnectionStringExpression);
    }
}
