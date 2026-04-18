# gRPC Balancer Migration — Replacing the Legacy Channel Stack

- **Date:** 2026-04-18
- **Status:** Draft, awaiting approval
- **Author:** Sergio (design), Claude (drafting)
- **Scope:** `src/Kurrent.Client` — channel construction, discovery, leader routing, interceptor chain
- **Target TFMs:** net8.0, net9.0, net10.0 (net8.0 minimum)

## 1. Goal

Replace `KurrentDBLegacyCallInvoker`, `LegacyClusterClient`, `SharingProvider`, `ChannelCache`, the whole `Legacy/RipAndTear/Client/Gossip/Selectors` tree, `KurrentDBClientSettings`, and `KurrentDBLegacySettingsConverter` with a single `GrpcChannel` built on `Grpc.Net.Client`'s native balancer API, driven by a custom `PollingResolver` and one small `LeaderRedirectInterceptor`.

The new stack must **retain every operational capability of the current code** — specifically seed resolution, gossip-based cluster discovery, `NodePreference` filtering, leader redirect handling on write failures, auth injection, keep-alive, TLS options, user-supplied interceptors, and deadlines. This document enumerates every capability, where it currently lives, and where it lands in the new design.

## 2. Non-Goals

- Changing the public `KurrentClient` or module APIs.
- Changing `KurrentClientOptions`, `KurrentConnectionString`, or any connection-string parameter.
- Rewriting the V1 or V2 protocol definitions.
- Adding a DI-integrated `IHttpClientFactory` story (out of scope, can follow).
- Moving `ServerCapabilities` (explicitly out per user direction — capability probing is not in use).

## 3. Motivation

### 3.1 Why the current stack is a bottleneck

- **Sync-over-async on every call.** `KurrentDBLegacyCallInvoker.WithChannelInfoInvoker` does `Task.Run(...).GetAwaiter().GetResult()` inside every one of `AsyncUnaryCall`, `BlockingUnaryCall`, `AsyncClientStreamingCall`, `AsyncServerStreamingCall`, `AsyncDuplexStreamingCall`. Every single gRPC call goes through a thread-pool hop and two blocking awaits. Under reconnection it blocks one thread per concurrent call for the full gossip loop.
- **~200-line `SharingProvider` coordination primitive** solving a problem the balancer API now solves natively (deduplicate concurrent reconnection, publish the current channel).
- **Dead code.** `TypedExceptionInterceptor` is never instantiated (`LegacyClusterClient` always passes `exceptionMap: []`). `ForceRefresh` is unreachable (only caller is commented out). `ChannelOptions` property has no external readers. `GossipTimeout` is populated but never consumed. `LegacyClusterClient.CreateWithExceptionMapping()` is commented out. `ServerCapabilities.SupportsSchemaRegistry` is never set.
- **Sharp edges.** `Random(0)` constant-seeded shuffle in `ChannelCache.cs:15`. Fire-and-forget `DisposeChannelsAsync` at `ChannelCache.cs:76`. Re-entrant `_lock` in `ChannelCache.UpdateCache → CreateChannel`. Flat 100 ms retry with no backoff/jitter — floods seeds under partition. `LeaderNotFoundInterceptor` does not override `BlockingUnaryCall` so sync callers never trigger leader-change reconnection. `KurrentDBClientRetrySettings.InitialBackoff = TimeSpan.FromSeconds(250)` looks like a ms-vs-s bug.
- **Two parallel option records.** `KurrentClientOptions` → `ConvertToLegacySettings` → `KurrentDBClientSettings`. The legacy record only exists because `LegacyClusterClient` consumes it. Removing the legacy layer lets us delete the converter and the legacy record.

### 3.2 What the server gives us (and why that changes the design)

- **V2 writes always set `requireLeader: true`.** `KurrentDB.Api.V2/Modules/Streams/StreamsService.cs:124, :286`. The server does not read the `requires-leader` header on V2 — there is no way to opt into V1-style TCP forwarding for V2 writes. On a non-leader node, V2 writes **always** deny.
- **Denial carries a rich structured payload.** `ApiCallback.cs:98` maps `NotHandled(NotLeader)` to `ApiErrors.NotLeaderNode(leaderId, leaderHttp)`, which at `ApiErrors.cs:291-309` returns `RpcException` with:
  - `Status.Code` = `FAILED_PRECONDITION` (via `ServerError.NotLeaderNode = 5`, from `proto/kurrent/rpc/errors.proto`).
  - `Status.Details` containing `Kurrent.Rpc.NotLeaderNodeErrorDetails { CurrentLeader = { NodeId, Host, Port } }` (`proto/kurrent/rpc/errors.proto`).
