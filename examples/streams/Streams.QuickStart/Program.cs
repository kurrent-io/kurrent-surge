
// Reproduces scenarios where rediscovery may NOT trigger after failover.
//
// The .NET client only reconnects on:
//   - NotLeaderNodeException → connect to reported leader
//   - RpcException { StatusCode: Unavailable } → rediscover via gossip
//
// Everything else (Internal, Unknown, DeadlineExceeded) leaves the client
// stuck on a dead channel — no rediscovery, appends keep failing.
//
// Usage:
//   dotnet run -- append [connectionString]
//   dotnet run -- readall [connectionString]
//   dotnet run -- auto [connectionString]         ← automated pause+kill repro
//
// Manual test scenarios:
//   1. `docker stop <leader>`       — graceful stop, usually produces Unavailable (recovers)
//   2. `docker kill <leader>`       — abrupt SIGKILL, may produce Internal/Unknown (may NOT recover)
//   3. `docker pause <leader>`      — frozen node, produces DeadlineExceeded (does NOT recover)
//   4. pause leader → kill leader   — permanently stuck (confirmed repro)

using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Kurrent.Client;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;

var mode = args.Length > 0 ? args[0] : "append";
var connectionString = args.Length > 1
    ? args[1]
    : "kurrentdb+discover://admin:changeit@localhost:2111,localhost:2112,localhost:2113?tls=true&tlsVerifyCert=false&nodePreference=leader&defaultDeadline=15000&keepAliveInterval=5000&keepAliveTimeout=5000&maxDiscoverAttempts=10000";

Console.WriteLine($"Mode: {mode}");
Console.WriteLine($"Connecting to: {connectionString}");

await using var client = KurrentClient.Create(connectionString);

Console.WriteLine(new string('-', 80));

switch (mode) {
    case "append":
        await RunOperationLoop(client, "append");
        break;
    case "readall":
        await RunOperationLoop(client, "readall");
        break;
    case "auto":
        await RunAutoRepro(client);
        break;
    case "flood":
        await RunFloodLoop(client);
        break;
    case "resign":
        await RunResignRepro(client);
        break;
    case "bad-hostname":
        await RunBadHostnameRepro(client);
        break;
    default:
        Console.WriteLine("Usage: dotnet run -- <append|readall|auto|flood|resign|bad-hostname> [connectionString]");
        break;
}

// ─── auto repro ──────────────────────────────────────────────────────────────

