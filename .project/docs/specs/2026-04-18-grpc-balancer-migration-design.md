# gRPC Channel Stack Replacement — Ultimate Implementation Plan

- **Date:** 2026-04-18
- **Status:** Validated draft, awaiting sign-off
- **Author:** Sergio (design), Claude (drafting and validation)
- **Scope:** `src/Kurrent.Client` — channel construction, discovery, leader routing, interceptor chain
- **Target TFMs:** net8.0, net9.0, net10.0 (net8.0 minimum)
- **Supersedes:** this file's prior draft from earlier today (validation revisions folded in)

## 1. Goal

Replace the entire legacy gRPC channel stack (`KurrentDBLegacyCallInvoker`, `LegacyClusterClient`, `SharingProvider`, `ChannelCache`, the `Gossip/` tree, the legacy settings records, the legacy converter) with a single `GrpcChannel` built on `Grpc.Net.Client`'s native balancer API, driven by a custom `PollingResolver` and one `LeaderRedirectInterceptor` that handles both V2 protobuf `NotLeaderNodeErrorDetails` and V1 trailer-based `not-leader` responses.

Greenfield — no backwards-compat obligations. Delete everything that isn't required for the new stack. Only proto-interop helpers that are used outside the channel stack survive (relocated out of `Legacy/`).

## 2. Decisions (confirmed)

| # | Decision | Answer |
|---|---|---|
| D1 | `ServerCapabilities` probe + accessor | **Delete entirely.** No module reads any capability flag today; only consumer (`KurrentClient.cs:82`) is commented out. |
| D2 | Public `KurrentClient.RefreshCluster()` | **Do not add.** Resolver self-refreshes; interceptor triggers refresh on leader redirect; external triggers unnecessary. |
| D3 | V1 trailer fallback in new interceptor | **Keep.** `StreamsClient` reads/subscriptions/manage (V1 legacy stub) and `AdminClient` (V1 Operations) still emit V1 trailers. Also covers future revival of PersistentSubs/Projections/Users. |
| D4 | `NodePreference` surfaces from legacy namespace | **Relocate to public `Kurrent.Client` namespace.** Already exposed via `KurrentClientGossipOptions.ReadPreference`. |
| D5 | Proto-interop helpers (`Uuid`, `EpochExtensions`, `StreamIdentifier`, `Metadata` constants, legacy error codes + extensions) | **Relocate, don't delete.** Load-bearing in V1 proto mappers in both `Kurrent.Client` and `Kurrent.Processors`. |

## 3. Motivation — grounded in validated reality

### 3.1 What's actually broken today (not just overbuilt)

These are not hypothetical — they are verified in the current code:

- **Sync-over-async on every gRPC call.** `KurrentDBLegacyCallInvoker.WithChannelInfoInvoker` (`KurrentDBLegacyCallInvoker.cs:121-124`) executes `Task.Run(...).GetAwaiter().GetResult()` on every `AsyncUnaryCall`, `BlockingUnaryCall`, `AsyncClientStreamingCall`, `AsyncServerStreamingCall`, `AsyncDuplexStreamingCall`. Under reconnection, one thread blocks per concurrent call.

- **Every call calls `Connect()` unconditionally** (`KurrentDBLegacyCallInvoker.cs:128`). No "already connected" fast path. Every call acquires `_stateLock` (`KurrentDBLegacyCallInvoker.cs:146`).

- **`HeadersInterceptor` is silently broken — `requires-leader` and identity headers are NEVER sent.** Two defects in `AddHeaders`:
  - `HeadersInterceptor.cs:77` — `if (context.Options.Headers is null) return;` — every module passes only a `CancellationToken`, so `CallOptions.Headers` is null by default. Bailout on every call.
  - `HeadersInterceptor.cs:82-84` — `skipOperation = _operations.Length > 0 && !_operations.Contains(...) || _isExcludeList` — both overloads pass `isExcludeList: true`, making `skipOperation` always `true`. Even past the null guard, nothing would be injected.
  - **Consequence:** the server sees no `requires-leader` header, no client-name, no client-version, no connection-name on any call. V1 server defaults `requires-leader` to `false` (`ServiceBase.cs:57-63`), so reads always land on whatever node the balancer picked — works by accident.

- **`DefaultDeadline` never reaches business RPCs.** `KurrentDBCallOptions.CreateNonStreaming` wires deadline (`KurrentDBCallOptions.cs:17-28`), but its only consumers are `GrpcGossipClient` and `GrpcServerCapabilitiesClient`. Every module call (e.g. `StreamsClient.AppendRecords.cs:27`) passes `cancellationToken: ct` with no `CallOptions` and no deadline.

- **`LeaderNotFoundInterceptor` detects only V1 trailers** — V2 writes that fail with `FailedPrecondition + NotLeaderNodeErrorDetails` are never detected today. Leader detection is broken for V2 write traffic.

- **`LeaderNotFoundInterceptor` does not override `BlockingUnaryCall`** — sync callers silently miss leader changes.