- **V1 writes still support TCP-level forwarding** when the client sets `requires-leader=false` (`ClusterVNodeController.cs:829-838`). We will continue to inject `requires-leader` based on `NodePreference`. This preserves V1 behavior exactly.
- **Gossip RPC has no leader check.** Any node answers (`KurrentDB.Core/Services/Transport/Grpc/Gossip.Read.cs:26`). Seeds do not need to be pre-filtered to the leader.
- **Reads with `requires-leader=false` served locally by any node.** Data may be slightly stale; no error. Preserved.
- **Persistent subscription Connect/Create/Update/Delete always deny on non-leader**, no forwarding path (`ClusterVNodeController.cs:773-826`). Requires leader-redirect to succeed.
- **Additional server error codes.** `ServerError.ServerNotReady = 7` → `UNAVAILABLE`, `ServerOverloaded = 8` → `UNAVAILABLE`, `LeaderForwardingTimeout = 9` → `DEADLINE_EXCEEDED`. These want retry on the client side, matching today's implicit behavior (current code does not handle them explicitly — this is an improvement the new design will pick up for free via gRPC's built-in retry policy on `Unavailable`).

**Consequence:** the server already tells the client exactly which node to retry against, via a typed proto payload. We do not need `NodePreference.Leader` filtering up-front at connection time — the first write can go to any node and the server redirect covers the rest. Gossip remains useful for bootstrapping a multi-seed connection and (later, optionally) for spreading reads across replicas.

## 4. Current State Inventory (the capability audit)

### 4.1 Callers of `KurrentDBLegacyCallInvoker`

Single shared-singleton pattern. One invoker instance per `KurrentClient`, passed to every gRPC stub:

| Caller | File:Line |
|---|---|
| Construction | `src/Kurrent.Client/KurrentClient.cs:35` |
| `LegacyCallInvoker` property | `KurrentClient.cs:65` |
| `KurrentBackdoorClientFactory` param | `KurrentBackdoorClientFactory.cs:18, 34, 57` |
| `StreamsServiceClient`, `LegacyStreamsServiceClient` | `Modules/Streams/StreamsClient.cs:13, 14` |
| `SchemaRegistryServiceClient` | `Modules/Registry/RegistryClient.cs:15` |
| `IndexesServiceClient` | `Modules/Indexes/IndexesClient.cs:11` |
| `ConnectorsCommandServiceClient`, `ConnectorsQueryServiceClient` | `Modules/Connectors/ConnectorsClient.cs:8, 9` |
| `OperationsServiceClient` (Admin) | `Modules/Admin/AdminClient.cs:11` |
| DisposeAsync | `KurrentClient.cs:92` |

### 4.2 Capability inventory (every concrete behavior today)

