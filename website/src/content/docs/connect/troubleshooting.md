---
title: Troubleshooting connections
description: Work from the symptom to the cause, for failures that happen at startup or later.
---

Before changing a parameter, establish three things: the topology, what changed, and the exact error
with whether it fires at startup or under load. Most connection parameters are safe to leave alone,
and a guessed value hides the real fault.

## The client cannot connect at all

### "Failed to discover a candidate node in N attempts"

This message is raised the same way for several unrelated causes. Do not read it as "the nodes are
down".

| Cause | Check |
|---|---|
| Direct scheme against a cluster | `kurrentdb://` talks to one node and never fails over. Use `kurrentdb+discover://` |
| Wrong port | gRPC is on `2113`. The legacy TCP port `1113` does not answer gRPC |
| TLS mismatch | A secure client against an insecure node, or the reverse, fails here rather than with a TLS error |
| Untrusted certificate | Surfaces as a discovery failure, not a certificate error |
| Attempts exhausted on a slow link | Raise `maxDiscoverAttempts` and `gossipTimeout` together |
| DNS incomplete | A cluster name must resolve to every member. Confirm with `dig +short cluster.example.internal` |
| Advertised address unreachable | See below |

**Turn the logger on first.** Certificate and TLS errors appear only in the client's logs, never in
the discovery message.

```csharp
opts.LoggerFactory = loggerFactory;
```

That one line separates "cannot reach the nodes" from "reached them and rejected the certificate",
which the exception alone cannot tell you.

### The advertised address, not the seed

Gossip hands the client the address the server advertises to clients, and the client dials that,
not the host in your connection string. When the advertised address does not resolve or route from
the client machine, discovery reads gossip successfully and then fails to connect.

Common behind NAT, on split-horizon DNS, and in managed deployments. Confirm every advertised
address resolves and is reachable from the client, not only the seed you typed. If the advertised
value is an IP it must appear in the certificate's IP SANs; if a hostname, in its DNS SANs, or TLS
verification fails as well.

### Certificate rejected

| Cause | Fix |
|---|---|
| Private CA not trusted | Set `tlsCaFile` to the CA bundle. Do not reach for `tlsVerifyCert=false` |
| Hostname mismatch | The client dialled an IP, the certificate names a host. Connect by the name, or reissue with both SANs |
| Expired CA or server certificate | Check expiry on both |

### `InvalidConnectionStringException`

The string itself did not parse. Two causes account for most of it: a scheme outside the six that are
supported, and several hosts under a direct scheme, which is a contradiction because a direct
connection addresses exactly one node.

### `KurrentClientNotConfiguredException`

`AddKurrentClient` was called with no connection string and no configuration source. It throws rather
than guessing a default.

## It connects, then fails later

### Subscriptions die after minutes or hours of quiet

Almost always a middlebox closing an idle connection. Work down this list.

1. **`keepAliveInterval` longer than the path's idle timeout.** Set it to at most half. An AWS NLB
   defaults to 350 seconds, Azure Load Balancer to 4 minutes, and corporate NAT gateways are often
   60 to 300 seconds.
2. **`keepAliveTimeout` too high.** Lowering it makes the client notice a silently broken channel and
   reconnect sooner.
3. **A load balancer dropping HTTP/2 pings.** Some L7 balancers discard them. Enable them, or accept
   the reconnects.
4. **The application not resubscribing.** The client surfaces the cancellation; resuming from the last
   checkpoint is the application's job. A [processor](/processors/) does this for you.

### Deadline exceeded

| Situation | What to do |
|---|---|
| A global `defaultDeadline` too low for the workload | Raise it, or set a deadline per call instead |
| A deadline on a client that subscribes | Remove it. A deadline covers the whole call, and a subscription is one long call |
| Calls that hang rather than fail | The opposite problem. Set a deadline so the operation fails instead of blocking |

Confirm against server metrics before tuning. A scavenge or an election in progress makes calls slow
for reasons no client setting will fix.

### Writes pay a redirect, or "not leader" appears in the logs

Check `nodePreference`. It defaults to `leader`, which is correct for any client that writes. A client
configured for `follower` or `random` that also writes will keep paying a redirect on every write.

Split them into two clients, one per preference, rather than compromising on one.

## Not a connection problem

| Symptom | Where it belongs |
|---|---|
| `StreamRevisionConflictException` after a retry | [Append](/client/streams/append/), optimistic concurrency |
| A schema fails to resolve or validate | [Registration](/schema/registration/) |
| A leader election that never converges | Server-side. Cluster health, not client configuration |