- **Dead code in the channel stack:**
  - `TypedExceptionInterceptor` — never instantiated (`exceptionMap.Count == 0` always true at `LegacyClusterClient.cs:96`).
  - `LegacyClusterClient.ResolverScheme` property (`LegacyClusterClient.cs:76, 106`) — zero consumers.
  - `KurrentDBLegacyCallInvoker.ForceRefresh` (`KurrentDBLegacyCallInvoker.cs:98`) — only caller commented out at `KurrentClient.cs:78-84`.
  - `KurrentDBLegacyCallInvoker.ChannelOptions` property — zero external readers.
  - `SingleNodeHttpHandler.cs` — defined, never registered anywhere.
  - `Gossip/Selectors/ChannelSelector.cs` — never instantiated; `LegacyClusterClient.cs:37-39` constructs concrete selectors inline.
  - `Legacy/SystemConstants.cs`, `Legacy/SystemStreams.cs` — 100% commented out.
  - `ServerCapabilities.SupportsSchemaRegistry` — field exists, never set.

- **Bugs baked in:**
  - `Random(0)` constant-seeded shuffle in `ChannelCache.cs:15` and `NodeSelector.cs:24` — "random" is deterministic across processes.
  - `KurrentDBClientRetrySettings.InitialBackoff = TimeSpan.FromSeconds(250)` (`KurrentDBClientRetrySettings.cs:23`) — live, applied to every channel's `ServiceConfig.RetryPolicy` via `ChannelFactoryExtensions.cs:22`. 250-second initial backoff is almost certainly a ms/s unit bug.
  - Fire-and-forget `DisposeChannelsAsync` in `ChannelCache.cs:76` — shutdown errors silently discarded.
  - `ChannelCache.UpdateCache` calls `CreateChannel` while holding `_lock` (`ChannelCache.cs:81`) — works only because C# `lock` is thread-reentrant.
  - `AuthenticationInterceptor` blocks on credential delegate (`.GetAwaiter().GetResult()` at `AuthenticationInterceptor.cs:74`).

### 3.2 Why the server gives us a clean redirect

Evidence from `/Users/sergio/dev/kurrent/kurrentdb/src`:

- **V2 writes hard-code `requireLeader: true`** (`KurrentDB.Api.V2/Modules/Streams/StreamsService.cs:124, 286`). The server does not read the `requires-leader` header on V2. On a non-leader node, V2 writes always deny — no forwarding opt-out possible.

- **Denial carries a rich protobuf payload.** `ApiCallback.cs:98` → `ApiErrors.NotLeaderNode(leaderId, leaderHttp)` → at `ApiErrors.cs:291-309` returns `RpcException` with status `FailedPrecondition` (via `ServerError.NotLeaderNode = 5` in `proto/kurrent/rpc/errors.proto`) and `Status.Details` carrying `Kurrent.Rpc.NotLeaderNodeErrorDetails { CurrentLeader = { Host, Port, NodeId } }`. No trailer parsing — `RpcException.GetRpcStatus().GetDetail<NotLeaderNodeErrorDetails>()` just works.

- **V1 still emits trailer-based redirects.** `RpcExceptions.LeaderInfo()` (`KurrentDB.Core/Services/Transport/Grpc/RpcExceptions.cs:25`) returns `StatusCode.NotFound` with trailers `exception=not-leader`, `leader-endpoint-host`, `leader-endpoint-port`. Called from V1 Streams (Append, BatchAppend, Delete, Read), V1 PersistentSubscriptions, V1 Operations (via `TryHandleNotHandled`), and all V1 enumerators.

- **V1 writes with `requires-leader=false` are transparently forwarded** via internal TCP (`ClusterVNodeController.cs:829-838`). V2 does NOT forward. V1 persistent subscriptions and admin operations do NOT forward.

- **Gossip RPC has no leader check** — any node answers (`Gossip.Read.cs:26`). Seeds do not need to be leader-filtered.

- **There is no V2 gossip RPC.** Only `event_store.client.gossip.Gossip/Read` exists. The V1 gossip stub stays.

- **SDK module → NotLeader flavor table:**

| SDK Module | Proto Service | NotLeader flavor |
|---|---|---|
| `StreamsClient` (Append, BatchAppend) | V2 `KurrentDB.Protocol.V2.Streams.StreamsService` | V2 payload |
| `StreamsClient` (Read, Subscribe, Manage) | V1 legacy stub | V1 trailer |
| `AdminClient` | V1 `Operations` | V1 trailer |
| `IndexesClient` | V2 | V2 payload |
| `ConnectorsClient` | V2 | V2 payload (no redirect path observed server-side) |
| `RegistryClient` | V2 Schema Registry | None — server has no `NotLeader` emission in Schema Registry |
| (future) `PersistentSubscriptionsClient` | V1 | V1 trailer |
| (future) `ProjectionsClient` | V1 | V1 trailer |
| (future) `UsersClient` | V1 | V1 trailer |

→ **The new interceptor must handle both V2 payload and V1 trailer. Not optional.**

## 4. Capability Inventory — validated

Re-validated against the code. Corrections from the previous draft are noted inline.

