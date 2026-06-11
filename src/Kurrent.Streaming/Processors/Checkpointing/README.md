# Hybrid Logical Clock

`HybridLogicalClock` generates timestamps that are **always increasing** and
**stay close to real time**, even when the computer's clock misbehaves. It is an
implementation of the Hybrid Logical Clock (HLC) from the paper
["Logical Physical Clocks and Consistent Snapshots in Globally Distributed Databases"](https://cse.buffalo.edu/tech-reports/2014-04.pdf)
by Kulkarni, Demirbas, Madappa, Avva, and Leone (OPODIS 2014).

## The problem this solves

It is tempting to order events with `DateTimeOffset.UtcNow`. That breaks in two
ways:

1. **The system clock is not always increasing.** Clock-synchronization services
   like NTP regularly adjust it — sometimes backward. Two events one after the
   other can get timestamps in the *wrong order*, or the *same* timestamp if they
   happen within the same millisecond.
2. **Two machines never agree on the time.** If machine A processes an event and
   machine B processes its follow-up, B's wall clock may stamp the follow-up
   *earlier* than the original.

A purely logical counter (1, 2, 3, ...) would fix the ordering but tells you
nothing about *when* something happened. The HLC combines both: a timestamp is a
**wall-clock time plus a small counter**. The wall-clock part keeps timestamps
meaningful to humans; the counter breaks ties and keeps order guaranteed when
the wall clock can't.

The guarantee: **if event A happened before event B, then A's timestamp is
smaller than B's** — on one machine or across machines, regardless of clock
adjustments.

## What a timestamp looks like

A `HybridTimestamp` is a single 64-bit number with two parts (this layout comes
straight from section 6.2 of the paper):

| Part           | Size    | Meaning                                            |
|----------------|---------|----------------------------------------------------|
| `PhysicalTime` | 47 bits | Unix time in milliseconds — "when" it happened     |
| `Counter`      | 16 bits | Tie-breaker for events within the same millisecond |

Because the time lives in the high bits, the packed numbers sort in event order
— you can compare, sort, or index them as plain `long`s. The string form is
fixed-width (`"0001718000000000000.00007"`), so sorting the *strings*
alphabetically gives the same order too. That makes a timestamp safe to use
directly as a database key or checkpoint identifier.

```csharp
var ts = HybridTimestamp.Create(physicalTime: 1_718_000_000_000, counter: 7);

long   packed = ts;     // packed 64-bit value, sorts in event order
string text   = ts;     // "0001718000000000000.00007", sorts in event order too

var fromLong   = (HybridTimestamp)packed;   // parse back; throws FormatException if invalid
var fromString = (HybridTimestamp)text;     // parse back; throws FormatException if invalid
```

## Which method do I call?

| You want to...                                     | Call                                                     |
|----------------------------------------------------|----------------------------------------------------------|
| Timestamp something that just happened locally     | `Now()`                                                  |
| Process a timestamp that came from another machine | `Receive(timestamp)`                                     |
| Resume after a restart, from a saved timestamp     | `new HybridLogicalClock(timestamp)` or `Sync(timestamp)` |
| Look at the clock without affecting it             | `Current` (never persist this)                           |

## Scenarios

### Timestamping local events

Call `Now()` every time you need a timestamp. Each call returns a value strictly
greater than every previous one. If the wall clock hasn't moved (or moved
backward), the counter increments instead — order is never lost.

```csharp
var clock = new HybridLogicalClock();

var first  = clock.Now();
var second = clock.Now();   // always > first, even within the same millisecond
```

### Receiving timestamps from other machines

When a timestamp arrives from outside — a message, a replicated record, a server
checkpoint — do **not** just call `Now()`. Your local clock might be *behind* the
sender's, and your new timestamp would come out smaller than the event you are
reacting to, breaking cause-and-effect order.

Instead, hand the incoming timestamp to `Receive()`. It pulls the clock forward
if needed and returns a timestamp greater than **both** your local clock and the
received value:

```csharp
var remote = record.Timestamp;        // stamped by another node

var local = clock.Receive(remote);    // local > remote, and > all earlier local timestamps
```

### Resuming after a restart

The clock lives in memory, so a process restart resets it. If the wall clock
also happens to be behind (say NTP pulled it back while the process was down),
a fresh clock could *re-issue a timestamp you already used* before dying.

To prevent that, seed the clock with the last timestamp you persisted. Both
options below treat the saved timestamp as already used — the next `Now()` is
guaranteed to be strictly greater:

```csharp
// Option A: create the clock pre-seeded.
var clock = new HybridLogicalClock(lastPersisted);

// Option B: catch an already-running clock up.
clock.Sync(lastPersisted);

var next = clock.Now();   // strictly > lastPersisted, never a duplicate
```

`Sync()` differs from `Receive()` in one way: it does not produce a timestamp,
it only fast-forwards the clock if it is behind.

### Peeking at the clock

`Current` shows the timestamp the clock would hand out next, without advancing
anything. It exists for diagnostics and logging. Because the next `Now()` can
return the *same* value, never save `Current` as if it were a real issued
timestamp.

## Errors you might see

### `ClockDriftException`

Thrown by `Receive()`, `Sync()`, and the seeding constructor when the incoming
timestamp is more than `MaxOffset` (default: **1 minute**) ahead of this
machine's wall clock.

A timestamp that far in the future means something is wrong — a machine with a
badly misconfigured clock, or a corrupted value. Accepting it would drag every
timestamp this clock issues afterward into the future, so it is rejected
instead. If you hit this, check the clocks on both machines before anything
else.

```csharp
// Tighten or loosen the tolerance per clock:
var clock = new HybridLogicalClock(maxOffset: TimeSpan.FromSeconds(30));
```

### `CounterOverflowException`

Thrown by `Now()` and `Receive()` when the 16-bit counter runs out. The counter
allows 65,536 timestamps within a single millisecond of wall-clock time — running
out means the wall clock is stuck or lagging far behind while timestamps are
being issued at an extreme rate. The clock recovers by itself as soon as the
wall clock moves past the stuck millisecond.

In practice this signals a sick machine clock, not a bug in your code.

## How close is this to the paper?

The send/receive rules follow the paper's Algorithm 2 exactly. The differences
are deliberate, practical additions:

| Difference                                                      | Why                                                                                                                                                                                          |
|-----------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| 47-bit physical time instead of the paper's 48                  | .NET's `long` is signed; keeping the sign bit clear lets packed values sort correctly as plain longs. Works until roughly year 6429.                                                         |
| Rejects far-future timestamps (`ClockDriftException`)           | The paper *assumes* clocks stay synchronized and never rejects anything. Production systems (CockroachDB does the same) enforce the assumption instead of trusting it.                       |
| `Sync()` and the seeding constructor                            | The paper has no concept of restarting from saved state. Both treat the saved timestamp as already issued so it can never be handed out twice.                                               |
| Hard failure on counter exhaustion (`CounterOverflowException`) | The paper proves the counter stays small only under its clock-sync assumption. The 16-bit packing makes the limit physical, so exceeding it fails loudly instead of corrupting the encoding. |
