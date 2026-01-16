# System Architecture Document
## [Product Name]

---

> **🤖 AGENT INSTRUCTIONS:** This is the "map" of the system — intentionally lightweight. Its job is to show how everything fits together and point to detailed specs. Do NOT duplicate detail from Feature Specs here. If you find yourself writing more than 5 pages, you're probably including too much detail.

---

## Document Control

| Field             | Value                        |
|-------------------|------------------------------|
| **Product**       | [Product Name]               |
| **Author(s)**     | [Names]                      |
| **Status**        | [Draft / Current / Outdated] |
| **Last Updated**  | [Date]                       |
| **PRD Reference** | [Link to PRD]                |

---

## 1. Overview

> **🤖 AGENT INSTRUCTIONS:** 2-3 paragraphs max. Describe what this system does at a high level. A new team member should understand the system's purpose after reading this section.

[Brief description of the system, its purpose, and primary users]

### 1.1 Key Capabilities

| Capability     | Description            | Spec Reference         |
|----------------|------------------------|------------------------|
| [Capability 1] | [One-line description] | [Link to Feature Spec] |
| [Capability 2] | [One-line description] | [Link to Feature Spec] |
| [Capability 3] | [One-line description] | [Link to Feature Spec] |

---

## 2. System Diagram

> **🤖 AGENT INSTRUCTIONS:** One primary diagram showing all major components and how they interact. This is the most important part of the document. Keep it high-level — detailed component internals belong in Feature Specs.
```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           [SYSTEM DIAGRAM]                                   │
│                                                                              │
│   ┌──────────────┐                           ┌──────────────┐               │
│   │              │                           │              │               │
│   │   Client     │ ────────────────────────► │   API        │               │
│   │   Apps       │                           │   Gateway    │               │
│   │              │                           │              │               │
│   └──────────────┘                           └──────┬───────┘               │
│                                                     │                        │
│                          ┌──────────────────────────┼──────────────────┐    │
│                          │                          │                  │    │
│                          ▼                          ▼                  ▼    │
│                   ┌──────────────┐          ┌──────────────┐   ┌──────────┐│
│                   │              │          │              │   │          ││
│                   │  Service A   │ ◄──────► │  Service B   │   │Service C ││
│                   │              │          │              │   │          ││
│                   └──────┬───────┘          └──────┬───────┘   └────┬─────┘│
│                          │                         │                │      │
│                          ▼                         ▼                ▼      │
│                   ┌──────────────┐          ┌──────────────┐   ┌──────────┐│
│                   │  Database A  │          │  Database B  │   │  Cache   ││
│                   └──────────────┘          └──────────────┘   └──────────┘│
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

[Brief description of what the diagram shows — 2-3 sentences]

---

## 3. Component Inventory

> **🤖 AGENT INSTRUCTIONS:** List ALL components/services in the system. This is an index — link to Feature Specs for detail. One row per component, keep descriptions brief.

### 3.1 Product Components

| Component     | Responsibility            | Tech Stack         | Spec   |
|---------------|---------------------------|--------------------|--------|
| [Component A] | [One-line responsibility] | [Key technologies] | [Link] |
| [Component B] | [One-line responsibility] | [Key technologies] | [Link] |
| [Component C] | [One-line responsibility] | [Key technologies] | [Link] |

### 3.2 Platform Components

| Component               | Responsibility            | Tech Stack         | Spec   |
|-------------------------|---------------------------|--------------------|--------|
| [Auth Service]          | [One-line responsibility] | [Key technologies] | [Link] |
| [Logging/Observability] | [One-line responsibility] | [Key technologies] | [Link] |
| [API Gateway]           | [One-line responsibility] | [Key technologies] | [Link] |

### 3.3 Data Stores

| Store        | Type                    | Purpose          | Owner Component |
|--------------|-------------------------|------------------|-----------------|
| [Database A] | [PostgreSQL/Redis/etc.] | [What it stores] | [Component]     |
| [Database B] | [Type]                  | [Purpose]        | [Component]     |

### 3.4 External Dependencies

| Dependency           | Type            | Purpose         |
|----------------------|-----------------|-----------------|
| [External Service A] | [API/SaaS/etc.] | [Why we use it] |
| [External Service B] | [Type]          | [Purpose]       |

---

## 4. Key Flows

> **🤖 AGENT INSTRUCTIONS:** Show 2-3 of the most important flows through the system. These should be the "golden paths" that new engineers need to understand. Detailed flows belong in Feature Specs — keep these high-level.

### 4.1 [Primary Flow Name]
```
┌────────┐     ┌─────────┐     ┌─────────┐     ┌─────────┐
│ Client │ ──► │ Gateway │ ──► │Service A│ ──► │Database │
└────────┘     └─────────┘     └─────────┘     └─────────┘
```

[1-2 sentence description]

### 4.2 [Secondary Flow Name]
```
[Simple flow diagram]
```

[1-2 sentence description]

---

## 5. Deployment Overview

> **🤖 AGENT INSTRUCTIONS:** High-level deployment topology. Where does this system run? How are components deployed? Do NOT include detailed deployment procedures — those belong in runbooks.

### 5.1 Environments

| Environment | Purpose                | Infrastructure                |
|-------------|------------------------|-------------------------------|
| Development | Local development      | [e.g., Docker Compose]        |
| Staging     | Pre-production testing | [e.g., Kubernetes - small]    |
| Production  | Live traffic           | [e.g., Kubernetes - multi-AZ] |

### 5.2 Deployment Diagram
```
┌─────────────────────────────────────────────────────────────────┐
│                    [DEPLOYMENT DIAGRAM]                          │
│                                                                  │
│   Region: [e.g., us-east-1]                                     │
│   ┌─────────────────────────────────────────────────────────┐   │
│   │                    Kubernetes Cluster                    │   │
│   │                                                          │   │
│   │   ┌─────────┐  ┌─────────┐  ┌─────────┐                │   │
│   │   │Service A│  │Service B│  │Service C│                │   │
│   │   │ (x3)    │  │ (x2)    │  │ (x2)    │                │   │
│   │   └─────────┘  └─────────┘  └─────────┘                │   │
│   └─────────────────────────────────────────────────────────┘   │
│                                                                  │
│   ┌─────────────────┐  ┌─────────────────┐                     │
│   │   RDS (Primary) │  │  RDS (Replica)  │                     │
│   └─────────────────┘  └─────────────────┘                     │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## 6. Cross-Cutting Conventions