| # | Capability | Current location | Validated status | New home |
|---|---|---|---|---|
| C1 | Parse connection string | `KurrentConnectionString.cs` → `ToClientOptions()` | Correct | Unchanged (modern side) |
| C2 | Validate options | `KurrentClientOptions.Validator` | Correct | Unchanged (modern side) |
| C3 | Resolve seeds from `Endpoints[]` | `KurrentClientOptions.Endpoints` | Correct | `KurrentResolver` consumes directly |
| C4 | Single-node vs discovery dispatch | `LegacyClusterClient.cs:37` `IsSingleNode` | Correct | URI scheme switch in `KurrentClientChannelFactory` |
| C5 | Gossip RPC call | `GrpcGossipClient.GetAsync` | Correct | Reused verbatim in `KurrentResolver.ResolveAsync` |
| C6 | Parse cluster members | `ClusterMessage.cs` direct enum cast | Correct | Reused verbatim |
| C7 | NodePreference filter/sort | `NodeSelector.SelectNode` + `NodePreferenceComparers` | Correct; `Random(0)` bug at `NodeSelector.cs:24` | Reused; constant-seed bug fixed |
| C8 | Seed shuffle | `ChannelCache.GetRandomOrderSnapshot` | Correct; same `Random(0)` bug at `ChannelCache.cs:15` | Resolver uses `Random.Shared` |
| C9 | `MaxDiscoverAttempts` loop | `GossipChannelSelector.cs:33` | Correct | `KurrentResolver` retry loop |
| C10 | Exhaustion → `DiscoveryException` | `GossipChannelSelector.cs:74` | Correct | Replaced with `ResolverResult.ForFailure(Status.Unavailable)` |
| C11 | Re-seed cache from static `GossipSeeds` | `GossipChannelSelector.cs:64` | Correct (not a DNS re-lookup, just cache reset) | Resolver re-reads `options.Endpoints` |
| C12 | Channel cache update from gossip | `ChannelCache.UpdateCache` | Correct | **Removed.** Balancer owns subchannels |
| C13 | Channel keying by `DnsEndPoint` | `ChannelCache.cs:101-128` | Correct | **Removed.** Balancer-internal |
| C14 | Build `GrpcChannel` with TLS, keep-alive, HTTP/2 | `ChannelFactoryExtensions.CreateChannel` | Correct | Consolidated into `KurrentClientChannelFactory.Create` |
| C15 | `TransportSecurity` modes | `KurrentClientSecurityOptions` | Correct | Applied in `BuildSslOptions` |
| C16 | mTLS client cert | `ClientCredentials.Certificate` | Correct; uses `X509Certificates.CreateFromPemFile` helper at `RipAndTear/X509Certificates.cs` | Replaced with built-in `X509Certificate2.CreateFromPemFile` (.NET 6+) |
| C17 | Authentication header | `AuthenticationInterceptor` | Correct and works | Rewritten (dropping the sync `.GetAwaiter().GetResult()` on the credential delegate is a nice-to-have but not required) |
| C18 | `requires-leader` header per call | `HeadersInterceptor` via `LegacyClusterClient.cs:81-83` | **BROKEN — never sent** (two defects, see §3.1) | New `RequiresLeaderHeaderInterceptor` — actually works |
| C19 | Client identity headers (name/version/connection-name) | `HeadersInterceptor` via `LegacyClusterClient.cs:85-89` | **BROKEN — never sent** | New `ClientIdentityHeaderInterceptor` — actually works |
| C20 | Leader redirect | `LeaderNotFoundInterceptor` | Partially works: V1 trailer only; V2 payload silently ignored; `BlockingUnaryCall` not covered | New `LeaderRedirectInterceptor` handles V2 payload + V1 trailer + `BlockingUnaryCall` |
| C21 | Deduplicate concurrent reconnects | `SharingProvider.OnBroken` CAS | Correct | **Removed.** Balancer subchannel state machine handles it |
| C22 | Cache current channel | `SharingProvider._currentBox` | Correct | **Removed.** Single `GrpcChannel` instance |
| C23 | Retry gossip on failure | `SharingProvider.FillBoxAsync` catch+delay | Flat delay, no backoff | `PollingResolver.MinimumRefreshInterval` + exponential retry inside resolver |
| C24 | User-supplied interceptors | `LegacyClusterClient.cs:96-97` | Correct | `channel.Intercept(..., options.Interceptors)` |
| C25 | `DefaultDeadline` applied | `KurrentDBCallOptions` at `KurrentDBCallOptions.cs:17-28` | **BROKEN for business RPCs** — only gossip/capability probes honor it | **New work item**: wire `options.Resilience.Deadline` at every module call site via `CallOptions.WithDeadline(...)`. Not strictly part of the channel stack but must ship together to avoid regression. |
| C26 | Keep-alive | `SocketsHttpHandler.KeepAlivePing*` | Correct | Same |
| C27 | Force refresh | `KurrentDBLegacyCallInvoker.ForceRefresh` | Dead (caller commented out) | **Removed — D2 decision.** No public API |
| C28 | `ServerCapabilities` | `GrpcServerCapabilitiesClient` → exposed via `ClientModuleBase.cs:29` | Dead (no reader) | **Removed — D1 decision** |
| C29 | Dispose cascade | `KurrentDBLegacyCallInvoker.DisposeAsync` → `LegacyClusterClient.DisposeAsync` → `ChannelCache.DisposeAsync` | Correct; `ChannelCache.cs:76` fire-and-forget bug | `GrpcChannel.Dispose()` + resolver `IAsyncDisposable` |
| C30 | Logger factory | Threaded through selectors, `SharingProvider`, `ChannelCache` | Correct | `GrpcChannelOptions.LoggerFactory` + resolver logger |

