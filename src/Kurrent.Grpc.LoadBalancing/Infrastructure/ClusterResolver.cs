using System.Net;
using System.Threading.Channels;
using Grpc.Net.Client;
using Grpc.Net.Client.Balancer;
using Microsoft.Extensions.Logging;

namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// gRPC Resolver that discovers cluster topology via TopologySource.
/// </summary>
/// <remarks>
/// <para>
/// The resolver owns the <see cref="Channel{T}"/> for topology updates and
/// manages the "working set" of endpoints (discovered nodes vs seeds fallback).
/// </para>
/// <para>
/// Working set pattern:
/// <list type="number">
/// <item>Initially use seeds for discovery</item>
/// <item>After first topology, use discovered nodes for subsequent queries</item>
/// <item>Shuffle endpoints for load distribution</item>
/// <item>Fall back to seeds when all discovered nodes fail</item>
/// </list>
/// </para>
/// <para>
/// See <c>reference-cluster-resolver-qa.md</c> for detailed explanation.
/// </para>
/// </remarks>
internal sealed class ClusterResolver : Resolver {
    readonly TopologySource _source;
    readonly ResilienceOptions _resilience;
    readonly DnsEndPoint[] _seeds;
    readonly bool _useTls;
    readonly ILoggerFactory? _loggerFactory;
    readonly ILogger? _logger;

    readonly Channel<ClusterTopology> _topologyChannel;
    readonly CancellationTokenSource _cts = new();
    readonly object _lock = new();

    // Working set state
    ClusterTopology? _lastTopology;
    CancellationTokenSource? _refreshCts;

    Task? _runTask;
    bool _started;

    public ClusterResolver(
        TopologySource source,
        DnsEndPoint[] seeds,
        ResilienceOptions resilience,
        bool useTls = false,
        ILoggerFactory? loggerFactory = null) {

        _source = source ?? throw new ArgumentNullException(nameof(source));
        _seeds = seeds ?? throw new ArgumentNullException(nameof(seeds));
        _resilience = resilience ?? throw new ArgumentNullException(nameof(resilience));
        _useTls = useTls;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<ClusterResolver>();

        _topologyChannel = Channel.CreateUnbounded<ClusterTopology>(
            new UnboundedChannelOptions {
                SingleReader = true,
                SingleWriter = true
            });
    }

    /// <inheritdoc/>
    public override void Start(Action<ResolverResult> listener) {
        if (_started)
            throw new InvalidOperationException("Resolver already started");

        _started = true;
        _runTask = RunAsync(listener);
    }

    /// <inheritdoc/>
    public override void Refresh() {
        _logger?.LogDebug("Refresh requested");

        // Cancel current polling delay to trigger immediate refresh
        lock (_lock) {
            _refreshCts?.Cancel();
        }
    }

