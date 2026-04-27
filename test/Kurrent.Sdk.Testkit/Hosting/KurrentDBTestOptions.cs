namespace Kurrent.Sdk.Testkit.Hosting;

public sealed record KurrentDBTopology(int NodeCount, int ReadOnlyReplicaCount = 0) {
    public static KurrentDBTopology SingleNode { get; } = new(1);

    public static KurrentDBTopology Cluster(int nodeCount = 3) => new(nodeCount);
}

public sealed record KurrentDBSecurity(bool Tls) {
    public static KurrentDBSecurity Insecure { get; } = new(false);
    public static KurrentDBSecurity Secure   { get; } = new(true);
}

public sealed record KurrentDBTestOptions {
    public KurrentDBTopology Topology { get; init; } = KurrentDBTopology.SingleNode;
    public KurrentDBSecurity Security { get; init; } = KurrentDBSecurity.Insecure;

    public static Builder Build => new();

    public Builder GetBuilder() => new(this);

    [PublicAPI]
    public sealed class Builder(KurrentDBTestOptions options) : FluentBuilder<Builder, KurrentDBTestOptions>(options) {
        public Builder() : this(new()) { }

        public Builder WithSingleNode() => With(o => o with {
            Topology = KurrentDBTopology.SingleNode
        });

        public Builder WithCluster(int nodes = 3) => With(o => o with {
            Topology = KurrentDBTopology.Cluster(nodes)
        });

        public Builder WithReadOnlyReplicas(int count) => With(o => o with {
            Topology = o.Topology with {
                ReadOnlyReplicaCount = count
            }
        });

        public Builder WithTls(bool enabled = true) => With(o => o with {
            Security = enabled ? KurrentDBSecurity.Secure : KurrentDBSecurity.Insecure
        });

        public static Builder Create() => new();
    }
}