### 4.1 Capabilities the spec previously missed (now accounted for)

- **`TypedExceptionInterceptor` `Unavailable + "Deadline Exceeded"` remap** (`TypedExceptionInterceptor.cs:18-19`) — never fires today (interceptor never instantiated). Do not migrate. Modern server doesn't emit this pattern.
- **`LegacyClusterClient.ResolverScheme` public property** — dead. Drop with the file.
- **Env headers** (`OsName`, `OsVersion`, `RuntimeName`, `RuntimeVersion`, `HostArchitecture`) — defined in `Constants.cs:73-94`, never passed to any interceptor. Not a real capability. Drop.
- **`KurrentBackdoorClientFactory.GetBackdoorClient()`** — defined at `ClientModuleBase.cs:33-34`, zero callers in `src/`. If kept, update to use `channel.Target` directly. **Candidate for deletion** (see §6.4).

## 5. Target Architecture

```
KurrentClientOptions                                                 [public, unchanged]
    │
    └─► KurrentClientChannelFactory.Create(options)                  [new, ~80 lines]
            │
            ├─ BuildSslOptions(Security.Transport, Security.Authentication)
            │   └─ SocketsHttpHandler { KeepAlive*, SslOptions, EnableMultipleHttp2Connections }
            │
            ├─ ServiceConfig {
            │     LoadBalancingConfigs: [ PickFirstConfig ],
            │     MethodConfigs: [{ Names: Default, RetryPolicy on Unavailable (3 attempts, exp backoff) }]
            │   }
            │
            ├─ Discover scheme: register KurrentResolverFactory(options) for "kurrentdb+discover"
            │   Direct scheme:   use GrpcChannel.ForAddress(https://host:port) — no resolver needed
            │
            └─► GrpcChannel.ForAddress(uri, channelOptions)
                    │
                    └─► channel.Intercept(
                          RequiresLeaderHeaderInterceptor(NodePreference),     // C18, fixed
                          ClientIdentityHeaderInterceptor(ConnectionName),     // C19, fixed
                          LeaderRedirectInterceptor(resolver),                 // C20, V2+V1
                          AuthenticationInterceptor(Security.Authentication),  // C17
                          ...options.Interceptors                              // C24
                        )
                    │
                    └─► CallInvoker passed to every module's gRPC stub
```

### 5.1 `KurrentResolver : PollingResolver`

Registered for scheme `kurrentdb+discover://`. Responsibilities:

- Maintain the current set of seeds (initial: `options.Endpoints`).
- Hold an optional `_leaderHint` (`DnsEndPoint?`) set by the interceptor.
- On `ResolveAsync(listener, ct)`:
  - **If `_leaderHint` is set** (one-shot): resolve to just that endpoint, clear the hint.
  - Else: iterate seeds in random order (`Random.Shared`), call V1 gossip on each until one responds, apply `NodeSelector.SelectNode(members, NodePreference)`, publish the result as `ResolverResult.ForResult([BalancerAddress(picked)])`.
  - On exhaustion after `MaxDiscoverAttempts` × seeds: `listener(ResolverResult.ForFailure(new Status(StatusCode.Unavailable, "...")))`.
- `MinimumRefreshInterval = Gossip.DiscoveryInterval`.
- Backoff between discovery attempts: `ExponentialBackoff(base: 100ms, cap: Gossip.Timeout)`, not the flat 100ms of today.
- Per-seed gossip call deadline: `Gossip.Timeout` (finally honored).
- `HintLeader(DnsEndPoint)` public method callable by the interceptor. Stores hint + calls `Refresh()`.
- Logger: `ILogger<KurrentResolver>` via `options.LoggerFactory`.
- `DisposeAsync`: disposes gossip channels it opened to seeds.

**NodePreference semantics**: resolver returns **one** picked address. `pick_first` keeps one live subchannel there. Matches today. Multi-address `round_robin` is a future extension (out of scope).

**Single-node** (`KurrentConnectionScheme.Direct`): no custom resolver. `GrpcChannel.ForAddress($"https://{host}:{port}")` with the static address goes through the built-in DNS resolver. Simpler, fewer moving parts.

### 5.2 `LeaderRedirectInterceptor`

Detection order on every faulted call:

1. **V2 payload** (preferred path):
   ```csharp
   if (ex.StatusCode == StatusCode.FailedPrecondition
       && ex.GetRpcStatus()?.GetDetail<NotLeaderNodeErrorDetails>() is { CurrentLeader: { } leader })
       resolver.HintLeader(new DnsEndPoint(leader.Host, leader.Port));
   ```

2. **V1 trailer** (fallback for V1 stubs):
   ```csharp
   if (ex.StatusCode == StatusCode.NotFound
       && ex.Trailers.GetValue("exception") == "not-leader"
       && ex.Trailers.GetValue("leader-endpoint-host") is { } host
       && ex.Trailers.GetValue("leader-endpoint-port") is { } port)
       resolver.HintLeader(new DnsEndPoint(host, int.Parse(port)));
   ```

**Direct-mode (single node)**: no resolver, no hint — the interceptor logs the redirect and gives up. The error propagates unchanged. Acceptable because single-node mode is explicit user choice.

**Does not retry the original call.** The failing call still throws. Next call on the channel picks up the new leader after resolver refresh.

