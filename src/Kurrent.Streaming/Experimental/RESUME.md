# Resume prompt

Paste the block below into a fresh session to continue evolving the experimental
drone. Designed to compose with the auto-loaded directives (`010-core-directives.md`,
`040-dotnet-conventions.md`, `050-dotnet-testing.md`, `MEMORY.md`) — does not duplicate
their content.

---

```markdown
Continue evolving the experimental drone toward production readiness.

## Read first (in order)

1. `src/Kurrent.Processors/Experimental/DESIGN.md` — topology, rationale, and
   deliberate omissions. Don't skip this.
2. The current files in `src/Kurrent.Processors/Experimental/` — what's actually
   implemented.
3. `src/Kurrent.Processors/Drone.cs` and `src/Kurrent.Processors/Gateway/SubscriptionSource.cs` —
   production reference. NOT the target — the experimental is a redesign, not a port.

## Project-specific reminders (on top of the auto-loaded directives)

- Internal `DroneState` (FSM, has `Draining`) drives behavior. Public `DroneStatus`
  is the projection. They're different on purpose — don't conflate.
- Channel sole-completer rule: only the `Checkpointer` calls `writer.Complete()`.
  Other paths (subscription pump, fault handling) signal completion via
  `_checkpointer.InjectCheckpoint(Shutdown)`.
- `IRecordProcessor` / `ProcessRecordContext` / `CheckpointContext` etc. already
  exist in `Kurrent.Processors`. Don't redefine — Read Before Write.
- `Edit`, not `Write`, on existing files.

## Backlog (rough priority — pick one and align with me before starting)

1. **Tests for `Checkpointer`** — pure logic, no gRPC. TUnit, snake_case names,
   `BogusFaker`, `ValueTask`, `Assert.That`, AAA comments per 050.
2. **Tests for `Drone`** — fake `ISubscription` (in-memory IAsyncEnumerable) and
   in-memory `ICheckpointStore`. Cover happy path, drain ordering, FSM
   transitions, reentrance.
3. **End-to-end smoke against real KurrentDB** — wire up `Subscription` with a
   real `KurrentClient`, append a few records, verify dispatch + commit.
4. **Edge case audit**:
   - Subscription faults mid-Drain (channel still closes cleanly?)
   - `Checkpointer.Start` fails (store unreachable) — Drone goes `Faulted` with
     no zombie tasks
   - Drain timeout — final state, what gets committed
   - Repeated Drain calls (FSM `TryTransition` is the guard — verify)
   - User handler throws on the final barrier
5. **Position snapshot race** — `Track` (unlocked) vs `InjectCheckpoint` snapshot
   (locked). DESIGN.md flags this. Either prove it doesn't matter or tighten.
6. **`IRecordBatchProcessor`** — not yet wired. Mirror production's split.
7. **`ProgressTracker`** — currently `DroneProgress.Empty` everywhere.
8. **Migration plan** — replacement vs. coexistence; document in DESIGN.md.

## Definition of "production ready"

- Public API surface has tests.
- End-to-end smoke against real KurrentDB passes.
- Hot-path allocation claims verified (benchmark or read-through).
- Edge cases above either have tests or "won't fix" rationale in DESIGN.md.
- Migration path decided and documented.

## Don't

- Touch production code outside `Experimental/` without a clear reason.
- Add features I haven't asked for, even "obvious" ones.
- Promote experimental into the public API yet — stays in `Experimental/` until
  the migration plan is confirmed.

Start by reading DESIGN.md and confirming what you see. Then propose which
backlog item to tackle and wait for me to pick.
```
