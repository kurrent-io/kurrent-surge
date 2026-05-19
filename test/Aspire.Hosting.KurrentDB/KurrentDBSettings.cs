namespace Aspire.Hosting.KurrentDB;

public class KurrentDBSettings {
    public string ImageRef {
        get => Container.ImageRef;
        set => Container.ImageRef = value;
    }
    
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
    /// The KurrentDB license key. Required for licensed features like UserCertificates,
    /// AutoScavenge, LDAP/OAuth auth, and licensed connectors.
    /// </summary>
    public string? LicenseKey { get; set; }

    /// <summary>
    /// Enables X.509 user certificate authentication. Requires <see cref="TlsEnabled"/>
    /// and a valid <see cref="LicenseKey"/>.
    /// </summary>
    public bool UserCertificatesEnabled { get; set; }

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
    /// The container lifetime for KurrentDB nodes. Default is <see cref="ContainerLifetime.Persistent"/>.
    /// Set to <see cref="ContainerLifetime.Session"/> for ephemeral containers (e.g., scavenge tests).
    /// </summary>
    public ContainerLifetime Lifetime { get; set; } = ContainerLifetime.Persistent;

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
    /// Sets the license key for KurrentDB licensed features.
    /// </summary>
    public KurrentDBSettings WithLicenseKey(string licenseKey) {
        LicenseKey = licenseKey;
        return this;
    }

    /// <summary>
    /// Enables X.509 user certificate authentication.
    /// </summary>
    public KurrentDBSettings EnableUserCertificates() {
        UserCertificatesEnabled = true;
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

        if (Topology.Nodes > 1 && Topology.Nodes % 2 == 0)
            throw new ArgumentException(
                $"Cluster size must follow the 2n+1 rule (1, 3, 5, 7...): got {Topology.Nodes}.", nameof(Topology.Nodes));

        if (UserCertificatesEnabled && !TlsEnabled)
            throw new ArgumentException("User certificate authentication requires TLS to be enabled.");

        if (UserCertificatesEnabled && string.IsNullOrEmpty(LicenseKey))
            throw new ArgumentException("User certificate authentication requires a license key.");

        ArgumentException.ThrowIfNullOrWhiteSpace(Container.Registry, "Container.Registry");
        ArgumentException.ThrowIfNullOrWhiteSpace(Container.Image, "Container.Image");
        ArgumentException.ThrowIfNullOrWhiteSpace(Container.Tag, "Container.Tag");

        if (!Container.Registry.Contains('.') && !Container.Registry.Contains(':') && Container.Registry != "localhost")
            throw new ArgumentException(
                $"Container registry must be a valid hostname (contain '.' or ':') or 'localhost': got '{Container.Registry}'.",
                "Container.Registry");

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
    
    /// <summary>
    /// Returns the total number of nodes in the cluster, including read-only replicas.
    /// </summary>
    internal int TotalNodeCount => Nodes + Replicas;
}

public sealed class NodeSettings {
    public int Priority { get; set; }
}

public sealed class CredentialSettings {
    const string DefaultUserName = "admin";
    const string DefaultPassword = "changeit";
    
    public string UserName { get; set; } = DefaultUserName;

    public string Password { get; set; } = DefaultPassword;
}

public sealed class ContainerSettings {
    public string Registry { get; set; } = KurrentDBContainerImageTags.Registry;

    public string Image { get; set; } = KurrentDBContainerImageTags.Image;

    public string Tag { get; set; } = KurrentDBContainerImageTags.Tag;

    public string ImageRef {
        get => string.IsNullOrEmpty(Registry) ? $"{Image}:{Tag}" : $"{Registry}/{Image}:{Tag}";
        set {
            if (string.IsNullOrWhiteSpace(value)) return;

            var lastSlash = value.LastIndexOf('/');
            var lastColon = value.LastIndexOf(':');

            string refPart;

            if (lastColon > lastSlash) {
                refPart = value[..lastColon];
                Tag     = value[(lastColon + 1)..];
            } else {
                refPart = value;
                Tag     = "latest";
            }

            var firstSlash = refPart.IndexOf('/');

            if (firstSlash > 0) {
                var firstSegment = refPart[..firstSlash];

                // Docker convention: first path segment is a registry if it contains '.' or ':'
                if (firstSegment.Contains('.') || firstSegment.Contains(':') || firstSegment == "localhost") {
                    Registry = firstSegment;
                    Image    = refPart[(firstSlash + 1)..];
                    return;
                }
            }

            Registry = "";
            Image    = refPart;
        }
    }
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