> **🤖 AGENT INSTRUCTIONS:** Document conventions that apply across all components. Keep it brief — if a convention needs detailed explanation, it should have its own Platform Spec.

### 6.1 API Conventions

| Convention     | Standard                             |
|----------------|--------------------------------------|
| API Style      | [e.g., REST / gRPC / GraphQL]        |
| Versioning     | [e.g., URL path: /api/v1/]           |
| Authentication | [e.g., OAuth2 + JWT — see Auth Spec] |
| Error Format   | [e.g., RFC 7807 Problem Details]     |

### 6.2 Data Conventions

| Convention | Standard                                        |
|------------|-------------------------------------------------|
| ID Format  | [e.g., UUIDv4]                                  |
| Timestamps | [e.g., ISO 8601 UTC]                            |
| Naming     | [e.g., snake_case for JSON, camelCase for code] |

### 6.3 Observability Conventions

| Convention | Standard                                         |
|------------|--------------------------------------------------|
| Logging    | [e.g., Structured JSON — see Observability Spec] |
| Metrics    | [e.g., Prometheus format]                        |
| Tracing    | [e.g., OpenTelemetry]                            |

---

## 7. Decision Log

> **🤖 AGENT INSTRUCTIONS:** Link to key ADRs that shaped this architecture. This helps readers understand WHY the system is designed this way.

| ADR       | Decision                                     | Date   |
|-----------|----------------------------------------------|--------|
| [ADR-001] | [e.g., Use Kafka for event streaming]        | [Date] |
| [ADR-002] | [e.g., PostgreSQL for primary data store]    | [Date] |
| [ADR-003] | [e.g., Multi-tenant via namespace isolation] | [Date] |

---

## 8. Spec Index

> **🤖 AGENT INSTRUCTIONS:** Complete index of all related specs. This is the "table of contents" for the technical documentation.

### 8.1 Feature Specs

| Spec                                | Status          | Last Updated |
|-------------------------------------|-----------------|--------------|
| [Feature Spec: Pipeline Builder]    | [Draft/Current] | [Date]       |
| [Feature Spec: Connector Framework] | [Status]        | [Date]       |
| [Feature Spec: Execution Engine]    | [Status]        | [Date]       |

### 8.2 Platform Specs

| Spec                             | Status   | Last Updated |
|----------------------------------|----------|--------------|
| [Platform Spec: Auth & Identity] | [Status] | [Date]       |
| [Platform Spec: Observability]   | [Status] | [Date]       |
| [Platform Spec: Error Handling]  | [Status] | [Date]       |

### 8.3 Related Documents

| Document      | Link   |
|---------------|--------|
| PRD           | [Link] |
| ADR Directory | [Link] |
| Runbooks      | [Link] |

---

> **🤖 AGENT META-INSTRUCTIONS:**
>
> When helping complete this Architecture Doc:
> 1. **Keep it lightweight** — If it's getting long, move detail to Feature Specs
> 2. **Focus on the diagram** — The system diagram is the most valuable part
> 3. **Link, don't duplicate** — Point to Feature Specs for detail
> 4. **Maintain the index** — Keep the Spec Index current as new specs are added
> 5. **Update rarely** — This doc should only change for significant architectural shifts
> 6. **Onboarding test** — A new engineer should understand the system's shape after reading this
