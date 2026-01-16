# Technical Documentation Guide
## From PRD to Implementation: A Complete Framework

---

> **🤖 AGENT INSTRUCTIONS:** This guide explains the documentation ecosystem for product development. Use it to understand when and how to use each document type. When helping users, reference this guide to recommend the appropriate documentation format for their situation.

---

## Overview: The Documentation Flow

Product development requires different types of documentation at different stages. Each document serves a specific purpose and audience.
```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         DOCUMENTATION FLOW                                   │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│   ┌─────────┐      ┌─────────┐      ┌─────────────┐      ┌─────────┐       │
│   │   PRD   │ ───► │   RFC   │ ───► │   System    │ ───► │  Code   │       │
│   │         │      │(optional)│      │Architecture │      │         │       │
│   └─────────┘      └─────────┘      └──────┬──────┘      └─────────┘       │
│    WHAT/WHY         PROPOSE               │               IMPLEMENT         │
│                                           │                                  │
│                              ┌────────────┼────────────┐                    │
│                              ▼            ▼            ▼                    │
│                        ┌──────────┐ ┌──────────┐ ┌──────────┐              │
│                        │ Feature  │ │ Feature  │ │ Platform │              │
│                        │ Spec A   │ │ Spec B   │ │ Spec     │              │
│                        └──────────┘ └──────────┘ └──────────┘              │
│                              │            │            │                    │
│                              └────────────┼────────────┘                    │
│                                           ▼                                  │
│                                     ┌──────────┐                            │
│                                     │   ADRs   │                            │
│                                     │(ongoing) │                            │
│                                     └──────────┘                            │
│                                       RECORD                                 │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## The Document Types

### Quick Reference

| Document                    | Purpose           | Quantity                          | Length     |
|-----------------------------|-------------------|-----------------------------------|------------|
| **PRD**                     | WHAT & WHY        | 1 per product/major feature       | 2-15 pages |
| **RFC**                     | Propose & discuss | As needed (significant decisions) | 2-10 pages |
| **System Architecture Doc** | The "map"         | 1 per product                     | 3-5 pages  |
| **Feature/Component Spec**  | HOW (detailed)    | Many per product                  | 2-10 pages |
| **ADR**                     | Record decisions  | Many (1 per decision)             | 1-2 pages  |

---

## 1. PRD (Product Requirements Document)

**Purpose:** Defines WHAT to build and WHY

**Answers:**
- What problem are we solving?
- Who are we solving it for?
- What does success look like?
- What features are needed?
- What's in scope and out of scope?

| Attribute    | Details                                               |
|--------------|-------------------------------------------------------|
| **Audience** | Everyone (product, design, engineering, stakeholders) |
| **Author**   | Product Manager, Founder, or Product Owner            |
| **Timing**   | Before design and development begins                  |
| **Length**   | 2-15 pages depending on complexity                    |
| **Lifespan** | Living document, updated as requirements evolve       |
| **Quantity** | 1 per product or major initiative                     |

**Key Principle:** A PRD should be understandable by anyone in the organization, regardless of technical background.

---

## 2. RFC (Request for Comments)

**Purpose:** Proposes a significant change and solicits feedback BEFORE committing to a decision

**Answers:**
- What are we proposing?
- Why is this change needed?
- How would it work at a high level?
- What are the trade-offs and risks?
- What alternatives exist?

| Attribute    | Details                                                                |
|--------------|------------------------------------------------------------------------|
| **Audience** | Engineering team, technical stakeholders                               |
| **Author**   | Engineer, Tech Lead, or Architect proposing the change                 |
| **Timing**   | Before making significant technical decisions                          |
| **Length**   | 2-10 pages                                                             |
| **Lifespan** | Active during review period, then archived                             |
| **Quantity** | As needed — for significant, cross-cutting, or controversial decisions |

**When to Write an RFC:**
- Major architectural changes
- Introducing new systems or services
- Changes affecting multiple teams
- New patterns, standards, or conventions
- Anything controversial, expensive, or hard to reverse

**When NOT to Write an RFC:**
- Small, localized changes
- Decisions within a single team's domain
- Standard implementations following established patterns
- Urgent fixes (document after with an ADR if significant)

**Key Principle:** RFCs are collaborative. Their purpose is to surface concerns and alternatives early, not to rubber-stamp a predetermined decision.

**RFC Lifecycle:**
```
┌──────────┐    ┌──────────┐    ┌──────────┐    ┌──────────┐
│  Draft   │ ─► │  Review  │ ─► │ Decision │ ─► │ Archived │
│          │    │          │    │          │    │          │
└──────────┘    └──────────┘    └──────────┘    └──────────┘
  Author          Team          Accepted/        Reference
  writes          discusses     Rejected/        for future
                                Modified
