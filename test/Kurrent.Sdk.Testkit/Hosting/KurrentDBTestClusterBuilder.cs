using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Kurrent.SDK.Testing.Hosting;

public class KurrentDBNodeOptions {
    public string  ResourceName { get; set; } = "kurrentdb";
    public bool    Secure       { get; set; }
    public string? CertsPath    { get; set; }
    public string  Image        { get; set; } = KurrentDBContainerImageTags.Latest.Image;
    public string  Tag          { get; set; } = KurrentDBContainerImageTags.Latest.Tag;
}

public sealed class KurrentDBTestClusterBuilder {
    readonly List<KurrentDBNodeOptions> _nodes = [];
    string?                             _existingConnectionString;

    public KurrentDBTestClusterBuilder AddNode(string? name = null) {
        _nodes.Add(new KurrentDBNodeOptions { ResourceName = name ?? $"kurrentdb{(_nodes.Count == 0 ? "" : $"-{_nodes.Count}")}" });
        return this;
    }

    public KurrentDBTestClusterBuilder AddNode(Action<KurrentDBNodeOptions> configure) {
        var options = new KurrentDBNodeOptions();
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

        var builder = DistributedApplicationTestingBuilder.Create();

        foreach (var node in _nodes) {
            var resource = builder.AddKurrentDB(node.ResourceName, node.Image, node.Tag);

            if (node.Secure) {
                var certsPath = node.CertsPath ?? Path.GetFullPath("../../certs");
                resource.WithSecureMode(certsPath);
            }
        }

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

        return new KurrentDBTestCluster(app, connectionStrings, anonymousConnectionStrings);
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

        return new KurrentDBTestCluster(app, connectionStrings, anonymousConnectionStrings);
    }
}
