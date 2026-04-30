namespace Kurrent.Sdk.Testkit.Hosting;

public static class KurrentDBAppHostArgs {
    public const string DefaultResourceName = "kurrentdb";

    public const string TopologyNodes            = "topology:nodes";
    public const string TopologyReadOnlyReplicas = "topology:readOnlyReplicas";
    public const string SecurityTls              = "security:tls";

    public static string Nodes(int count)            => $"--{TopologyNodes}={count}";
    public static string ReadOnlyReplicas(int count) => $"--{TopologyReadOnlyReplicas}={count}";
    public static string Tls(bool enabled)           => $"--{SecurityTls}={(enabled ? "true" : "false")}";
}