static async Task RunAutoRepro(KurrentClient client) {
    var containers = new Dictionary<int, string> {
        [2111] = "esdb-node1",
        [2112] = "esdb-node2",
        [2113] = "esdb-node3",
    };

    // step 1: establish connection with a few successful appends
    Console.WriteLine("[auto] Step 1: Establishing connection with successful appends...");
    var stream = $"auto-repro-{Guid.NewGuid():N}";

    for (var i = 0; i < 5; i++) {
        var result = await client.Streams.AppendStream(
            stream,
            [AppendRecord.New.ForStream(stream).WithValue(new { i }).Build()]
        );
        Console.WriteLine($"  Append #{i + 1} OK  revision={result.Revision}");
    }

    // step 2: find the leader via gossip
    Console.WriteLine("[auto] Step 2: Finding leader node via gossip...");
    var leaderPort = await FindLeaderPort(containers.Keys.ToArray());
    if (leaderPort == null) {
        Console.WriteLine("  ERROR: Could not find leader. Aborting.");
        return;
    }
    var leaderContainer = containers[leaderPort.Value];
    Console.WriteLine($"  Leader is on port {leaderPort} → container {leaderContainer}");

    // step 3: pause the leader
    Console.WriteLine($"[auto] Step 3: Pausing {leaderContainer}...");
    await Docker($"pause {leaderContainer}");
    Console.WriteLine($"  {leaderContainer} is paused.");

    // step 4: wait for errors to start, then verify client is stuck
    Console.WriteLine("[auto] Step 4: Attempting appends on paused leader (expect DeadlineExceeded)...");
    var stuckErrors = 0;
    for (var i = 0; i < 3; i++) {
        try {
            await client.Streams.AppendStream(
                stream,
                [AppendRecord.New.ForStream(stream).WithValue(new { stuck = true }).Build()]
            );
            Console.WriteLine($"  Append OK (unexpected!)");
        } catch (Exception ex) {
            stuckErrors++;
            var statusCode = ex is Grpc.Core.RpcException rpc ? rpc.StatusCode.ToString() : ex.GetType().Name;
            Console.WriteLine($"  Append failed: {statusCode}");
        }
    }

    // step 5: kill the paused leader
    Console.WriteLine($"[auto] Step 5: Killing paused {leaderContainer}...");
    await Docker($"kill {leaderContainer}");
    Console.WriteLine($"  {leaderContainer} is dead.");

    // step 6: verify a new leader exists on remaining nodes
    Console.WriteLine("[auto] Step 6: Verifying new leader elected on remaining nodes...");
    var remainingPorts = containers.Keys.Where(p => p != leaderPort).ToArray();
    var newLeaderPort = await FindLeaderPort(remainingPorts, retries: 15);
    if (newLeaderPort != null) {
        Console.WriteLine($"  New leader on port {newLeaderPort} → container {containers[newLeaderPort.Value]}");
    } else {
        Console.WriteLine("  WARNING: No new leader found (cluster may have lost quorum).");
    }

    // step 7: try appends — the client should be stuck
    Console.WriteLine("[auto] Step 7: Attempting appends (client should be stuck on dead channel)...");
    Console.WriteLine("  Waiting 30 seconds, expecting NO recovery...");
    Console.WriteLine();

    var recovered = false;
    var errorCount = 0;

    for (var i = 0; i < 30; i++) {
        try {
            var result = await client.Streams.AppendStream(
                stream,
                [AppendRecord.New.ForStream(stream).WithValue(new { attempt = i }).Build()]
            );
            Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] Append OK — RECOVERED after {errorCount} errors (revision={result.Revision})");
            recovered = true;
            break;
        } catch (Exception ex) {
            errorCount++;
            var statusCode = ex is Grpc.Core.RpcException rpc ? rpc.StatusCode.ToString() : ex.GetType().Name;
            Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] Attempt {errorCount}: {statusCode}");
        }
    }

    Console.WriteLine();
    Console.WriteLine(new string('=', 80));
    if (!recovered) {
        Console.WriteLine($"BUG CONFIRMED: Client stuck after {errorCount} attempts.");
        Console.WriteLine("The client never rediscovered despite a healthy leader being available.");
        Console.WriteLine($"Dead channel: port {leaderPort} ({leaderContainer})");
        if (newLeaderPort != null)
            Console.WriteLine($"Available leader: port {newLeaderPort} ({containers[newLeaderPort.Value]})");
    } else {
        Console.WriteLine("Client recovered — bug NOT reproduced in this run.");
    }
    Console.WriteLine(new string('=', 80));

    // cleanup
    Console.WriteLine();
    Console.WriteLine($"Cleanup: run 'docker start {leaderContainer}' to restore the cluster.");
}

// ─── resign repro ────────────────────────────────────────────────────────────
// Fires many concurrent appends and resigns the leader mid-flight.
// Under production-like load, the batch appender may have many pending requests
// when the server sends BatchAppendResp.Error (not-leader/server-not-ready).
// If the stream isn't terminated quickly, the appender stays stuck.