| # | Capability | Where (legacy) | Notes |
|---|---|---|---|
| C1 | Parse connection string to options | `KurrentConnectionString.cs` → `ToClientOptions()` | Modern side, already correct |
| C2 | Validate endpoints, schemes | `KurrentClientOptions.Validator` | Modern side, already correct |
| C3 | Resolve seeds from connection string | `KurrentDBClientConnectivitySettings.GossipSeeds` | Populated by converter |
| C4 | Single-node vs discovery mode dispatch | `LegacyClusterClient` ctor `IsSingleNode` branch | Direct vs Discover scheme |
| C5 | Gossip RPC call to retrieve cluster members | `GrpcGossipClient.GetAsync` | Uses `EventStore.Client.Gossip` stub, V1 |
| C6 | Parse cluster members into `MemberInfo` / `VNodeState` | `ClusterMessage.cs` | Direct integer cast of state enum |
| C7 | Filter and sort cluster members by `NodePreference` | `NodeSelector.SelectNode`, `NodePreferenceComparers` | Drops 11 unusable states; random tie-break |
| C8 | Cycle seeds in random order | `GossipChannelSelector.DiscoverAsync` + `ChannelCache.GetRandomOrderSnapshot` | `Random(0)` seed bug |
| C9 | Retry gossip across seeds with `MaxDiscoverAttempts` | `GossipChannelSelector.DiscoverAsync` | Flat `DiscoveryInterval`, no backoff |
| C10 | Throw `DiscoveryException` when all seeds fail | `GossipChannelSelector.cs:74` | Propagates through `SharingProvider` |
| C11 | Re-seed from connection string on each outer attempt | `GossipChannelSelector.cs:64` | Protects against cache drift |
| C12 | Update channel cache from gossip response | `ChannelCache.UpdateCache` | Disposes evicted channels |
| C13 | Key channels by `DnsEndPoint` | `ChannelCache.DnsEndPointEqualityComparer` | Host case-insensitive |
| C14 | Build `GrpcChannel` with TLS, keep-alive, `SocketsHttpHandler` | `ChannelFactoryExtensions.CreateChannel` | Also enables HTTP/2 multi-connection |
| C15 | Apply `TransportSecurity` (None/Insecure/Standard/Certificate) | `KurrentClientSecurityOptions` → converter → channel options | Certificate loads root CA |
| C16 | Apply client cert for mTLS | `ClientCredentials.Certificate` → `ClientCertificate` | |
| C17 | Inject `Authorization` header (basic or per-call) | `AuthenticationInterceptor` | Per-call, blocks on credential delegate |
| C18 | Inject `requires-leader` header per call | `HeadersInterceptor` constructed at `LegacyClusterClient.cs:81-83` | Value derived from `NodePreference` |
| C19 | Inject client identity headers (name, version, connection-name) | `HeadersInterceptor` at `LegacyClusterClient.cs:85-89` | One-shot per channel |
| C20 | Detect `NotLeader` and reconnect | `LeaderNotFoundInterceptor` → `TriggerReconnect(NewLeader)` | V1 trailer-based detection |
| C21 | Deduplicate concurrent reconnection requests | `SharingProvider.OnBroken` CAS loop | |
| C22 | Cache current `ChannelInfo` across calls | `SharingProvider._currentBox` | |
| C23 | Retry gossip factory on failure with delay | `SharingProvider.FillBoxAsync` catch | `DiscoveryInterval` flat |
| C24 | Apply user-supplied `Interceptors` | `LegacyClusterClient.cs:96-98` | |
| C25 | Apply `DefaultDeadline` to calls | Threaded via `KurrentDBCallOptions` | Set in converter |
| C26 | Keep-alive interval/timeout on HTTP/2 | `ChannelFactoryExtensions` → `SocketsHttpHandler.KeepAlivePing*` | |
| C27 | Force-refresh on demand | `KurrentDBLegacyCallInvoker.ForceRefresh` | Currently unreachable — caller commented out |
| C28 | Fetch server capabilities | `GrpcServerCapabilitiesClient.GetAsync` | **Explicitly out of scope — not in use** |
| C29 | Dispose all channels on client dispose | `KurrentDBLegacyCallInvoker.DisposeAsync` → `LegacyClusterClient.DisposeAsync` → `ChannelCache.DisposeAsync` | Via `SharingProvider.Dispose` |
| C30 | Logger factory propagation | Settings → `SharingProvider`/selector loggers | |

## 5. New Architecture

### 5.1 Shape

```
KurrentClientOptions
    │
    ├─► KurrentClientChannelFactory.Create(options)  [static, ~40 lines]
    │       │
    │       ├─ register KurrentResolverFactory on scheme "kurrentdb+discover"
    │       │     (single-node uses built-in static/dns resolver)
    │       │
    │       ├─ build SocketsHttpHandler (TLS + keepalive + mTLS)
    │       │
    │       ├─ build GrpcChannelOptions:
    │       │     Credentials              = TLS or Insecure
    │       │     HttpHandler              = SocketsHttpHandler above
    │       │     ServiceConfig            = { LoadBalancing: pick_first,
    │       │                                 RetryPolicy: on UNAVAILABLE }
    │       │     UnsafeUseInsecureChannelCallCredentials = true (basic auth over TLS & plain)
    │       │
    │       └─ GrpcChannel.ForAddress(uri, options)
    │
    └─► GrpcChannel  (single instance per KurrentClient)
            │
            └─ channel.Intercept(
                  RequiresLeaderHeaderInterceptor,     // C18
                  ClientIdentityHeaderInterceptor,     // C19
                  LeaderRedirectInterceptor,           // C20 (new, payload-driven)
                  AuthenticationInterceptor,           // C17 (kept, unchanged)
                  ...user-supplied interceptors        // C24
              )
            │
            └─► every module's gRPC stub constructed from the intercepted invoker
```

Every module (`StreamsClient`, `RegistryClient`, `IndexesClient`, `ConnectorsClient`, `AdminClient`, `KurrentBackdoorClientFactory`) takes the intercepted `CallInvoker` directly. No custom `CallInvoker` subclass.

### 5.2 KurrentResolver (PollingResolver subclass)

Handles scheme `kurrentdb+discover://`. Registered via `ResolverFactory` on channel construction.

**Responsibilities:**
- Own a list of current cluster `BalancerAddress` entries.
- On initial `ResolveAsync`: iterate seed endpoints, call `Kurrent.Grpc.V1.Gossip/Read`, parse `MemberInfo[]`, filter via `NodeSelector`/`NodePreferenceComparers` (C7 — reused verbatim), publish result.
- On periodic refresh (`PollingResolver` base): same, with `MinimumRefreshInterval = Gossip.DiscoveryInterval` (C23).
- On `Refresh()` triggered by interceptor: use stashed leader hint (see 5.3) to bias the next resolve.
- Public method: `HintLeader(Host, Port)` — sets a one-shot bias that the next resolve will prefer.
- Errors: when all seeds fail for `MaxDiscoverAttempts`, publish `ResolverResult.ForFailure(Status.Unavailable(...))`. The balancer surfaces this to callers as `RpcException(Unavailable)` (C10 equivalent, but routed through idiomatic gRPC types instead of `DiscoveryException`).