    async Task RunAsync(Action<ResolverResult> listener) {
        var ct = _cts.Token;

        // Run source in background with retry logic
        _ = Task.Run(() => RunSourceWithRetryAsync(ct), ct);

        // Consume topology updates
        await foreach (var topology in _topologyChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false)) {
            // Update working set
            _lastTopology = topology;

            var addresses = topology.Nodes
                .Where(n => n.IsEligible)
                .Select(n => {
                    var address = new BalancerAddress(n.EndPoint.Host, n.EndPoint.Port);
                    SetAttributes(address.Attributes, n);
                    return address;
                })
                .ToArray();

            if (addresses.Length > 0) {
                listener(ResolverResult.ForResult(addresses));
                _logger?.LogDebug("Reported {Count} addresses to load balancer", addresses.Length);
            }
            else {
                _logger?.LogWarning("No eligible nodes in topology");
            }
        }
    }

    async Task RunSourceWithRetryAsync(CancellationToken ct) {
        var attempt = 0;
        var backoff = _resilience.InitialBackoff;

        while (!ct.IsCancellationRequested) {
            try {
                attempt++;
                _logger?.LogDebug("Starting topology source, attempt {Attempt}", attempt);

                // Get working endpoints (discovered nodes or seeds)
                var workingEndpoints = GetWorkingEndpoints();

                // Try each endpoint until one succeeds
                Exception? lastException = null;
                foreach (var endpoint in workingEndpoints) {
                    try {
                        var channel = CreateChannel(endpoint);
                        var context = new TopologyContext(channel, _seeds, ct) {
                            Logger = _logger,
                            Timeout = _resilience.Timeout
                        };

                        // Create cancellable delay token for refresh
                        using var refreshCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        lock (_lock) { _refreshCts = refreshCts; }

                        try {
                            await _source.RunAsync(_topologyChannel.Writer, context).ConfigureAwait(false);
                            attempt = 0; // Reset on success
                            backoff = _resilience.InitialBackoff;
                        }
                        finally {
                            lock (_lock) { _refreshCts = null; }
                        }

                        break; // Source completed normally
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                        throw;
                    }
                    catch (Exception ex) {
                        lastException = ex;
                        _logger?.LogDebug(ex, "Endpoint {Endpoint} failed, trying next", endpoint);
                    }
                }

                if (lastException != null && !ct.IsCancellationRequested) {
                    throw lastException; // All endpoints failed
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                break;
            }
            catch (Exception ex) {
                var level = GetLogLevelForAttempt(attempt);
                _logger?.Log(level, ex, "Topology source failed on attempt {Attempt}", attempt);

                if (attempt >= _resilience.MaxDiscoveryAttempts) {
                    _logger?.LogError("Max discovery attempts ({Max}) reached", _resilience.MaxDiscoveryAttempts);
                    throw new DiscoveryException(attempt, ex);
                }

                // Wait before retry (can be cancelled by Refresh())
                try {
                    using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    lock (_lock) { _refreshCts = delayCts; }
                    try {
                        await Task.Delay(backoff, delayCts.Token).ConfigureAwait(false);
                    }
                    finally {
                        lock (_lock) { _refreshCts = null; }
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
                    _logger?.LogDebug("Delay cancelled by refresh request");
                }

                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, _resilience.MaxBackoff.Ticks));
            }
        }
    }

    /// <summary>
    /// Gets the working set of endpoints: discovered nodes if available, otherwise seeds.
    /// Shuffles for load distribution.
    /// </summary>
    IReadOnlyList<DnsEndPoint> GetWorkingEndpoints() {
        var topology = _lastTopology;

        // Use discovered nodes if we have them
        DnsEndPoint[] endpoints;
        if (topology != null && topology.Nodes.Count > 0) {
            endpoints = topology.Nodes
                .Where(n => n.IsEligible)
                .Select(n => n.EndPoint)
                .ToArray();

            if (endpoints.Length > 0) {
                _logger?.LogTrace("Using {Count} discovered nodes for working set", endpoints.Length);
            }
            else {
                endpoints = _seeds;
                _logger?.LogTrace("No eligible discovered nodes, falling back to {Count} seeds", _seeds.Length);
            }
        }
        else {
            endpoints = _seeds;
            _logger?.LogTrace("No topology yet, using {Count} seeds", _seeds.Length);
        }

        // Shuffle for load distribution
        return Shuffle(endpoints);
    }

    static T[] Shuffle<T>(T[] array) {
        var result = new T[array.Length];
        Array.Copy(array, result, array.Length);
        Random.Shared.Shuffle(result);
        return result;
    }

    GrpcChannel CreateChannel(DnsEndPoint endpoint) {
        var scheme = _useTls ? "https" : "http";
        return GrpcChannel.ForAddress($"{scheme}://{endpoint.Host}:{endpoint.Port}");
    }

    static LogLevel GetLogLevelForAttempt(int attempt) => attempt switch {
        1 => LogLevel.Warning,
        _ => LogLevel.Debug
    };

    static void SetAttributes(BalancerAttributes attrs, ClusterNode node) {
        // Store full node for efficient extraction in picker
        attrs.Set(ClusterNodeAttributes.Node, node);
        // Also store individual values for debugging/logging
        attrs.Set(ClusterNodeAttributes.NodeId, node.NodeId);
        attrs.Set(ClusterNodeAttributes.Role, node.Role);
        attrs.Set(ClusterNodeAttributes.Status, node.Status);
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            _cts.Cancel();
            _cts.Dispose();
            _topologyChannel.Writer.TryComplete();
        }

        base.Dispose(disposing);
    }
}