static async Task RunResignRepro(KurrentClient client) {
    // step 1: warm up - establish the batch appender with successful writes
    Console.WriteLine("[resign] Step 1: Warming up with 10 successful appends...");
    for (var i = 0; i < 10; i++) {
        var stream = $"resign-warmup-{Guid.NewGuid():N}";
        await client.Streams.AppendStream(
            stream,
            [AppendRecord.New.ForStream(stream).WithValue(new { warmup = true }).Build()]
        );
    }
    Console.WriteLine("  Warm-up complete. Batch appender is established.");

    // step 2: fire many concurrent appends, resign the leader mid-flight
    Console.WriteLine("[resign] Step 2: Firing 50 concurrent appends, resigning leader after 10ms...");
    Console.WriteLine();

    var resignTask = Task.Run(async () => {
        await Task.Delay(10); // let some appends start
        Console.WriteLine($"  [{DateTime.Now:HH:mm:ss.fff}] Resigning leader...");
        try {
            await client.Admin.ResignNode();
            Console.WriteLine($"  [{DateTime.Now:HH:mm:ss.fff}] Leader resigned.");
        } catch (Exception ex) {
            Console.WriteLine($"  [{DateTime.Now:HH:mm:ss.fff}] Resign failed: {ex.GetType().Name}: {ex.Message}");
        }
    });

    var appendTasks = Enumerable.Range(0, 50).Select(async i => {
        var stream = $"resign-test-{Guid.NewGuid():N}";
        try {
            var result = await client.Streams.AppendStream(
                stream,
                [AppendRecord.New.ForStream(stream).WithValue(new { i }).Build()]
            );
            return (index: i, error: (string?)null, detail: $"revision={result.Revision}");
        } catch (NotLeaderNodeException ex) {
            return (index: i, error: "NotLeaderNodeException", detail: $"leader={ex.CurrentLeader}");
        } catch (Grpc.Core.RpcException ex) {
            return (index: i, error: $"RpcException({ex.StatusCode})", detail: ex.Status.Detail);
        } catch (Exception ex) {
            return (index: i, error: ex.GetType().Name, detail: ex.Message);
        }
    }).ToArray();

    await resignTask;
    var results = await Task.WhenAll(appendTasks);

    var errors = results.Where(r => r.error != null).OrderBy(r => r.index).ToArray();
    var ok = results.Length - errors.Length;

    Console.WriteLine($"  Results: {ok} OK, {errors.Length} errors");
    if (errors.Length > 0) {
        var grouped = errors.GroupBy(e => e.error).Select(g => $"  {g.Key}: {g.Count()}");
        Console.WriteLine(string.Join("\n", grouped));
        Console.WriteLine();
        Console.WriteLine("  Error details (first 10):");
        foreach (var err in errors.Take(10))
            Console.WriteLine($"    [{err.index}] {err.error}: {err.detail}");
    }

    // step 3: wait for cluster to stabilize, then test recovery
    Console.WriteLine();
    Console.WriteLine("[resign] Step 3: Waiting 5s for cluster to stabilize, then testing recovery...");
    await Task.Delay(5000);

    var recovered = false;
    var recoveryErrors = 0;
    for (var i = 0; i < 30; i++) {
        try {
            var stream = $"resign-recovery-{Guid.NewGuid():N}";
            await client.Streams.AppendStream(
                stream,
                [AppendRecord.New.ForStream(stream).WithValue(new { recovery = true }).Build()]
            );
            Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] RECOVERED after {recoveryErrors} errors");
            recovered = true;
            break;
        } catch (Exception ex) {
            recoveryErrors++;
            var errorType = ex is Grpc.Core.RpcException rpc ? $"RpcException({rpc.StatusCode})" : ex.GetType().Name;
            Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] Recovery attempt {recoveryErrors}: {errorType}: {ex.Message}");
            await Task.Delay(1000);
        }
    }

    Console.WriteLine();
    Console.WriteLine(new string('=', 80));
    if (recovered) {
        Console.WriteLine("Client recovered after leader resignation.");
    } else {
        Console.WriteLine($"BUG: Client stuck after {recoveryErrors} recovery attempts.");
    }
    Console.WriteLine(new string('=', 80));
}

// ─── bad-hostname repro ─────────────────────────────────────────────────────
// Reproduces Insureon's AdvertiseHostToClientAs misconfiguration.
//
// Requires the cluster to be started with the bad-hostname override:
//   cd qa/cluster
//   docker compose -f docker-compose.yml -f docker-compose.bad-hostnames.yml up -d
//
// In that setup:
//   Node 1 (port 2111): advertises as localhost     (correct, resolvable)
//   Node 2 (port 2112): advertises as node22.esdb.local (WRONG, not resolvable)
//   Node 3 (port 2113): advertises as node33.esdb.local (WRONG, not resolvable)
//
// The test resigns the leader until leadership moves to a misconfigured node,
// then verifies the client gets permanently stuck.

