using System.Net.Http.Json;
using Kurrent.Surge.Testkit;
using Kurrent.Testkit.TUnit.Assertions;

namespace Aspire.Hosting.KurrentDB.Tests;

/// <summary>
/// Proves the hosting integration provisions each supported topology: the containers start,
/// gossip reports the requested shape, and a leader emerges. These tests register no schemas
/// and append nothing — a provisioning failure must not be confused with an SDK or registry
/// failure, which is why they live here rather than in the client's test project.
/// </summary>
static class ClusterTopology {
    // The cluster health check that would assert quorum is never wired up (WithClusterHealthCheck
    // has no callers), so a fixture reports ready on per-node health alone — before an election
    // necessarily completed. Convergence has to be polled here.
    static readonly TimeSpan ConvergenceBudget = TimeSpan.FromSeconds(90);

    public static async ValueTask AssertProvisioned(
        KurrentDBFixture fixture, int expectedNodes, int expectedReplicas, CancellationToken ct
    ) {
        var server         = fixture.AppModel.Resources.OfType<KurrentDBServerResource>().Single();
        var expectedTotal  = expectedNodes + expectedReplicas;

        // Resource graph: one container per node plus one per replica, replicas named apart.
        await Assert.That(server.Nodes).HasCount().EqualTo(expectedTotal);
        await Assert.That(server.Nodes.Count(n => n.IsReadOnlyReplica)).IsEqualTo(expectedReplicas);
        await Assert.That(server.Nodes.Where(n => n.IsReadOnlyReplica).All(n => n.Name.Contains("-ro"))).IsTrue();

        // Gossip: every member visible from every node, exactly one leader, replicas flagged.
        foreach (var node in server.Nodes) {
            var url = node.PrimaryEndpoint.Url;

            await Assert.That(async () => {
                var gossip = await ReadGossip(url, ct);

                await Assert.That(gossip.Members).HasCount().EqualTo(expectedTotal);
                await Assert.That(gossip.Members.Count(m => m is { IsAlive: true, State: "Leader" })).IsEqualTo(1);
                await Assert.That(gossip.Members.Count(m => m.IsReadOnlyReplica)).IsEqualTo(expectedReplicas);
                await Assert.That(gossip.Members.All(m => m.IsAlive)).IsTrue();
            }).EventuallyPasses(new EventuallyOptions { Timeout = ConvergenceBudget });
        }
    }

    static async ValueTask<GossipResponse> ReadGossip(string baseUrl, CancellationToken ct) {
        // The fixture's certificates are generated per run and self-signed, so the gossip probe
        // trusts them unconditionally — this asserts topology, not chain validation.
        using var handler = new HttpClientHandler {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };

        using var http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };

        return await http.GetFromJsonAsync("/gossip", GossipJsonContext.Default.GossipResponse, ct)
            ?? throw new InvalidOperationException($"Gossip endpoint at {baseUrl} returned no payload.");
    }
}

[Category("Hosting")]
[Category("Integration")]
[Timeout(120_000)]
public class SingleNodeProvisioningTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask single_node_with_tls_starts_and_leads_itself(CancellationToken ct) =>
        await ClusterTopology.AssertProvisioned(Fixture, expectedNodes: 1, expectedReplicas: 0, ct);
}

[Category("Hosting")]
[Category("Integration")]
[Timeout(120_000)]
public class InsecureSingleNodeProvisioningTests {
    [ClassDataSource<KurrentDBInsecureFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBInsecureFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask single_node_without_tls_starts_and_leads_itself(CancellationToken ct) =>
        await ClusterTopology.AssertProvisioned(Fixture, expectedNodes: 1, expectedReplicas: 0, ct);
}

[Category("Hosting")]
[Category("Integration")]
[Timeout(300_000)]
public class ReplicaLessClusterProvisioningTests {
    [ClassDataSource<KurrentDBClusterNoReplicasFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBClusterNoReplicasFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask three_node_cluster_without_replicas_elects_a_leader(CancellationToken ct) =>
        await ClusterTopology.AssertProvisioned(Fixture, expectedNodes: 3, expectedReplicas: 0, ct);
}

[Category("Hosting")]
[Category("Integration")]
[Timeout(300_000)]
public class ClusterWithReplicaProvisioningTests {
    [ClassDataSource<KurrentDBClusterFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBClusterFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask three_node_cluster_reports_its_replica_as_read_only(CancellationToken ct) =>
        await ClusterTopology.AssertProvisioned(Fixture, expectedNodes: 3, expectedReplicas: 1, ct);
}

[Category("Hosting")]
[Category("Integration")]
[Timeout(600_000)]
public class LargeClusterProvisioningTests {
    [ClassDataSource<KurrentDBLargeClusterFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBLargeClusterFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask five_node_cluster_with_two_replicas_converges(CancellationToken ct) =>
        await ClusterTopology.AssertProvisioned(Fixture, expectedNodes: 5, expectedReplicas: 2, ct);
}
