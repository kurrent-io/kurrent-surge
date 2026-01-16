# RFC: [Title]

---

> **🤖 AGENT INSTRUCTIONS:** An RFC proposes a significant change and invites discussion BEFORE a decision is made. Keep it focused on one proposal. The goal is to surface concerns, alternatives, and implications early. Write to persuade thoughtful skeptics, not to rubber-stamp a predetermined decision.

---

## Metadata

| Field | Value |
|-------|-------|
| **RFC ID** | RFC-[XXX] |
| **Title** | [Descriptive title] |
| **Author(s)** | [Names] |
| **Status** | [Draft / In Review / Accepted / Rejected / Superseded] |
| **Created** | [Date] |
| **Last Updated** | [Date] |
| **Review Period** | [Start Date] — [End Date] |
| **Decision Date** | [Date or TBD] |
| **Related PRD** | [Link if applicable] |
| **Related RFCs** | [Links if applicable] |
| **Resulting ADR** | [Link — added after decision] |

---

## Summary

> **🤖 AGENT INSTRUCTIONS:** Write 2-4 sentences that capture the essence of the proposal. A busy reader should understand what's being proposed from this section alone.

[One paragraph summary of what is being proposed]

---

## Motivation

> **🤖 AGENT INSTRUCTIONS:** Explain WHY this change is needed. What problem does it solve? What opportunity does it enable? What's the cost of NOT doing this? Include data or evidence where possible.

### Problem Statement

[What problem or opportunity prompted this RFC?]

### Current State

[How does the current system work? What are its limitations?]

### Why Now?

[Why is this the right time to address this? What has changed?]

---

## Detailed Proposal

> **🤖 AGENT INSTRUCTIONS:** Describe the proposed solution in enough detail that reviewers can evaluate it. Include architecture, key components, and how it addresses the motivation. Use diagrams where helpful.

### Overview

[High-level description of the proposed solution]

### Architecture / Design

[More detailed design, with diagrams if helpful]
[DIAGRAM IF APPLICABLE]

### Key Components

| Component | Purpose |
|-----------|---------|
| [Component] | [What it does] |

### Implementation Approach

[How would this be implemented? Phases? Key steps?]

---

## Impact Analysis

> **🤖 AGENT INSTRUCTIONS:** Help reviewers understand the full implications. Consider all stakeholders and systems affected.

### Who/What is Affected?

| Affected Party | Impact |
|----------------|--------|
| [Team/System A] | [How they're affected] |
| [Team/System B] | [How they're affected] |
| [Users] | [How they're affected] |

### Migration / Transition

[How do we get from current state to proposed state?]

### Backward Compatibility

[What breaks? What's preserved? For how long?]

---

## Trade-offs & Risks

> **🤖 AGENT INSTRUCTIONS:** Every proposal has downsides. Acknowledge them honestly — this builds trust and helps reviewers engage constructively.

### Benefits

- ✅ [Benefit 1]
- ✅ [Benefit 2]
- ✅ [Benefit 3]

### Drawbacks

- ⚠️ [Drawback 1]
- ⚠️ [Drawback 2]

### Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| [Risk] | [H/M/L] | [H/M/L] | [How to mitigate] |

---

## Alternatives Considered

> **🤖 AGENT INSTRUCTIONS:** Show that you've thought beyond the obvious. For each alternative, explain why it was rejected. This demonstrates rigor and preempts "why not just do X?" questions.

### Alternative 1: [Name]

**Description:** [Brief description]

**Pros:**
- [Pro]

**Cons:**
- [Con]

**Why Rejected:** [Reason this wasn't chosen]

### Alternative 2: [Name]

**Description:** [Brief description]

**Pros:**
- [Pro]

**Cons:**
- [Con]

**Why Rejected:** [Reason this wasn't chosen]

### Alternative 3: Do Nothing

**Description:** Maintain the status quo.

**Pros:**
- No implementation cost
- No migration risk

**Cons:**
- [Problems that persist]

**Why Rejected:** [Why the status quo is unacceptable]

---

## Resource Estimation

> **🤖 AGENT INSTRUCTIONS:** Provide rough estimates to help decision-makers understand the investment required. Be honest about uncertainty.

| Resource | Estimate | Confidence |
|----------|----------|------------|
| Engineering effort | [e.g., 2-4 weeks] | [High/Medium/Low] |
| Timeline | [e.g., Q2 2026] | [High/Medium/Low] |
| Infrastructure cost | [e.g., +$500/month] | [High/Medium/Low] |
| Ongoing maintenance | [e.g., 0.5 FTE] | [High/Medium/Low] |

---

## Open Questions

> **🤖 AGENT INSTRUCTIONS:** List questions you want reviewers to address. This focuses the discussion on what matters.

1. [Question you want feedback on]
2. [Another question]
3. [Technical uncertainty to resolve]

---

## Review & Feedback

> **🤖 AGENT INSTRUCTIONS:** This section is filled in during the review period. Capture feedback and how it was addressed.

### Reviewers

| Reviewer | Role | Status |
|----------|------|--------|
| [Name] | [Role] | [Pending / Approved / Concerns] |

### Feedback Summary

| Feedback | From | Response | Resolution |
|----------|------|----------|------------|
| [Concern or suggestion] | [Name] | [How addressed] | [Resolved / Open] |

---

## Decision

> **🤖 AGENT INSTRUCTIONS:** Filled in after the review period ends. Document the outcome clearly.

**Decision:** [Accepted / Rejected / Accepted with Modifications / Deferred]

**Decision Date:** [Date]

**Decision Maker(s):** [Name(s)]

**Rationale:** [Brief explanation of why this decision was made]

**Next Steps:**
1. [Action item]
2. [Action item]

**Resulting ADR:** [Link to ADR documenting this decision]

---

## Appendix

### Glossary

| Term | Definition |
|------|------------|
| [Term] | [Definition] |

### References

- [Link to relevant documentation]
- [Link to related RFC]
- [Link to research or data]

---

> **🤖 AGENT META-INSTRUCTIONS:**
>
> When helping write an RFC:
> 1. **Focus on one decision** — If you're proposing multiple things, split into multiple RFCs
> 2. **Be honest about trade-offs** — Hiding drawbacks erodes trust and leads to surprises
> 3. **Show alternatives seriously** — Don't strawman alternatives; consider them genuinely
> 4. **Invite specific feedback** — Open questions focus reviewers on what matters
> 5. **Keep scope appropriate** — RFCs are for significant, cross-cutting, or hard-to-reverse decisions
> 6. **Update during review** — Incorporate feedback into the RFC; track changes
> 7. **Close the loop** — Always record the decision and resulting ADR
