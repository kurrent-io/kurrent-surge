namespace Kurrent.Sdk.Testkit.Hosting;

public class KurrentDBSettings {
    public static KurrentDBSettings SingleNode => new() {
        Topology   = new() { Nodes = 1 },
        TlsEnabled = true
    };

    public static KurrentDBSettings DefaultCluster => new() {
        Topology   = new() { Nodes = 3, Replicas = 1 },
        TlsEnabled = true
    };

    /// <summary>
    /// Represents the cluster topology for the KurrentDB resource, including the number of nodes and read-only replicas.
    /// </summary>
    public ClusterTopologySettings Topology { get; set; } = new();

    /// <summary>
    /// Represents the container image settings for the KurrentDB resource, including registry, image, and tag.
    /// </summary>
    public ContainerSettings Container { get; set; } = new();

    /// <summary>
    /// Indicates whether TLS encryption is enabled for the KurrentDB resource.
    /// </summary>
    public bool TlsEnabled { get; set; } = true;

    /// <summary>
    /// The credentials for the KurrentDB resource.
    /// </summary>
    public CredentialSettings Credentials { get; set; } = new();

    /// <summary>
    /// OpenTelemetry export settings for the KurrentDB node.
    /// Controls OTLP export of logs, metrics, and traces to the configured endpoint.
    /// </summary>
    public OpenTelemetrySettings OpenTelemetry { get; set; } = new();

    /// <summary>
    /// Gets or sets a boolean value that indicates whether the KurrentDB health check is disabled or not.
    /// </summary>
    /// <value>
    /// The default value is <see langword="false"/>.
    /// </value>
    public bool DisableHealthChecks { get; set; }

    /// <summary>
    /// Gets or sets the timeout duration for the health check.
    /// </summary>
    /// <value>
    /// The default value is <see langword="null"/>.
    /// </value>
    public TimeSpan? HealthCheckTimeout { get; set; }

    #region ->> Fluent API <<-

    /// <summary>
    /// Sets the cluster size. Must follow the 2n+1 rule (1, 3, 5, 7...).
    /// Creates default <see cref="NodeSettings"/> for each node.
    /// </summary>
    public KurrentDBSettings Cluster(int size) {
        if (size > 1 && size % 2 == 0)
            throw new ArgumentException(
                $"Cluster size must follow the 2n+1 rule (1, 3, 5, 7...): got {size}.", nameof(size));

        Topology.Nodes = size;
        return this;
    }

    /// <summary>
    /// Configures the cluster with granular per-node settings.
    /// The number of nodes added must follow the 2n+1 rule.
    /// </summary>
    public KurrentDBSettings Cluster(Action<ClusterBuilder> configure) {
        var builder = new ClusterBuilder();
        configure(builder);

        var count = builder.Build().Count;

        if (count > 1 && count % 2 == 0)
            throw new ArgumentException(
                $"Cluster size must follow the 2n+1 rule (1, 3, 5, 7...): got {count} nodes.");

        Topology.NodeConfigurations = builder.Build();
        return this;
    }

    /// <summary>
    /// Sets the number of read-only replicas.
    /// </summary>
    public KurrentDBSettings Replicas(int count) {
        Topology.Replicas = count;
        return this;
    }

    /// <summary>
    /// Enables TLS encryption for all nodes.
    /// </summary>
    public KurrentDBSettings EnableTls() {
        TlsEnabled = true;
        return this;
    }

    /// <summary>
    /// Sets custom credentials for the KurrentDB resource.
    /// </summary>
    public KurrentDBSettings WithCredentials(string userName, string password = "") {
        Credentials = new() { UserName = userName, Password = password };
        return this;
    }

    /// <summary>
    /// Configures OpenTelemetry export settings for the KurrentDB node.
    /// </summary>
    public KurrentDBSettings WithOpenTelemetry(Action<OpenTelemetrySettings> configure) {
        configure(OpenTelemetry);
        return this;
    }

    #endregion // Fluent API

    public KurrentDBSettings EnsureValid() {
        ArgumentException.ThrowIfNullOrWhiteSpace(Credentials.UserName);
        ArgumentOutOfRangeException.ThrowIfLessThan(Topology.Nodes, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(Topology.Replicas);

        if (Topology is { Replicas: > 0, Nodes: < 3 })
            throw new ArgumentException("Read-only replicas require a cluster of at least 3 nodes.");

        return this;
    }
}

public sealed class ClusterTopologySettings {
    List<NodeSettings> _nodes = [new()];

    public int Nodes {
        get => _nodes.Count;
        set {
            while (_nodes.Count < value) _nodes.Add(new());
            while (_nodes.Count > value) _nodes.RemoveAt(_nodes.Count - 1);
        }
    }

    public int Replicas { get; set; }

    public List<NodeSettings> NodeConfigurations {
        get => _nodes;
        set => _nodes = value;
    }
}

public sealed class NodeSettings {
    public int Priority { get; set; }
}

public sealed class ClusterBuilder {
    readonly List<NodeSettings> _nodes = [];

    public ClusterBuilder AddNode(int priority = 0) {
        _nodes.Add(new() { Priority = priority });
        return this;
    }

    internal List<NodeSettings> Build() => _nodes;
}

public sealed class CredentialSettings {
    public string UserName { get; set; } = KurrentDBServerResource.DefaultUserName;

    public string Password { get; set; } = KurrentDBServerResource.DefaultPassword;
}

public sealed class ContainerSettings {
    public string Registry { get; set; } = KurrentDBContainerImageTags.Registry;

    public string Image { get; set; } = KurrentDBContainerImageTags.Latest.Image;

    public string Tag { get; set; } = KurrentDBContainerImageTags.Latest.Tag;
}

public sealed class OpenTelemetrySettings {
    const string DefaultContainerEndpoint = "http://host.docker.internal:4317";

    /// <summary>
    /// The OTLP endpoint for exporting telemetry data.
    /// Defaults to the Aspire dashboard OTLP collector reachable from inside Docker.
    /// </summary>
    public string Endpoint { get; set; } = DefaultContainerEndpoint;

    /// <summary>
    /// Enables OTLP log export from the KurrentDB node. Default is <see langword="false"/>.
    /// </summary>
    public bool LogsEnabled { get; set; }

    /// <summary>
    /// Enables OTLP metrics export from the KurrentDB node. Default is <see langword="false"/>.
    /// </summary>
    public bool MetricsEnabled { get; set; }

    /// <summary>
    /// Enables OTLP traces export from the KurrentDB node. Default is <see langword="false"/>.
    /// </summary>
    public bool TracesEnabled { get; set; }

    /// <summary>
    /// Returns <see langword="true"/> if any telemetry signal is enabled.
    /// </summary>
    public bool IsEnabled => LogsEnabled || MetricsEnabled || TracesEnabled;

    /// <summary>
    /// Enables all telemetry signals (logs, metrics, traces).
    /// </summary>
    public OpenTelemetrySettings EnableAll() {
        LogsEnabled    = true;
        MetricsEnabled = true;
        TracesEnabled  = true;
        return this;
    }
}
