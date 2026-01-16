# ADR-[XXX]: [Title]

---

> **🤖 AGENT INSTRUCTIONS:** An ADR documents a SINGLE architectural decision. Keep it short (1-2 pages max). The goal is to create a permanent record that future team members can reference to understand WHY the system is the way it is. Write ADRs immediately after decisions are made, while context is fresh.

---

## Metadata

| Field | Value |
|-------|-------|
| **ADR ID** | ADR-[XXX] |
| **Title** | [Short descriptive title of the decision] |
| **Status** | [Proposed / Accepted / Deprecated / Superseded] |
| **Date** | [Date decision was made] |
| **Author(s)** | [Who made or documented the decision] |
| **Deciders** | [Who was involved in the decision] |
| **Related RFC** | [Link if this came from an RFC] |
| **Supersedes** | [ADR-XXX if this replaces a previous decision] |
| **Superseded by** | [ADR-XXX if this has been replaced] |

---

## Status

> **🤖 AGENT INSTRUCTIONS:** Use one of these statuses:
> - **Proposed** — Under discussion, not yet decided
> - **Accepted** — Decision has been made and is in effect
> - **Deprecated** — No longer recommended but may still exist in codebase
> - **Superseded** — Replaced by another ADR (link to it)

**[STATUS]**

[Optional: Brief note about status, e.g., "Superseded by ADR-015 on 2026-03-15"]

---

## Context

> **🤖 AGENT INSTRUCTIONS:** Describe the situation that led to this decision. What problem were you facing? What constraints existed? Include enough background that someone unfamiliar with the project can understand why a decision was needed. Be factual and neutral — save opinions for the Decision section.

[Describe the context and problem that required a decision]

**Key Constraints:**
- [Constraint 1 — e.g., "Must integrate with existing PostgreSQL database"]
- [Constraint 2 — e.g., "Team has limited experience with technology X"]
- [Constraint 3 — e.g., "Must support 10,000 concurrent users"]

**Forces at Play:**
- [Force 1 — e.g., "Need for fast iteration vs. long-term maintainability"]
- [Force 2 — e.g., "Cost vs. performance requirements"]

---

## Decision

> **🤖 AGENT INSTRUCTIONS:** State the decision clearly and directly. Use active voice: "We will..." not "It was decided that..." This section should be unambiguous — a reader should know exactly what was decided.

**We will [decision statement].**

[Expand with 1-3 sentences providing additional detail if needed]

---

## Rationale

> **🤖 AGENT INSTRUCTIONS:** Explain WHY this decision was made. This is the most valuable part of an ADR — it captures the reasoning that would otherwise be lost. Reference the options considered and why this one was chosen.

[Explain the reasoning behind the decision]

**Why this option:**
- [Reason 1]
- [Reason 2]
- [Reason 3]

---

## Options Considered

> **🤖 AGENT INSTRUCTIONS:** List all options that were seriously considered. For each, note key pros and cons. This shows the decision was thoughtful and helps future readers understand the trade-offs.

### Option 1: [Name] ← **CHOSEN**

[Brief description]

| Pros | Cons |
|------|------|
| ✅ [Pro] | ⚠️ [Con] |
| ✅ [Pro] | ⚠️ [Con] |

### Option 2: [Name]

[Brief description]

| Pros | Cons |
|------|------|
| ✅ [Pro] | ⚠️ [Con] |
| ✅ [Pro] | ⚠️ [Con] |

**Why not chosen:** [Brief explanation]

### Option 3: [Name]

[Brief description]

| Pros | Cons |
|------|------|
| ✅ [Pro] | ⚠️ [Con] |

**Why not chosen:** [Brief explanation]

---

## Consequences

> **🤖 AGENT INSTRUCTIONS:** Describe the implications of this decision — both positive and negative. Be honest about trade-offs. This helps future readers understand what to expect and what to watch for.

### Positive Consequences

- ✅ [Positive outcome 1]
- ✅ [Positive outcome 2]
- ✅ [Positive outcome 3]

### Negative Consequences

- ⚠️ [Trade-off or downside 1]
- ⚠️ [Trade-off or downside 2]

### Neutral / Notes

- ℹ️ [Other implications or things to be aware of]

---

## Implementation Notes

> **🤖 AGENT INSTRUCTIONS:** Optional section. Include brief notes on how this decision should be implemented, if not obvious. Keep it high-level — detailed implementation belongs in the Tech Spec.

[Any notes on implementation, if helpful]

---

## Related Decisions

> **🤖 AGENT INSTRUCTIONS:** Link to related ADRs. Decisions often form chains or clusters — make these connections explicit.

| ADR | Relationship |
|-----|--------------|
| [ADR-XXX] | [e.g., "Depends on this decision"] |
| [ADR-XXX] | [e.g., "Related authentication decision"] |

---

## References

- [Link to RFC if applicable]
- [Link to Tech Spec]
- [Link to relevant documentation or research]
- [Link to discussion thread where decision was made]

---

> **🤖 AGENT META-INSTRUCTIONS:**
>
> When helping write an ADR:
> 1. **One decision per ADR** — If you're documenting multiple decisions, create multiple ADRs
> 2. **Write immediately** — Capture decisions while context is fresh
> 3. **Be concise** — ADRs should be 1-2 pages max; longer means you're over-explaining
> 4. **Stay neutral in Context** — Save opinions for Decision and Rationale
> 5. **Be honest about consequences** — Don't hide negative implications
> 6. **Never delete ADRs** — Mark as Deprecated or Superseded instead; history matters
> 7. **Use clear titles** — "Use PostgreSQL for user data" not "Database decision"
> 8. **Number sequentially** — ADR-001, ADR-002, etc. Never reuse numbers
> 9. **Link related ADRs** — Decisions form chains; make connections explicit