**Covers all five `CallInvoker` overrides** including `BlockingUnaryCall` (fixes today's gap).

### 5.3 Other interceptors

- **`RequiresLeaderHeaderInterceptor`**: inject `requires-leader: true|false` based on `options.Gossip.ReadPreference == NodePreference.Leader`. Creates a new `Metadata` if `context.Options.Headers` is null (fixes today's bug). No exclude-list shenanigans.

- **`ClientIdentityHeaderInterceptor`**: inject `kurrentdb-client-name`, `kurrentdb-client-version`, `kurrentdb-connection-name`. Same null-safe approach. One-shot (values computed once at interceptor construction).

- **`AuthenticationInterceptor`**: rewritten clean — holds a `ClientCredentials` snapshot, produces header synchronously for basic/bearer. No `GetAwaiter().GetResult()` on a delegate. If future auth flows need async token refresh, that's a follow-up.

### 5.4 Deadline wiring (C25 regression fix)

Every module call site that today passes only a `CancellationToken` will be updated to pass a `CallOptions` built from `options.Resilience.Deadline`:

```csharp
var callOptions = new CallOptions(cancellationToken: ct)
    .WithDeadline(deadline);
```

This is mechanical across the module files (`StreamsClient.AppendRecords.cs`, `.Read.cs`, etc.). Produce a small helper on `ClientModuleBase` — `protected CallOptions BuildCallOptions(CancellationToken ct)` — and use it everywhere.

## 6. File Disposition — complete manifest

### 6.1 Delete outright

| File | Reason |
|---|---|
| `Legacy/KurrentDBLegacyCallInvoker.cs` | Replaced by raw `GrpcChannel` + interceptor chain |
| `Legacy/KurrentDBLegacySettingsConverter.cs` | No more legacy settings record to target |
| `Legacy/SystemConstants.cs` | 100% commented-out dead file |
| `Legacy/SystemStreams.cs` | 100% commented-out dead file |
| `Legacy/RipAndTear/Client/LegacyClusterClient.cs` | Replaced by `KurrentResolver` + channel factory |
| `Legacy/RipAndTear/Client/KurrentDBCallOptions.cs` | Only consumed by gossip + capabilities plumbing (both going) |
| `Legacy/RipAndTear/Client/Constants.cs` | Whole file goes (see §6.3 for which constants survive and where) |
| `Legacy/RipAndTear/Client/Settings/KurrentDBClientSettings.cs` | No more legacy settings record |
| `Legacy/RipAndTear/Client/Settings/KurrentDBClientConnectivitySettings.cs` | Ditto |
| `Legacy/RipAndTear/Client/Settings/KurrentDBClientOperationOptions.cs` | `BatchAppendSize` unconfigurable, `ThrowOnAppendFailure` unused |
| `Legacy/RipAndTear/Client/Settings/KurrentDBClientRetrySettings.cs` | Replaced by `ServiceConfig.RetryPolicy`. Kills `InitialBackoff = 250s` bug |
| `Legacy/RipAndTear/Client/Interceptors/LeaderNotFoundInterceptor.cs` | Replaced by `LeaderRedirectInterceptor` |
| `Legacy/RipAndTear/Client/Interceptors/AuthenticationInterceptor.cs` | Rewritten clean in new location |
| `Legacy/RipAndTear/Client/Interceptors/TypedExceptionInterceptor.cs` | Never instantiated today |
| `Legacy/RipAndTear/Client/Exceptions/DiscoveryException.cs` | Replaced by idiomatic `RpcException(Unavailable)` |
| `Legacy/RipAndTear/Client/Gossip/SharingProvider.cs` | Balancer owns the equivalent state machine |
| `Legacy/RipAndTear/Client/Gossip/ChannelCache.cs` | Balancer owns subchannels; kills `Random(0)` + re-entrant lock + fire-and-forget dispose |
| `Legacy/RipAndTear/Client/Gossip/ChannelFactoryExtensions.cs` | Folded into `KurrentClientChannelFactory` |
| `Legacy/RipAndTear/Client/Gossip/ChannelInfo.cs` | Internal DTO no longer needed |
| `Legacy/RipAndTear/Client/Gossip/ReconnectionRequired.cs` | DU replaced by resolver `HintLeader` + `Refresh` |
| `Legacy/RipAndTear/Client/Gossip/SingleNodeHttpHandler.cs` | Never registered; dead file |
| `Legacy/RipAndTear/Client/Gossip/ServerCapabilities.cs` | D1 — delete entirely |
| `Legacy/RipAndTear/Client/Gossip/GrpcServerCapabilitiesClient.cs` | D1 |
| `Legacy/RipAndTear/Client/Gossip/IServerCapabilitiesClient.cs` | D1 |
| `Legacy/RipAndTear/Client/Gossip/Selectors/ChannelSelector.cs` | Dead — never instantiated |
| `Legacy/RipAndTear/Client/Gossip/Selectors/IChannelSelector.cs` | Replaced by resolver |
| `Legacy/RipAndTear/Client/Gossip/Selectors/GossipChannelSelector.cs` | Replaced by `KurrentResolver` |
| `Legacy/RipAndTear/Client/Gossip/Selectors/SingleNodeChannelSelector.cs` | Single-node path uses plain `GrpcChannel.ForAddress` |
| `Legacy/RipAndTear/HashCode.cs` | Replace uses with `System.HashCode.Combine` (.NET 6+) |
| `Legacy/RipAndTear/UserCredentials.cs` | Only consumed by legacy stack files, all going |
| `Legacy/RipAndTear/X509Certificates.cs` | Replace uses with built-in `X509Certificate2.CreateFromPemFile` |

### 6.2 Relocate (keep, move out of `Legacy/`)

These are used outside the channel stack and survive:

| Source | Destination | Why |
|---|---|---|
| `Legacy/RipAndTear/Uuid.cs` | `Core/Uuid.cs` (or folded into `Internal/Proto/`) | Used by `StreamsClientV1Mapper.cs:240` (Kurrent.Client) and processors V1 mapper. Switch internal impl to `System.HashCode.Combine` after relocation. |
| `Legacy/RipAndTear/EpochExtensions.cs` | `Internal/Proto/EpochExtensions.cs` | Used by `StreamsClientV1Mapper.cs:244` and processors V1 mapper |
| `Legacy/RipAndTear/StreamIdentifier.cs` | `Internal/Proto/StreamIdentifierExtensions.cs` | Partial class extending generated proto type; used by all stream mappers |
| `Legacy/KurrentDBLegacyErrorCodes.cs` | `Internal/Errors/LegacyErrorCodes.cs` | 7 live call sites in stream/processors code |
| `Legacy/KurrentDBLegacyExceptionExtensions.cs` | `Internal/Errors/LegacyExceptionExtensions.cs` | Heavily used outside channel stack |
| `Legacy/RipAndTear/Client/Gossip/NodePreference.cs` | `Options/NodePreference.cs` | Public-facing enum (D4) |
| `Legacy/RipAndTear/Client/Gossip/NodeSelector.cs` | `Internal/Cluster/NodeSelector.cs` | Pure filter/sort; reused by resolver. Fix `Random(0)` → `Random.Shared`. |
| `Legacy/RipAndTear/Client/Gossip/NodePreferenceComparers.cs` | `Internal/Cluster/NodePreferenceComparers.cs` | Used by `NodeSelector` |
| `Legacy/RipAndTear/Client/Gossip/ClusterMessage.cs` | `Internal/Cluster/ClusterMessage.cs` | V1 gossip proto projection |
| `Legacy/RipAndTear/Client/Gossip/GrpcGossipClient.cs` | `Internal/Cluster/GossipClient.cs` | Gossip RPC stub wrapper; reused by resolver |
| `Legacy/RipAndTear/Client/Gossip/IGossipClient.cs` | `Internal/Cluster/IGossipClient.cs` | Test seam |
| `Legacy/RipAndTear/Client/Gossip/EndPointExtensions.cs` | Delete unused `ToHttpUrl`; fold `ToUri` into resolver or inline | Mixed |
| `Legacy/RipAndTear/TaskExtensions.cs` | Delete | Only consumer is `LegacyClusterClient`; other `WithCancellation` uses in module code hit the runtime `IAsyncEnumerable` overload, not this one |
| `Metadata` constants (`Type`, `Created`, `ContentType`) from `Constants.cs` | `Internal/Proto/ProtoMetadataKeys.cs` | Used by V1 mappers |

### 6.3 Constants.cs — line-by-line disposition

| Block | Disposition |
|---|---|
| `Constants.LegacyErrorCodes` nested class | Delete entirely. Zero external references. |
| `Constants.Metadata.{Type, Created, ContentType}` | Relocate to `Internal/Proto/ProtoMetadataKeys.cs`. Used by mappers. |
| `Constants.Headers.{Authorization, BasicScheme, BearerScheme}` | Inline into new `AuthenticationInterceptor` |
| `Constants.Headers.RequiresLeader` | Inline into `RequiresLeaderHeaderInterceptor` |
| `Constants.Headers.{ClientName, ClientVersion, ConnectionName}` | Inline into `ClientIdentityHeaderInterceptor` |
| `Constants.Headers.{OsName, OsVersion, RuntimeName, RuntimeVersion, HostArchitecture}` | Delete. Never wired. |

### 6.4 `KurrentBackdoorClientFactory` decision

`KurrentBackdoorClientFactory.GetBackdoorClient()` has zero callers in `src/`. `ClientModuleBase.GetBackdoorClient()` (`ClientModuleBase.cs:33-34`) is defined but never invoked. The factory builds `HttpClient` for a hypothetical HTTP backdoor not currently used.

**Option A (recommended): delete it.** If backdoor HTTP becomes needed later, reintroduce with a plain `HttpClient` constructed from `channel.Target` — much simpler than the current factory (which has its own Polly pipeline and cert validation logic replicating `SocketsHttpHandler` config).

**Option B: keep it, switch `Invoker.ChannelTarget` → `channel.Target`.** Preserves latent capability at the cost of ~150 lines of unused code.

**Default: Option A** unless Sergio wants to preserve it.

### 6.5 Add

| File | Purpose |
|---|---|
| `Internal/Channel/KurrentClientChannelFactory.cs` | Builds `GrpcChannel` from `KurrentClientOptions` |
| `Internal/Cluster/KurrentResolver.cs` | `PollingResolver` with gossip + NodePreference + leader hint |
| `Internal/Cluster/KurrentResolverFactory.cs` | Registered for `kurrentdb+discover` scheme |
| `Internal/Interceptors/LeaderRedirectInterceptor.cs` | V2 payload + V1 trailer + all 5 CallInvoker overrides |
| `Internal/Interceptors/RequiresLeaderHeaderInterceptor.cs` | Clean, null-safe |
| `Internal/Interceptors/ClientIdentityHeaderInterceptor.cs` | Clean, null-safe |
| `Internal/Interceptors/AuthenticationInterceptor.cs` | Rewritten, no sync-over-async delegate call |
| `Internal/Proto/ProtoMetadataKeys.cs` | `Type`, `Created`, `ContentType` constants |

## 7. Connection String / Options Mapping (no change to public surface)

Every parameter from `KurrentConnectionString.ToClientOptions()` continues to be honored. Same table as before, confirmed:

| Param | Path in `KurrentClientOptions` | New consumer |
|---|---|---|
| scheme (`kurrentdb[+discover]`, aliases) | `ConnectionScheme` | `KurrentClientChannelFactory` |
| host:port list | `Endpoints[]` | `KurrentResolver` seeds (Discover) or URL (Direct) |
| `user:pass@` | `Security.Authentication = ClientCredentials.Basic(...)` | `AuthenticationInterceptor` |
| `ConnectionName` | `ConnectionName` | `ClientIdentityHeaderInterceptor` |
| `Tls`, `TlsVerifyCert`, `TlsCaFile` | `Security.Transport` | `BuildSslOptions` |
| `UserCertFile`, `UserKeyFile` | `Security.Authentication = ClientCredentials.Certificate(...)` | `BuildSslOptions` (using stdlib `X509Certificate2.CreateFromPemFile`) |
| `KeepAliveInterval`, `KeepAliveTimeout` | `Resilience.KeepAliveInterval`, `.KeepAliveTimeout` | `SocketsHttpHandler.KeepAlivePing*` |
| `DefaultDeadline` | `Resilience.Deadline` | Consumer-side `CallOptions.WithDeadline` (**new work — see §5.4**) |
| `MaxDiscoverAttempts` | `Gossip.MaxDiscoverAttempts` | `KurrentResolver` |
| `DiscoveryInterval` | `Gossip.DiscoveryInterval` | `KurrentResolver.MinimumRefreshInterval` |
| `GossipTimeout` | `Gossip.Timeout` | **Now load-bearing** — per-seed gossip call deadline |
| `NodePreference` | `Gossip.ReadPreference` | `NodeSelector` in resolver + `RequiresLeaderHeaderInterceptor` |

## 8. Implementation Plan — phased

Seven phases, each compilable and testable on its own. Single branch, single PR, commits per phase.

### Phase 1 — Relocate load-bearing helpers out of `Legacy/` (no behavior change)

Pure file moves + namespace renames. Everything still compiles against the legacy stack.

- Move files per §6.2 table.
- Update namespaces and `using` statements across the solution.
- Fix `Uuid` impl to use `System.HashCode.Combine`.
- Fix `X509Certificates` usages to use `X509Certificate2.CreateFromPemFile`.
- Split `Constants.cs`: keep `Metadata.{Type,Created,ContentType}` relocated; delete the rest (but keep a temporary reference in the legacy interceptors until Phase 4).
- Run full test suite → green.

**Deliverable:** `Legacy/` folder now contains only channel-stack and its dependencies.

### Phase 2 — Build the new channel stack (additive, not yet wired)

- Add `KurrentClientChannelFactory`, `KurrentResolver[Factory]`, 4 new interceptors (`LeaderRedirect`, `RequiresLeaderHeader`, `ClientIdentityHeader`, `AuthenticationInterceptor`).
- Unit tests for each, using TUnit + Faker per project conventions.
- Nothing in `KurrentClient.cs` changes yet.

**Deliverable:** new code exists, tested in isolation, not wired into `KurrentClient`.

### Phase 3 — Wire `KurrentClient` to the new stack

- `KurrentClient.cs` ctor: replace `new KurrentDBLegacyCallInvoker(options)` with `KurrentClientChannelFactory.Create(options)` → `channel.Intercept(...).CreateCallInvoker()`.
- Dispose order: `await channel.DisposeAsync()` (resolver disposes via channel).
- Drop `LegacyCallInvoker` property. Modules still want a `CallInvoker`, so expose `internal CallInvoker CallInvoker { get; }` instead.
- Update `ClientModuleBase` to consume `CallInvoker` (not `LegacyCallInvoker.ServerCapabilities`) — drop `ServerCapabilities` accessor.
- Update `KurrentBackdoorClientFactory` per §6.4 decision.
- **Full integration test suite** against real containers (single-node + cluster). Leader-kill scenario. Mixed V1/V2 traffic.

**Deliverable:** production path uses new stack end-to-end. Legacy stack files still on disk but unreferenced.

### Phase 4 — Delete the legacy stack

- Delete every file in §6.1.
- Remove references from `.csproj` if any explicit `<Compile Include=...>`.
- Grep for any stragglers — should be zero.
- Rebuild + full test suite.

**Deliverable:** `Legacy/` folder contains only proto-interop and error-code relocations; if those also moved in Phase 1, `Legacy/` is empty and can be deleted.

### Phase 5 — Wire deadlines (C25 fix)

- Add `ClientModuleBase.BuildCallOptions(ct)` helper that combines deadline from `options.Resilience.Deadline` with the passed `CancellationToken`.
- Sweep all module call sites. Replace `cancellationToken: ct` with `BuildCallOptions(ct)`.
- Integration test: verify `DefaultDeadline=500ms` actually times out a hanging request.

**Deliverable:** `DefaultDeadline` now load-bearing.

### Phase 6 — Final cleanup

- Remove `Legacy/` folder if empty.
- Audit namespace consistency.
- Update `CLAUDE.md` layout notes if any.
- Verify no `KurrentDB.Client`-namespaced types outside intentional legacy-aliased spots.

### Phase 7 — Docs + commit hygiene

- Update any architecture/onboarding docs referencing the old stack.
- One PR, multiple commits, clean history per phase.
- Tag the PR with `breaking: internal` — the public surface is unchanged but internals are a different universe.

## 9. Testing

Per project conventions (TUnit + TestDataSource + Faker, AAA comments, integration over containers via FluentDocker):

### 9.1 Unit tests

- `KurrentResolver` — happy path, seed exhaustion, leader hint, `NodePreference` variants, gossip RPC failures.
- `LeaderRedirectInterceptor` — V2 payload extraction, V1 trailer extraction, ignores non-leader errors, covers `BlockingUnaryCall`, calls `resolver.HintLeader + Refresh` exactly once per failure, does not retry.
- `RequiresLeaderHeaderInterceptor` — null headers collection handled; value derived from `NodePreference`.
- `ClientIdentityHeaderInterceptor` — null headers collection handled; values from options.
- `AuthenticationInterceptor` — Basic, Bearer, certificate, no-auth paths.
- `NodeSelector` with `Random.Shared` — distribution sanity + preference filter correctness.

### 9.2 Integration tests (container)

- Single-node: connect, write, read, subscribe, TLS + insecure matrix.
- Cluster: kill leader mid-write → client recovers, next write lands on new leader, no manual intervention.
- V1-only operations (`StreamsClient.Read`, `AdminClient.Scavenge`): verify V1 trailer redirect path works on cluster.
- V2-only operations (`StreamsClient.AppendRecords`): verify V2 payload redirect.
- Mixed traffic: interleaved V1 reads + V2 writes on a single `KurrentClient` → all land correctly.
- Deadline: configure `DefaultDeadline=500ms`, hang a request on server, verify client throws `DeadlineExceeded` (validates Phase 5).
- User-supplied interceptor: still invoked.
- `AuthenticationInterceptor`: mTLS against real certs.

### 9.3 Deleted tests

- Anything that targets `SharingProvider`, `ChannelCache`, `GossipChannelSelector`, `LegacyClusterClient` internals directly. `NodeSelectorTests` survives (tests preference logic, not channel mechanics).

## 10. Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| `PollingResolver` + `SocketsHttpHandler` interaction on net8.0 | Low | `Grpc.Net.Client 2.76.0` well past bug cutoff. Integration tests on all three TFMs. |
| V1 trailer path regresses during migration | Medium | Phase 3 integration test covers V1 `StreamsClient.Read` on a 3-node cluster with leader kill. |
| Resolver leader hint race (two failures, two hints) | Low | Last-hint wins; next poll re-validates via gossip. Self-correcting. |
| Deleting `KurrentBackdoorClientFactory` breaks latent consumer | Low | Grep confirms zero `src/` callers. If a caller emerges, reintroduce with 10 lines. |
| `RetryPolicy` on `Unavailable` retries non-idempotent writes | Very low | KurrentDB writes are idempotent via expected-revision consistency checks. `FailedPrecondition` is never retried. |
| Moving `Uuid` breaks binary compat for Kurrent.Processors | Low — same monorepo, rebuild together | Single-PR migration, both projects rebuild. |
| Someone un-comments PS/Projections/Users after migration | Medium (it will happen eventually) | V1 trailer path already supported. No additional work needed on channel stack. |

## 11. Open Questions

1. **`KurrentBackdoorClientFactory` — delete (Option A) or keep with minor rewire (Option B)?** Default recommendation: A (delete). Sergio's call.
2. **Phase 5 (deadlines) in same PR or follow-up?** It's mechanical but touches every module file. If we want this PR to stay under ~30 files changed, Phase 5 could be a follow-up. Default: same PR, it's a correctness fix that's been silently broken.
3. **Is there appetite to replace `AuthenticationInterceptor` with `CallCredentials`** (the gRPC-native per-call metadata mechanism)? Cleaner, composable with `ChannelCredentials`. Adds ~20 lines, removes interceptor. Out of scope unless wanted.

## 12. Ready Checklist

- [x] D1-D5 decisions recorded
- [x] Every current capability validated (C1-C30) with corrections folded in
- [x] Every file in `Legacy/` assigned to delete/relocate/keep with reason
- [x] Active today-broken capabilities (C18, C19, C20 V2 path, C25) called out as bugs fixed, not migrated
- [x] V1 trailer fallback justified with module-level evidence
- [x] Phased migration plan with compile-green gates between phases
- [x] Test plan aligned with project conventions
- [x] Risks enumerated
- [ ] Sergio sign-off on §11 open questions
- [ ] Start Phase 1 (blocked on sign-off)