```

---

## 3. System Architecture Doc

**Purpose:** The "map" of your system — shows how everything fits together

**Answers:**
- What are the major components/services?
- How do they relate to each other?
- What are the system boundaries?
- How is it deployed?
- What conventions apply across the system?

| Attribute    | Details                                                          |
|--------------|------------------------------------------------------------------|
| **Audience** | Engineering team, new team members                               |
| **Author**   | Tech Lead or Architect                                           |
| **Timing**   | Early in product development, before detailed specs              |
| **Length**   | 3-5 pages (intentionally lightweight)                            |
| **Lifespan** | Living document, updated when architecture changes significantly |
| **Quantity** | 1 per product                                                    |

**What it IS:**
- A high-level map
- An index pointing to detailed specs
- A starting point for onboarding
- A place for shared conventions

**What it is NOT:**
- Detailed design (that's in Feature Specs)
- Implementation instructions
- A dump of all technical decisions (that's ADRs)

**Key Principle:** Keep it lightweight. The Architecture Doc points to Feature Specs for detail — it doesn't duplicate them.

---

## 4. Feature/Component Spec

**Purpose:** Detailed HOW document for a specific capability

**Answers:**
- How does this feature work internally?
- What are the components and how do they interact?
- What are the APIs and data models?
- What are the edge cases and error scenarios?
- How will we test it?

| Attribute    | Details                                                  |
|--------------|----------------------------------------------------------|
| **Audience** | Engineers building the feature                           |
| **Author**   | Tech Lead or Senior Engineer                             |
| **Timing**   | After architecture is established, before implementation |
| **Length**   | 2-10 pages depending on complexity                       |
| **Lifespan** | Active during development, then reference material       |
| **Quantity** | Many per product — one per feature or component          |

**Two Types of Feature Specs:**

| Type                      | Examples                                                 | Notes                                                |
|---------------------------|----------------------------------------------------------|------------------------------------------------------|
| **Product Feature Specs** | Pipeline Builder, Connector Framework, User Dashboard    | Capabilities visible to end users                    |
| **Platform Specs**        | Auth & Identity, Logging & Observability, Error Handling | Cross-cutting concerns that serve internal consumers |

**Key Principle:** Platform specs (auth, logging, etc.) are just feature specs for internal capabilities. Use the same template — they deserve the same rigor.

**Feature Spec Structure:**
- Modular sections — include what's relevant, skip what's not
- Heavy on sequence diagrams — often multiple per spec
- API contracts can be actual files (protobuf, OpenAPI) — reference or embed them
- Security and performance sections only when feature has unique needs

---

## 5. ADR (Architecture Decision Record)

**Purpose:** Documents a SINGLE significant decision with its context and consequences

**Answers:**
- What decision was made?
- What was the context?
- What options were considered?
- Why was this option chosen?
- What are the consequences?

| Attribute    | Details                                    |
|--------------|--------------------------------------------|
| **Audience** | Current and future engineers               |
| **Author**   | Anyone involved in the decision            |
| **Timing**   | Immediately after a significant decision   |
| **Length**   | 1-2 pages (intentionally brief)            |
| **Lifespan** | Permanent — never deleted, only superseded |
| **Quantity** | Many — one per significant decision        |

**When to Write an ADR:**
- Choosing databases, frameworks, or major libraries
- Selecting architectural patterns
- API style decisions (REST, GraphQL, gRPC)
- Authentication/authorization approaches
- Any decision that's hard to reverse
- Any decision future engineers will question

**Key Principle:** ADRs are lightweight and focused. One decision = one ADR. They create institutional memory so future engineers understand WHY.

**ADR Lifecycle:**
```
┌───────────┐    ┌─────────────┐    ┌────────────┐
│ Proposed  │ ─► │  Accepted   │ ─► │ Superseded │
│           │    │             │    │ (optional) │
└───────────┘    └─────────────┘    └────────────┘
                       │
                       ▼
                ┌─────────────┐
                │  Deprecated │
                │  (optional) │
                └─────────────┘