static async Task RunBadHostnameRepro(KurrentClient client) {
    var ports = new[] { 2111, 2112, 2113 };
    var badPorts = new HashSet<int> { 2112, 2113 }; // nodes with wrong AdvertiseHostToClientAs

    // step 0: ensure the leader is on the correctly-configured node (port 2111)
    // before establishing the client connection, otherwise initial gossip picks
    // the bad hostname and the client can't even start.
    Console.WriteLine("[bad-hostname] Step 0: Ensuring leader is on correctly-configured node (port 2111)...");

    for (var attempt = 1; attempt <= 15; attempt++) {
        var leaderPort = await FindLeaderPort(ports, retries: 3);
        if (leaderPort == null) {
            Console.WriteLine($"  Attempt {attempt}: Could not find leader. Waiting...");
            await Task.Delay(3000);
            continue;
        }

        if (leaderPort == 2111) {
            Console.WriteLine($"  Leader is on port 2111 (correctly-configured). Good.");
            break;
        }

        Console.WriteLine($"  Attempt {attempt}: Leader is on port {leaderPort} (misconfigured). Resigning...");
        await ResignNode(leaderPort.Value);

        await Task.Delay(5000);
    }

    var initialLeader = await FindLeaderPort(ports, retries: 5);
    if (initialLeader != 2111) {
        Console.WriteLine($"  ERROR: Could not get leader onto port 2111 (current: {initialLeader}). Aborting.");
        Console.WriteLine("  Try again — election results are non-deterministic.");
        return;
    }

    // step 1: establish connection with successful appends
    Console.WriteLine("[bad-hostname] Step 1: Establishing connection with successful appends...");
    var stream = $"bad-hostname-{Guid.NewGuid():N}";

    for (var i = 0; i < 5; i++) {
        var result = await client.Streams.AppendStream(
            stream,
            [AppendRecord.New.ForStream(stream).WithValue(new { i }).Build()]
        );
        Console.WriteLine($"  Append #{i + 1} OK  revision={result.Revision}");
    }

    // step 2: resign the leader until it lands on a misconfigured node (port 2112 or 2113)
    Console.WriteLine("[bad-hostname] Step 2: Resigning leader until it moves to a misconfigured node...");

    var maxResignAttempts = 15;

    for (var attempt = 1; attempt <= maxResignAttempts; attempt++) {
        var leaderPort = await FindLeaderPort(ports, retries: 3);
        if (leaderPort == null) {
            Console.WriteLine("  Could not find leader. Waiting...");
            await Task.Delay(3000);
            continue;
        }

        Console.WriteLine($"  Attempt {attempt}: Leader is on port {leaderPort}");

        if (badPorts.Contains(leaderPort.Value)) {
            Console.WriteLine($"  Leader is on misconfigured node (port {leaderPort}). Testing client...");
            break;
        }

        Console.WriteLine($"  Leader is on correctly-configured node (port {leaderPort}). Resigning...");

        await ResignNode(leaderPort.Value);

        // wait for a new leader to be elected
        await Task.Delay(5000);
    }

    // verify the current leader is on a bad port
    var currentLeader = await FindLeaderPort(ports, retries: 10);
    if (currentLeader == null || !badPorts.Contains(currentLeader.Value)) {
        Console.WriteLine($"  Could not get leader onto a misconfigured node after {maxResignAttempts} attempts.");
        Console.WriteLine("  Current leader: " + (currentLeader?.ToString() ?? "unknown"));
        Console.WriteLine("  Try running again — election results are non-deterministic.");
        return;
    }

    Console.WriteLine();
    Console.WriteLine($"[bad-hostname] Leader is now on port {currentLeader} (misconfigured node).");
    Console.WriteLine("[bad-hostname] The gossip will advertise an unresolvable hostname for this leader.");
    Console.WriteLine();

    // step 3: attempt appends — the client should be stuck
    Console.WriteLine("[bad-hostname] Step 3: Attempting appends (expect permanent failure)...");
    Console.WriteLine();

    var recovered = false;
    var errorCount = 0;

    for (var i = 0; i < 20; i++) {
        try {
            var result = await client.Streams.AppendStream(
                stream,
                [AppendRecord.New.ForStream(stream).WithValue(new { attempt = i }).Build()]
            );
            Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] Append OK — RECOVERED after {errorCount} errors (revision={result.Revision})");
            recovered = true;
            break;
        } catch (Exception ex) {
            errorCount++;
            var errorType = ex switch {
                NotLeaderNodeException nle => $"NotLeaderNodeException (leader={nle.CurrentLeader})",
                Grpc.Core.RpcException rpc => $"RpcException({rpc.StatusCode}: {rpc.Status.Detail})",
                _ => $"{ex.GetType().Name}: {ex.Message}"
            };
            Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] Attempt {errorCount}: {errorType}");
        }

        await Task.Delay(2000);
    }

    Console.WriteLine();
    Console.WriteLine(new string('=', 80));
    if (!recovered) {
        Console.WriteLine($"BUG REPRODUCED: Client stuck after {errorCount} attempts.");
        Console.WriteLine("The client cannot resolve the advertised leader hostname.");
        Console.WriteLine();
        Console.WriteLine("Root cause: AdvertiseHostToClientAs on the leader node is set to a");
        Console.WriteLine("hostname that doesn't exist in DNS. The client's gossip-based");
        Console.WriteLine("rediscovery also returns these bad hostnames, so it can never recover.");
        Console.WriteLine();
        Console.WriteLine("This matches the Insureon production issue where nodes advertised as");
        Console.WriteLine("default-11, default-22, default-33 instead of default-01, default-02, default-03.");
    } else {
        Console.WriteLine("Client recovered — bug NOT reproduced in this run.");
        Console.WriteLine("The leader may have moved back to the correctly-configured node.");
    }
    Console.WriteLine(new string('=', 80));
}

