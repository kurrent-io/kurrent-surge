using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Aspire.Hosting.KurrentDB.Tests;

[Category("Hosting")]
public class KurrentDBServerResourceTests {
    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask with_environment_on_server_fans_out_to_single_node() {
        // Arrange
        var envKey   = $"KURRENTDB_{Faker.Lorem.Word().ToUpperInvariant()}";
        var envValue = Faker.Lorem.Word();

        var builder = DistributedApplicationTestingBuilder.Create([]);

        builder
            .AddKurrentDB()
            .RunInsecure()
            .WithEnvironment(envKey, envValue);

        await using var app = await builder.BuildAsync().ConfigureAwait(false);

        var server = app.Services.GetRequiredService<DistributedApplicationModel>()
            .Resources
            .OfType<KurrentDBServerResource>()
            .Single();

        var node = server.Nodes.Single();

        // Act
        var envVars = await CollectEnvironmentVariablesAsync(node).ConfigureAwait(false);

        // Assert
        await Assert.That(envVars).ContainsKey(envKey);
        await Assert.That(envVars[envKey]).IsEqualTo(envValue);
    }

    [Test]
    public async ValueTask with_environment_on_server_fans_out_to_all_cluster_nodes() {
        // Arrange
        var envKey   = $"KURRENTDB_{Faker.Lorem.Word().ToUpperInvariant()}";
        var envValue = Faker.Lorem.Word();

        var builder = DistributedApplicationTestingBuilder.Create([]);

        builder
            .AddKurrentDB(cfg => {
                cfg.TlsEnabled     = false;
                cfg.Topology.Nodes = 3;
            })
            .WithEnvironment(envKey, envValue);

        await using var app = await builder.BuildAsync().ConfigureAwait(false);

        var server = app.Services.GetRequiredService<DistributedApplicationModel>()
            .Resources
            .OfType<KurrentDBServerResource>()
            .Single();

        // Act & Assert — every node should have the env var
        await Assert.That(server.Nodes.Count).IsEqualTo(3);

        foreach (var node in server.Nodes) {
            var envVars = await CollectEnvironmentVariablesAsync(node).ConfigureAwait(false);

            await Assert.That(envVars).ContainsKey(envKey);
            await Assert.That(envVars[envKey]).IsEqualTo(envValue);
        }
    }

    [Test]
    public async ValueTask with_environment_on_server_does_not_clobber_node_config() {
        // Arrange — use a key that the node infra also sets
        var builder = DistributedApplicationTestingBuilder.Create([]);

        builder
            .AddKurrentDB()
            .RunInsecure()
            .WithEnvironment("KURRENTDB_INSECURE", "false");

        await using var app = await builder.BuildAsync().ConfigureAwait(false);

        var server = app.Services.GetRequiredService<DistributedApplicationModel>()
            .Resources
            .OfType<KurrentDBServerResource>()
            .Single();

        var node = server.Nodes.Single();

        // Act
        var envVars = await CollectEnvironmentVariablesAsync(node).ConfigureAwait(false);

        // Assert — the server-level .WithEnvironment runs AFTER ConfigureEnvironment,
        // so it should override the node's default value
        await Assert.That(envVars["KURRENTDB_INSECURE"]).IsEqualTo("false");
    }

    #region ->> Helpers <<-

    /// <summary>
    /// Executes all <see cref="EnvironmentCallbackAnnotation"/> callbacks on a node
    /// and returns the resulting environment variable dictionary as string key-value pairs.
    /// </summary>
    static async Task<Dictionary<string, string>> CollectEnvironmentVariablesAsync(KurrentDBResource node) {
        var envVars = new Dictionary<string, object>();
        var execCtx = new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run);
        var ctx     = new EnvironmentCallbackContext(execCtx, node, envVars);

        foreach (var annotation in node.Annotations.OfType<EnvironmentCallbackAnnotation>())
            await annotation.Callback(ctx).ConfigureAwait(false);

        return envVars.ToDictionary(
            kv => kv.Key,
            kv => kv.Value?.ToString() ?? "",
            StringComparer.OrdinalIgnoreCase);
    }

    #endregion
}
