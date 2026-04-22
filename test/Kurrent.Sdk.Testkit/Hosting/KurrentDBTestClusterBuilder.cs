using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Kurrent.Sdk.Testkit.Hosting;

public class KurrentDBNodeOptions {
    public string ResourceName { get; set; } = "kurrentdb";
    public bool   Secure       { get; set; }
    public string Image        { get; set; } = KurrentDBContainerImageTags.Nightly.Image;
    public string Tag          { get; set; } = KurrentDBContainerImageTags.Nightly.Tag;
}

public sealed class KurrentDBTestClusterBuilder {
    readonly List<KurrentDBNodeOptions> _nodes = [];
    string?                             _existingConnectionString;

    public KurrentDBTestClusterBuilder AddNode(string? name = null) {
        _nodes.Add(new KurrentDBNodeOptions { ResourceName = name ?? $"kurrentdb{(_nodes.Count == 0 ? "" : $"-{_nodes.Count}")}" });
        return this;
    }

    public KurrentDBTestClusterBuilder AddNode(Action<KurrentDBNodeOptions> configure) {
        var options = new KurrentDBNodeOptions {
            ResourceName = $"kurrentdb{(_nodes.Count == 0 ? "" : $"-{_nodes.Count}")}"
        };
        configure(options);
        _nodes.Add(options);
        return this;
    }

    public KurrentDBTestClusterBuilder UseExistingInstance(string connectionString) {
        _existingConnectionString = connectionString;
        return this;
    }

    public async Task<KurrentDBTestCluster> BuildAsync(CancellationToken ct = default) {
        if (_existingConnectionString is { })
            return BuildFromExistingInstance();

        if (_nodes.Count == 0)
            AddNode();

        var secureCluster = _nodes.Any(n => n.Secure);
        foreach (var node in _nodes)
            node.Secure = secureCluster;

        var builder = DistributedApplicationTestingBuilder.Create();

        var resources = new List<IResourceBuilder<KurrentDBResource>>(_nodes.Count);
        foreach (var node in _nodes) {
            var resource = builder.AddKurrentDB(node.ResourceName, node.Image, node.Tag);

            if (node.Secure)
                resource.WithSecureMode();

            resources.Add(resource);
        }

        if (_nodes.Count > 1)
            ConfigureClusterTopology(resources);

        var app           = await builder.BuildAsync(ct);
        var notifications = app.Services.GetRequiredService<ResourceNotificationService>();

        await app.StartAsync(ct);

        var connectionStrings          = new Dictionary<string, string>();
        var anonymousConnectionStrings = new Dictionary<string, string>();

        foreach (var node in _nodes) {
            try {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromMinutes(2));
                await notifications.WaitForResourceHealthyAsync(node.ResourceName, cts.Token);
            }
            catch (InvalidOperationException) {
                // Resource is a connection string reference; no health check needed.
            }
            catch (OperationCanceledException ex) {
                throw new TimeoutException(
                    $"KurrentDB resource '{node.ResourceName}' did not become healthy within 2 minutes.",
                    ex
                );
            }

            var baseCs = await app.GetConnectionStringAsync(node.ResourceName, ct)
                      ?? throw new InvalidOperationException($"Failed to get connection string for resource '{node.ResourceName}'.");

            connectionStrings[node.ResourceName]          = KurrentDBTestCluster.InjectCredentials(baseCs);
            anonymousConnectionStrings[node.ResourceName] = baseCs;
        }

        var certsDirectory = KurrentDBBuilderExtensions.GetCertsHostPath(builder);

        return new KurrentDBTestCluster(app, connectionStrings, anonymousConnectionStrings, certsDirectory);
    }

    static void ConfigureClusterTopology(IReadOnlyList<IResourceBuilder<KurrentDBResource>> nodes) {
        var clusterSize = nodes.Count.ToString();

        for (var i = 0; i < nodes.Count; i++) {
            var self  = nodes[i];
            var peers = nodes.Where((_, j) => j != i).ToArray();

            var gossipSeed = string.Join(
                ",",
                peers.Select(p => $"{p.Resource.Name}:{KurrentDBResource.DefaultHttpPort}")
            );

            self
                .WithEnvironment("KURRENTDB_CLUSTER_SIZE", clusterSize)
                .WithEnvironment("KURRENTDB_DISCOVER_VIA_DNS", "false")
                .WithEnvironment("KURRENTDB_GOSSIP_SEED", gossipSeed)
                .WithEnvironment("KURRENTDB_NODE_HOST_ADVERTISE_AS", self.Resource.Name)
                .WithEnvironment("KURRENTDB_REPLICATION_HOST_ADVERTISE_AS", self.Resource.Name)
                .WithEnvironment("KURRENTDB_ADVERTISE_HOST_TO_CLIENT_AS", "localhost")
                .WithEnvironment(
                    "KURRENTDB_ADVERTISE_NODE_PORT_TO_CLIENT_AS",
                    ReferenceExpression.Create($"{self.Resource.PrimaryEndpoint.Property(EndpointProperty.Port)}")
                );
        }
    }

    KurrentDBTestCluster BuildFromExistingInstance() {
        var cs   = _existingConnectionString!;
        var name = "kurrentdb";

        var connectionStrings = new Dictionary<string, string> { [name] = cs };

        // Strip credentials for anonymous string
        var uri                        = new Uri(cs);
        var anonymous                  = $"{uri.Scheme}://{uri.Host}:{uri.Port}{uri.PathAndQuery}";
        var anonymousConnectionStrings = new Dictionary<string, string> { [name] = anonymous };

        // Create a minimal Aspire app that does nothing -- just a handle for the API contract
        var builder = DistributedApplication.CreateBuilder();
        builder.AddConnectionString(name);
        var app = builder.Build();

        return new KurrentDBTestCluster(app, connectionStrings, anonymousConnectionStrings, certsDirectory: null);
    }
}