// ─── shared operation loop ───────────────────────────────────────────────────

static async Task RunOperationLoop(KurrentClient client, string operation) {
    var stream = $"failover-test-{Guid.NewGuid():N}";
    var position = LogPosition.Earliest;
    var count = 0;
    var consecutiveErrors = 0;

    if (operation == "readall") {
        Console.WriteLine("[readall] Seeding events...");
        for (var i = 0; i < 5; i++) {
            var seedStream = $"readall-seed-{Guid.NewGuid():N}";
            await client.Streams.AppendStream(
                seedStream,
                [AppendRecord.New.ForStream(seedStream).WithValue(new { i }).Build()]
            );
        }
    }

    Console.WriteLine($"[{operation}] Running. Kill/pause the leader to test failover. Ctrl+C to stop.");

    while (true) {
        count++;

        try {
            if (operation == "append") {
                var result = await client.Streams.AppendStream(
                    stream,
                    [AppendRecord.New.ForStream(stream).WithValue(new { count }).Build()]
                );

                LogResult(count, ref consecutiveErrors, $"revision={result.Revision}");
            } else {
                var records = await client.Streams
                    .ReadRecords(
                        ReadRecordsRequest.New.FromPosition(position).WithMaxRecords(10).Build()
                    )
                    .ToRecordsList();

                if (records.Count > 0)
                    position = records[^1].Position.LogPosition;

                LogResult(count, ref consecutiveErrors, $"events={records.Count}  position={position}");
            }
        } catch (NotLeaderNodeException ex) {
            LogError(count, ref consecutiveErrors, "NotLeaderNodeException", $"leader={ex.CurrentLeader}", willRecover: true);
        } catch (Grpc.Core.RpcException ex) {
            var willRecover = ex.StatusCode == Grpc.Core.StatusCode.Unavailable;
            LogError(count, ref consecutiveErrors, "RpcException", $"status={ex.StatusCode}  detail={ex.Status.Detail}", willRecover);
        } catch (Exception ex) {
            LogError(count, ref consecutiveErrors, ex.GetType().Name, ex.Message, willRecover: false);
        }

        await Task.Delay(1000);
    }
}

// ─── flood mode ──────────────────────────────────────────────────────────────