```

---

## How the Documents Work Together

### The Documentation Hierarchy
```
┌─────────────────────────────────────────────────────────────────────────────┐
│                                                                             │
│   PRD: Kurrent Processors                                                   │
│   └── Defines WHAT we're building and WHY                                  │
│                                                                             │
│         │                                                                   │
│         ▼                                                                   │
│                                                                             │
│   System Architecture Doc                                                   │
│   └── The "map" — shows all components and how they relate                 │
│       └── Points to specs for detail                                       │
│                                                                             │
│         │                                                                   │
│         ├─────────────────────┬─────────────────────┐                      │
│         ▼                     ▼                     ▼                      │
│                                                                             │
│   Feature Specs           Feature Specs         Platform Specs             │
│   (Product)               (Product)             (Cross-cutting)            │
│                                                                             │
│   ┌─────────────────┐   ┌─────────────────┐   ┌─────────────────┐         │
│   │ Pipeline        │   │ Connector       │   │ Auth &          │         │
│   │ Builder Spec    │   │ Framework Spec  │   │ Identity Spec   │         │
│   └─────────────────┘   └─────────────────┘   └─────────────────┘         │
│   ┌─────────────────┐   ┌─────────────────┐   ┌─────────────────┐         │
│   │ Execution       │   │ Monitoring      │   │ Observability   │         │
│   │ Engine Spec     │   │ Dashboard Spec  │   │ Spec            │         │
│   └─────────────────┘   └─────────────────┘   └─────────────────┘         │
│                                                                             │
│         │                     │                     │                      │
│         └─────────────────────┴─────────────────────┘                      │
│                               │                                             │
│                               ▼                                             │
│                                                                             │
│   ADRs (scattered throughout — one per decision)                           │
│                                                                             │
│   • ADR-001: Use Kafka for streaming backbone                              │
│   • ADR-002: Use Protobuf for message serialization                        │
│   • ADR-003: Multi-tenant isolation via namespaces                         │
│   • ADR-004: OAuth2 + JWT for authentication                               │
│   • ...                                                                     │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Information Flow Between Documents

| From             | To               | What Flows                                        |
|------------------|------------------|---------------------------------------------------|
| PRD              | Architecture Doc | Problem context, user needs, feature list         |
| PRD              | Feature Specs    | User stories, acceptance criteria                 |
| Architecture Doc | Feature Specs    | System context, component boundaries, conventions |
| RFC              | ADR              | Decision outcome (RFC proposes → ADR records)     |
| RFC              | Feature Specs    | Approved approach to implement                    |
| Feature Specs    | ADRs             | Significant decisions made during design          |

---

## Practical Example: Building Kurrent Processors

| Stage                      | Document         | Example                                                                                     |
|----------------------------|------------------|---------------------------------------------------------------------------------------------|
| **Define product**         | PRD              | "Kurrent Processors enables data engineers to build stream processing pipelines 10x faster" |
| **Propose major decision** | RFC              | "RFC-001: Use Apache Kafka as streaming backbone"                                           |
| **Record decision**        | ADR              | "ADR-001: Use Kafka over Pulsar — better ecosystem, team familiarity"                       |
| **Map the system**         | Architecture Doc | Shows Pipeline Service, Connector Registry, Execution Engine, and how they connect          |
| **Design features**        | Feature Specs    | Separate specs for Pipeline Builder, Connector Framework, Execution Engine                  |
| **Design platform**        | Platform Specs   | Separate specs for Auth, Observability, Error Handling                                      |
| **Record more decisions**  | ADRs             | "ADR-002: Protobuf for serialization", "ADR-003: Kubernetes for orchestration"              |
| **Build**                  | Code             | Implementation following the specs                                                          |