**Single-node mode** (`KurrentConnectionScheme.Direct`): we do NOT register `KurrentResolverFactory`. We use the built-in `StaticResolverFactory` with the single endpoint (C4 preserved). No gossip, no `NodePreference`.

**NodePreference semantics (C7 preserved):** the resolver returns **one** selected address on every resolve — the node picked by the filter + comparer. `pick_first` then keeps a single live subchannel there. This matches current behavior exactly (today's `GossipChannelSelector` also picks one).

**Future extension (not in this change):** returning N addresses with `round_robin` for multi-node read fan-out. Out of scope here — would be a follow-up when reads want to balance.

### 5.3 LeaderRedirectInterceptor (~60 lines, payload-driven)

Replaces `LeaderNotFoundInterceptor`. Does two things:

1. **On every faulted call**: inspect `RpcException`. If `StatusCode == FailedPrecondition` AND `ex.GetRpcStatus()?.GetDetail<NotLeaderNodeErrorDetails>()` is non-null, read `CurrentLeader.Host` and `CurrentLeader.Port`, call `resolver.HintLeader(host, port)`, then `resolver.Refresh()`.
2. **Also handle V1 trailer-based NotLeader** (`rex.IsLegacyError("not-leader")` with `leader-endpoint-host`/`leader-endpoint-port`) for backward compatibility with V1 server responses. The modern and legacy detection paths coexist inside one interceptor.

**Does it retry the call?** No, matching today's `LeaderNotFoundInterceptor` behavior (C20). The failing call still throws to the caller; the next call lands on the new leader once the resolver updates. Callers wanting transparent retry compose Polly or gRPC `ServiceConfig.RetryPolicy` externally.

**Streams:** overrides `AsyncUnaryCall`, `AsyncClientStreamingCall`, `AsyncServerStreamingCall`, `AsyncDuplexStreamingCall`, AND `BlockingUnaryCall` (fixing a current bug in `LeaderNotFoundInterceptor` — C20+).

### 5.4 Interceptor chain (ordered, applied once at channel construction)

| Order | Interceptor | Purpose | Retention source |
|---|---|---|---|
| 1 | `RequiresLeaderHeaderInterceptor` | Injects `requires-leader: true|false` based on `NodePreference` | C18 — split out from legacy `HeadersInterceptor` for clarity |
| 2 | `ClientIdentityHeaderInterceptor` | Injects `kurrentdb-client-name`, `-version`, `-connection-name` | C19 — split from legacy headers interceptor |
| 3 | `LeaderRedirectInterceptor` | Payload + trailer leader redirect → resolver hint + refresh | C20 replacement |
| 4 | `AuthenticationInterceptor` | Per-call `Authorization` header from `ClientCredentials` | C17 — kept as-is, already works |
| 5 | `options.Interceptors[]` | User-supplied | C24 |

`TypedExceptionInterceptor` is **NOT** included — current code passes empty map and never instantiates it. Dead code.

### 5.5 Channel construction detail

`KurrentClientChannelFactory.Create(KurrentClientOptions options)`:

```
uri = options.ConnectionScheme switch {
    Direct   => new Uri($"{HttpUriScheme}://{Endpoints[0].Host}:{Endpoints[0].Port}"),
    Discover => new Uri($"kurrentdb+discover://{ConnectionName}")   // scheme triggers KurrentResolverFactory
};

handler = new SocketsHttpHandler {
    PooledConnectionIdleTimeout     = Timeout.InfiniteTimeSpan,
    KeepAlivePingDelay              = Resilience.KeepAliveInterval,   // C26
    KeepAlivePingTimeout            = Resilience.KeepAliveTimeout,    // C26
    KeepAlivePingPolicy             = HttpKeepAlivePingPolicy.Always,
    EnableMultipleHttp2Connections  = true,
    SslOptions                      = BuildSslOptions(options.Security),   // C15, C16
};

channelOptions = new GrpcChannelOptions {
    Credentials         = options.Security.Transport.IsEnabled ? ChannelCredentials.SecureSsl : ChannelCredentials.Insecure,
    HttpHandler         = handler,
    LoggerFactory       = options.LoggerFactory,                       // C30
    ServiceConfig       = new ServiceConfig {
        LoadBalancingConfigs = { new PickFirstConfig() },
        MethodConfigs = {
            new MethodConfig {
                Names = { MethodName.Default },
                RetryPolicy = new RetryPolicy {
                    MaxAttempts       = 3,
                    InitialBackoff    = TimeSpan.FromMilliseconds(200),
                    MaxBackoff        = TimeSpan.FromSeconds(2),
                    BackoffMultiplier = 2.0,
                    RetryableStatusCodes = { StatusCode.Unavailable }   // server_not_ready, overloaded
                }
            }
        }
    },
    ResolverFactoryRegistrations = { new KurrentResolverFactory(options) },  // only on Discover
    UnsafeUseInsecureChannelCallCredentials = true                           // permit basic auth over plain HTTP (dev)
};

return GrpcChannel.ForAddress(uri, channelOptions);
```

### 5.6 Lifecycle

- `KurrentClient` owns the channel and disposes it on `DisposeAsync` (C29).
- Channel disposal shuts down all subchannels cleanly (built-in).
- Resolver disposes via `IAsyncDisposable` (C29 subset).
- User-supplied `ClientCredentials` delegates are held only by `AuthenticationInterceptor` (no change from today).

## 6. Capability Mapping (C1-C30 → new home)

| # | Capability | New home | Status |
|---|---|---|---|
| C1 | Parse connection string | `KurrentConnectionString.ToClientOptions()` | Unchanged |
| C2 | Validate options | `KurrentClientOptions.Validator` | Unchanged |
| C3 | Resolve seeds | `KurrentResolverFactory` consumes `Endpoints` directly | Kept, simpler path |
| C4 | Single-node vs discovery dispatch | Scheme switch in `KurrentClientChannelFactory` | Kept, clearer |
| C5 | Gossip RPC | `KurrentResolver.ResolveAsync` → same `Gossip/Read` RPC | **File kept**: `GrpcGossipClient.cs` reused |
| C6 | Parse cluster members | `ClusterMessage.cs` | **Kept verbatim** |
| C7 | NodePreference filter/sort | `NodeSelector.cs`, `NodePreferenceComparers.cs` | **Kept verbatim**, called from resolver |
| C8 | Seed shuffle | `KurrentResolver` with a proper `Random.Shared.Next` | Kept, constant-seed bug fixed |
| C9 | MaxDiscoverAttempts loop | `KurrentResolver` | Kept, exponential backoff added (improvement) |
| C10 | DiscoveryException on exhaustion | `ResolverResult.ForFailure(Status.Unavailable)` | Replaced with idiomatic gRPC surface; `DiscoveryException` type deleted |
| C11 | Re-seed from connection string | Resolver holds `Endpoints` reference | Kept |
| C12 | Update channel cache from gossip | **Removed**: balancer owns subchannels | Superseded by `pick_first`; no cache needed |
| C13 | Channel keying | **Removed**: balancer-internal | |
| C14 | GrpcChannel construction | `KurrentClientChannelFactory.Create` | Consolidated |
| C15 | TransportSecurity modes | `BuildSslOptions(Security.Transport)` in factory | Kept |
| C16 | mTLS client cert | `BuildSslOptions(Security.Authentication)` | Kept |
| C17 | Authentication header | `AuthenticationInterceptor` | **Kept as-is** |
| C18 | requires-leader header | `RequiresLeaderHeaderInterceptor` | Kept, split from legacy headers bundle |
| C19 | Client identity headers | `ClientIdentityHeaderInterceptor` | Kept, split from legacy headers bundle |
| C20 | Leader redirect detection | `LeaderRedirectInterceptor` | Upgraded: reads protobuf payload first (V2), falls back to V1 trailers; also covers `BlockingUnaryCall` (current bug fix) |
| C21 | Deduplicate reconnect | Balancer subchannel state machine | Built-in |
| C22 | Cache current channel | `GrpcChannel` (single instance) | Built-in |
| C23 | Retry gossip with delay | `PollingResolver.MinimumRefreshInterval` + exponential retry in resolver loop | Kept, semantics clearer |
| C24 | User interceptors | `channel.Intercept(... options.Interceptors)` | Kept |
| C25 | DefaultDeadline | Consumer-side `CallOptions.Deadline` computed from `Resilience.Deadline` — **existing mechanism unchanged**, does not live in channel stack | Kept |
| C26 | Keep-alive | `SocketsHttpHandler.KeepAlivePing*` | Kept |
| C27 | Force refresh | Expose `KurrentClient.RefreshCluster()` → `resolver.Refresh()` | Kept and actually reachable (today's is dead) |
| C28 | ServerCapabilities | **Explicitly out of scope per user direction** | Drop caller from `ClientModuleBase`, delete `GrpcServerCapabilitiesClient` |
| C29 | Dispose | `GrpcChannel.Dispose()` + resolver | Kept |
| C30 | Logger factory | `GrpcChannelOptions.LoggerFactory` + resolver logger | Kept |

**Every capability is preserved or explicitly excluded with reason.** No silent loss.

## 7. File-by-File Disposition

### 7.1 Delete (with reason)

| File | Why |
|---|---|
| `Legacy/KurrentDBLegacyCallInvoker.cs` | Replaced by raw `GrpcChannel` + interceptor chain. Removes sync-over-async. |
| `Legacy/RipAndTear/Client/LegacyClusterClient.cs` | Functionality moved to `KurrentResolver` + channel factory. |
| `Legacy/RipAndTear/Client/Gossip/SharingProvider.cs` | Balancer owns the equivalent state machine. |
| `Legacy/RipAndTear/Client/Gossip/ChannelCache.cs` | Balancer manages subchannels. `Random(0)` bug goes away with it. |
| `Legacy/RipAndTear/Client/Gossip/ReconnectionRequired.cs` | No longer needed — resolver's `Refresh()` + `HintLeader` replaces the DU. |
| `Legacy/RipAndTear/Client/Gossip/Selectors/IChannelSelector.cs` | Interface no longer needed. |
| `Legacy/RipAndTear/Client/Gossip/Selectors/ChannelSelector.cs` | Base class no longer needed. |
| `Legacy/RipAndTear/Client/Gossip/Selectors/GossipChannelSelector.cs` | Loop moves into `KurrentResolver.ResolveAsync`. |
| `Legacy/RipAndTear/Client/Gossip/Selectors/SingleNodeChannelSelector.cs` | Built-in `StaticResolverFactory` covers it. |
| `Legacy/RipAndTear/Client/Gossip/ChannelFactoryExtensions.cs` | Folded into `KurrentClientChannelFactory`. |
| `Legacy/RipAndTear/Client/Gossip/SingleNodeHttpHandler.cs` | Scheme-rewrite hack no longer needed — channel URI is authoritative. |
| `Legacy/RipAndTear/Client/Gossip/GrpcServerCapabilitiesClient.cs` | C28 out of scope. |
| `Legacy/RipAndTear/Client/Gossip/IServerCapabilitiesClient.cs` | C28 out of scope. |
| `Legacy/RipAndTear/Client/Gossip/ServerCapabilities.cs` | C28 out of scope. |
| `Legacy/RipAndTear/Client/Exceptions/DiscoveryException.cs` | Replaced by idiomatic `RpcException(Unavailable)`. |
| `Legacy/RipAndTear/Client/Interceptors/LeaderNotFoundInterceptor.cs` | Replaced by `LeaderRedirectInterceptor`. |
| `Legacy/RipAndTear/Client/Interceptors/TypedExceptionInterceptor.cs` | Never instantiated today — dead code. |
| `Legacy/RipAndTear/Client/Settings/KurrentDBClientSettings.cs` | No more legacy settings record. |
| `Legacy/RipAndTear/Client/Settings/KurrentDBClientConnectivitySettings.cs` | Ditto. |
| `Legacy/RipAndTear/Client/Settings/KurrentDBClientOperationOptions.cs` | `BatchAppendSize` never configurable, `ThrowOnAppendFailure` not used. |
| `Legacy/RipAndTear/Client/Settings/KurrentDBClientRetrySettings.cs` | Replaced by `ServiceConfig.RetryPolicy` + consumer-side Polly. `InitialBackoff = 250s` bug deleted with it. |
| `Legacy/RipAndTear/Client/KurrentDBCallOptions.cs` | If deadline wiring is here, fold into per-module call sites. (Verify during implementation.) |
| `Legacy/KurrentDBLegacySettingsConverter.cs` | No more legacy settings record to convert to. |

### 7.2 Keep (with reason and new location if moved)

| File | Reason | Moved? |
|---|---|---|
| `Legacy/RipAndTear/Client/Gossip/NodeSelector.cs` | C7 — preference filter/sort is correct and reusable | Move to `Internal/Cluster/` |
| `Legacy/RipAndTear/Client/Gossip/NodePreferenceComparers.cs` | C7 — idem | Move to `Internal/Cluster/` |
| `Legacy/RipAndTear/Client/Gossip/NodePreference.cs` | Public API referenced by `KurrentClientGossipOptions.ReadPreference` | Move to `Options/` |
| `Legacy/RipAndTear/Client/Gossip/GrpcGossipClient.cs` | C5 — still the gossip RPC client | Move to `Internal/Cluster/` |
| `Legacy/RipAndTear/Client/Gossip/IGossipClient.cs` | Abstraction used by tests | Move to `Internal/Cluster/` |
| `Legacy/RipAndTear/Client/Gossip/ClusterMessage.cs` | C6 — V1 proto projection | Move to `Internal/Cluster/` |
| `Legacy/RipAndTear/Client/Gossip/EndPointExtensions.cs` | Helpers for gossip | Move to `Internal/Cluster/` |
| `Legacy/RipAndTear/Client/Interceptors/AuthenticationInterceptor.cs` | C17 — works as-is | Move to `Internal/Interceptors/` |
| `Legacy/RipAndTear/Client/Constants.cs` | Header constants still used | Move to `Internal/Constants.cs`; prune dead headers (`OsName`, `OsVersion`, `RuntimeName`, `RuntimeVersion`, `HostArchitecture` — unused) and dead legacy error codes |
| `Legacy/RipAndTear/UserCredentials.cs`, `Uuid.cs`, `HashCode.cs`, `TaskExtensions.cs`, `EpochExtensions.cs`, `X509Certificates.cs`, `StreamIdentifier.cs` | Used by V1 proto plumbing still referenced by legacy streams RPC stub | Keep in `Legacy/` folder (out of scope for this change) |

### 7.3 Add

| File | Purpose |
|---|---|
| `Internal/Channel/KurrentClientChannelFactory.cs` | Builds the `GrpcChannel` from `KurrentClientOptions`. |
| `Internal/Cluster/KurrentResolver.cs` | `PollingResolver` subclass doing gossip + NodePreference. |
| `Internal/Cluster/KurrentResolverFactory.cs` | Registered for the `kurrentdb+discover` scheme. |
| `Internal/Interceptors/LeaderRedirectInterceptor.cs` | Payload-driven leader redirect. |
| `Internal/Interceptors/RequiresLeaderHeaderInterceptor.cs` | Split from legacy headers bundle. |
| `Internal/Interceptors/ClientIdentityHeaderInterceptor.cs` | Split from legacy headers bundle. |

## 8. Settings / Connection String Mapping

Every connection-string parameter (from `KurrentConnectionString.ToClientOptions()`) continues to be supported. No changes to the parser or options surface.

| Connection string param | KurrentClientOptions path | New consumer |
|---|---|---|
| scheme (`kurrentdb[+discover]`, aliases) | `ConnectionScheme` | `KurrentClientChannelFactory` |
| `host:port[,host:port...]` | `Endpoints[]` | `KurrentResolver` seeds |
| `user:pass@` | `Security.Authentication = ClientCredentials.Basic(...)` | `AuthenticationInterceptor` |
| `ConnectionName` | `ConnectionName` | `ClientIdentityHeaderInterceptor` |
| `Tls`, `TlsVerifyCert`, `TlsCaFile` | `Security.Transport` | `BuildSslOptions` in factory |
| `UserCertFile`, `UserKeyFile` | `Security.Authentication = ClientCredentials.Certificate(...)` | `BuildSslOptions` in factory |
| `KeepAliveInterval`, `KeepAliveTimeout` | `Resilience.KeepAliveInterval`, `KeepAliveTimeout` | `SocketsHttpHandler` |
| `DefaultDeadline` | `Resilience.Deadline` | consumer-side `CallOptions` (unchanged) |
| `MaxDiscoverAttempts` | `Gossip.MaxDiscoverAttempts` | `KurrentResolver` |
| `DiscoveryInterval` | `Gossip.DiscoveryInterval` | `KurrentResolver.MinimumRefreshInterval` |
| `GossipTimeout` | `Gossip.Timeout` | `KurrentResolver` — **newly consumed** (was dead in legacy, now honored as per-seed gossip call deadline) |
| `NodePreference` | `Gossip.ReadPreference` | `NodeSelector` in resolver |

**Note on improvements:** `GossipTimeout` becomes load-bearing (small capability gain, not a regression). `InitialBackoff = 250s` legacy bug removed. `Random(0)` shuffle bug removed.

## 9. Error Surface (before/after)

| Scenario | Today | After |
|---|---|---|
| All seeds fail during discovery | `DiscoveryException(maxAttempts)` wrapped in user exception | `RpcException(StatusCode.Unavailable, "discovery failed: <detail>")` |
| Leader election in progress (no leader yet) | Same `DiscoveryException` or eventual success after retry | Same — resolver keeps polling |
| V1 write to follower, `requires-leader=false` | Server TCP-forwards to leader, write succeeds (transparent) | Unchanged — header interceptor still sets `false` when `NodePreference != Leader` |
| V1 write to follower, `requires-leader=true` | `RpcException(NotFound)` + trailers `not-leader`, `leader-endpoint-host/port` → legacy interceptor reconnects | Same trailers path still handled in `LeaderRedirectInterceptor` (fallback) |
| V2 write to follower | `RpcException(FailedPrecondition)` + `NotLeaderNodeErrorDetails` protobuf payload | `LeaderRedirectInterceptor` extracts `CurrentLeader.Host/Port`, hints resolver, refreshes |
| Server overloaded / not ready | `RpcException(Unavailable)` → caller retries manually today | Same exception, but `ServiceConfig.RetryPolicy` now retries automatically up to 3 times (improvement) |
| Forwarding timeout | `RpcException(DeadlineExceeded)` | Unchanged |
| BlockingUnaryCall hits NotLeader | **Leader change not detected today** (bug) | Handled by new interceptor |

## 10. Migration Plan

Single PR, mechanical order:

1. **Add new files** (`KurrentClientChannelFactory`, `KurrentResolver[Factory]`, 3 interceptors). Tests for each.
2. **Move kept files** to `Internal/Cluster/` and `Internal/Interceptors/`. Update namespaces. Fix references.
3. **Wire `KurrentClient.cs`** to use `KurrentClientChannelFactory.Create(options)` instead of `new KurrentDBLegacyCallInvoker(options)`. Pass `channel.Intercept(...).CreateCallInvoker()` to every module ctor.
4. **Update `KurrentBackdoorClientFactory`** to read `channel.Target` directly instead of `invoker.ChannelTarget`.
5. **Run the full test suite** (unit + integration). Integration tests hit a real container — this is the meaningful validation.
6. **Delete the legacy files** listed in 7.1. Remove from `.csproj` if explicitly listed.
7. **Prune `Constants.cs`** of unused headers and dead error codes.
8. **Update `ClientModuleBase`** to drop the `ServerCapabilities` accessor (C28).

No gradual rollout. Greenfield project, single branch, one PR.

## 11. Testing Strategy

- **Unit tests** (TUnit, mocks):
  - `KurrentResolver.ResolveAsync` — happy path (gossip succeeds), all-seeds-fail path, leader-hint path, `NodePreference` variants.
  - `LeaderRedirectInterceptor` — V2 payload path, V1 trailer path, non-leader error ignored, resolver hint is called.
  - Header interceptors — header presence, values from options.
- **Integration tests** (real KurrentDB container via FluentDocker):
  - Single-node connect, basic auth, mTLS.
  - Multi-node cluster: write lands on leader transparently, reads respect `NodePreference`, leader kill triggers resolver refresh and subsequent calls succeed on the new leader.
  - V1 and V2 RPCs both exercised (streams V2 append + legacy reads).
  - Server restart — reconnect works without manual intervention.
  - User-supplied `Interceptors` still invoked.
- **Delete obsolete tests** that target `SharingProvider`, `ChannelCache`, `GossipChannelSelector` directly. Behavior is now covered end-to-end.

## 12. Risks

| Risk | Mitigation |
|---|---|
| `GrpcChannel` + `PollingResolver` interaction with `HttpHandler` on older .NET versions | Targeting net8.0 minimum; grpc-dotnet 2.76 balancer API is mature. Integration tests on all three TFMs. |
| `LeaderRedirectInterceptor` race: two concurrent writes both fail, both hint different (stale) leaders | Resolver stores last-hint and always re-validates via next poll. If the second hint is also stale, another redirect arrives — self-correcting. |
| `ServiceConfig.RetryPolicy` retries writes unsafely | Writes to KurrentDB are idempotent at the consistency-check level (expected revision). `Unavailable` retry is safe. No retry on `FailedPrecondition`. |
| Loss of sync-call leader redirect semantics (C20 gap) | Actually a gain — new interceptor also covers `BlockingUnaryCall`. |
| `KurrentClientChannelFactory` hard-codes `pick_first` | Future work: switch to `round_robin` when the resolver returns multi-node lists. Documented, not done here. |
| Resolver-side gossip uses V1 RPC — if V1 stub ever retires | V1 gossip RPC is load-bearing server-side (any node can serve). Retirement would require server V2 gossip first. Out of scope. |

## 13. Open Questions

1. **Polly integration at the consumer module level?** Currently out of scope — `ServiceConfig.RetryPolicy` covers `Unavailable`. If we want user-tunable retry semantics, we add a `KurrentClientResilienceOptions.RetryPolicy` knob later and translate to `ServiceConfig` at channel-build time. Non-blocking for this change.
2. **Expose `KurrentClient.RefreshCluster()` publicly?** Resurrects C27. Cheap, useful, and zero cost if unused. Default: yes, add it.
3. **`kurrentdb+discover` scheme collision risk.** `GrpcChannel.ForAddress` treats schemes as URL schemes. Registering a custom `ResolverFactory` for `kurrentdb+discover` is standard. Single-node uses regular `https://` or `http://`. Confirmed supported by grpc-dotnet 2.76.

## 14. Approval Required

Please confirm:
- Full capability list C1-C30 is complete (add any I missed).
- File deletion list in §7.1 is acceptable.
- C28 (`ServerCapabilities`) is genuinely out of scope — we delete the probe and the accessor.
- Open question 2 — add public `RefreshCluster()` method.

Once approved, implementation tracked under task #1 (resolver + interceptor), cleanup under task #3.