static async Task RunFloodLoop(KurrentClient client) {
    const int concurrency = 20;

    // seed events
    Console.WriteLine("[flood] Seeding events...");
    for (var i = 0; i < 50; i++) {
        var stream = $"flood-seed-{i}";
        await client.Streams.AppendStream(
            stream,
            [AppendRecord.New.ForStream(stream).WithValue(new { i }).Build()]
        );
    }

    Console.WriteLine($"[flood] Firing {concurrency} concurrent ReadAll requests per batch, no delay.");
    Console.WriteLine("[flood] Run ./kill-leader.sh NOW to catch a request mid-flight.");
    Console.WriteLine(new string('-', 80));

    var batch = 0;
    var consecutiveErrorBatches = 0;

    while (true) {
        batch++;
        var tasks = Enumerable.Range(0, concurrency).Select(async i => {
            try {
                var records = await client.Streams
                    .ReadRecords(
                        ReadRecordsRequest.New.FromPosition(LogPosition.Earliest).WithMaxRecords(100).Build()
                    )
                    .ToRecordsList();
                return (error: (string?)null, detail: $"events={records.Count}");
            } catch (NotLeaderNodeException ex) {
                return (error: "NotLeaderNodeException", detail: $"leader={ex.CurrentLeader}");
            } catch (Grpc.Core.RpcException ex) {
                return (error: $"RpcException({ex.StatusCode})", detail: ex.Status.Detail);
            } catch (Exception ex) {
                return (error: ex.GetType().Name, detail: ex.Message);
            }
        }).ToArray();

        var results = await Task.WhenAll(tasks);
        var errors = results.Where(r => r.error != null).ToArray();
        var ok = results.Length - errors.Length;

        if (errors.Length > 0) {
            consecutiveErrorBatches++;
            var grouped = errors.GroupBy(e => e.error).Select(g => $"{g.Key}={g.Count()}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Batch #{batch}  ok={ok}  errors={errors.Length}  [{string.Join(", ", grouped)}]");

            if (consecutiveErrorBatches > 1) {
                // show if any error would trigger recovery
                foreach (var err in errors.DistinctBy(e => e.error)) {
                    var willRecover = err.error == "NotLeaderNodeException" || err.error == "RpcException(Unavailable)";
                    Console.WriteLine($"             {err.error}: {(willRecover ? "WILL recover" : "will NOT recover")}  detail={err.detail}");
                }
            }
        } else {
            if (consecutiveErrorBatches > 0) {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Batch #{batch}  RECOVERED after {consecutiveErrorBatches} error batches  ok={ok}");
                consecutiveErrorBatches = 0;
            } else {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Batch #{batch}  ok={ok}");
            }
        }

        await Task.Delay(100); // 100ms between batches for high throughput
    }
}

// ─── helpers ─────────────────────────────────────────────────────────────────

static async Task ResignNode(int port) {
    try {
        using var handler = new HttpClientHandler {
            ServerCertificateCustomValidationCallback = delegate { return true; }
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var request = new HttpRequestMessage(HttpMethod.Post, $"https://localhost:{port}/admin/node/resign");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:changeit")));
        var response = await http.SendAsync(request);
        Console.WriteLine($"    Resign response: {(int)response.StatusCode} {response.StatusCode}");
    } catch (Exception ex) {
        Console.WriteLine($"    Resign failed: {ex.GetType().Name}: {ex.Message}");
    }
}

static void LogResult(int count, ref int consecutiveErrors, string detail) {
    if (consecutiveErrors > 0) {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] #{count} RECOVERED after {consecutiveErrors} errors  {detail}");
        consecutiveErrors = 0;
    } else {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] #{count} OK  {detail}");
    }
}

static void LogError(int count, ref int consecutiveErrors, string errorType, string detail, bool willRecover) {
    consecutiveErrors++;
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] #{count} {errorType,-28} {detail}");
    Console.WriteLine($"             → {(willRecover ? "triggers rediscovery (WILL recover)" : "does NOT trigger rediscovery (client stuck on dead channel)")}");
}

static async Task<int?> FindLeaderPort(int[] ports, int retries = 5) {
    using var handler = new HttpClientHandler {
        ServerCertificateCustomValidationCallback = delegate { return true; }
    };
    using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };

    for (var attempt = 0; attempt < retries; attempt++) {
        foreach (var port in ports) {
            try {
                var json = await http.GetStringAsync($"https://localhost:{port}/gossip");
                var doc = JsonDocument.Parse(json);
                var members = doc.RootElement.GetProperty("members");

                foreach (var member in members.EnumerateArray()) {
                    if (member.GetProperty("state").GetString() == "Leader" &&
                        member.GetProperty("isAlive").GetBoolean()) {
                        return member.GetProperty("httpEndPointPort").GetInt32();
                    }
                }
            } catch {
                // node might be down or paused
            }
        }

        await Task.Delay(2000);
    }

    return null;
}

static async Task Docker(string args) {
    var psi = new ProcessStartInfo("docker", args) {
        RedirectStandardOutput = true,
        RedirectStandardError  = true,
    };
    using var proc = Process.Start(psi)!;
    await proc.WaitForExitAsync();
    if (proc.ExitCode != 0) {
        var err = await proc.StandardError.ReadToEndAsync();
        throw new Exception($"docker {args} failed: {err}");
    }
}