---

## Quick Reference: Choosing the Right Document

| Situation                                                  | Document                                         |
|------------------------------------------------------------|--------------------------------------------------|
| "I need to define what we're building and why"             | **PRD**                                          |
| "I want to propose a major change and get feedback"        | **RFC**                                          |
| "I need to show how the whole system fits together"        | **System Architecture Doc**                      |
| "I need to design how a specific feature works"            | **Feature Spec**                                 |
| "I need to design a cross-cutting concern (auth, logging)" | **Feature Spec** (Platform)                      |
| "I want to record why we made a decision"                  | **ADR**                                          |
| "I need to onboard a new engineer"                         | Architecture Doc → relevant Feature Specs → ADRs |

---

## Team Size Recommendations

### Solo Developer / Very Small Team (1-3)

| Document         | Recommendation                       |
|------------------|--------------------------------------|
| PRD              | Lightweight version (Product Brief)  |
| RFC              | Skip — just decide and record in ADR |
| Architecture Doc | Brief overview                       |
| Feature Specs    | For complex features only            |
| ADRs             | Yes — you'll thank yourself later    |

### Small Team (4-10)

| Document         | Recommendation               |
|------------------|------------------------------|
| PRD              | Full PRD                     |
| RFC              | For significant decisions    |
| Architecture Doc | Yes                          |
| Feature Specs    | For all non-trivial features |
| ADRs             | Yes — build the habit        |

### Medium/Large Team (10+)

| Document         | Recommendation                    |
|------------------|-----------------------------------|
| PRD              | Full PRD with formal review       |
| RFC              | Required for cross-team decisions |
| Architecture Doc | Yes, with ownership               |
| Feature Specs    | Required for all features         |
| ADRs             | Required, with tooling            |

---

## Anti-Patterns to Avoid

| Anti-Pattern              | Problem                                       | Solution                                    |
|---------------------------|-----------------------------------------------|---------------------------------------------|
| **Documentation Theater** | Writing docs nobody reads                     | Keep docs focused and actionable            |
| **Monolithic Specs**      | One giant doc covering everything             | Split into Architecture Doc + Feature Specs |
| **Tribal Knowledge**      | "Everyone knows why we did that"              | Write ADRs for every significant decision   |
| **RFC as Rubber Stamp**   | Using RFCs to announce decisions already made | RFCs should invite genuine input            |
| **Orphaned Documents**    | Docs that don't link to each other            | Cross-reference related documents           |
| **Over-Documentation**    | Every tiny feature gets a spec                | Scale documentation to complexity           |
| **Under-Documentation**   | Complex features with no specs                | If it takes >2 days, write a spec           |

---

## Templates

The following templates are provided separately:

| Template                    | Purpose                        |
|-----------------------------|--------------------------------|
| **PRD (Brief)**             | Lightweight product definition |
| **PRD (Comprehensive)**     | Full product requirements      |
| **System Architecture Doc** | High-level system map          |
| **Feature/Component Spec**  | Detailed feature design        |
| **RFC**                     | Proposing significant changes  |
| **ADR**                     | Recording individual decisions |

---

> **🤖 AGENT META-INSTRUCTIONS:**
>
> When helping users with documentation:
> 1. **Assess their stage** — Are they defining (PRD), proposing (RFC), mapping (Arch), designing (Spec), or recording (ADR)?
> 2. **Match document to complexity** — Simple features may not need full specs
> 3. **Encourage the Architecture Doc** — Teams often skip this and regret it
> 4. **Treat platform concerns as features** — Auth, logging, etc. deserve specs too
> 5. **Push for ADRs** — Teams consistently undervalue decision records
> 6. **Connect documents** — Ensure docs reference each other appropriately
> 7. **Keep it practical** — Documentation should enable work, not block it
