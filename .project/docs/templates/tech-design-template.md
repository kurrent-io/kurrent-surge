# Technical Design Document / Spec
## [Project/Feature Name]

---

> **🤖 AGENT INSTRUCTIONS:** This is the most detailed technical document in the stack. It bridges the PRD (WHAT/WHY) to implementation (CODE). Focus on architectural clarity, not implementation minutiae. The goal is that any engineer could implement this system from this document. Always reference the parent PRD and any relevant RFCs/ADRs.

---

## Document Control

| Field             | Value                                        |
|-------------------|----------------------------------------------|
| **Project Name**  | [Project Name]                               |
| **Author(s)**     | [Names]                                      |
| **Reviewers**     | [Names]                                      |
| **Status**        | [Draft / In Review / Approved / Implemented] |
| **Last Updated**  | [Date]                                       |
| **PRD Reference** | [Link to PRD]                                |
| **Related RFCs**  | [Links to relevant RFCs]                     |
| **Related ADRs**  | [Links to relevant ADRs]                     |

### Version History

| Version | Date   | Author | Changes       |
|---------|--------|--------|---------------|
| 0.1     | [Date] | [Name] | Initial draft |

---

## 1. Overview

### 1.1 Summary

> **🤖 AGENT INSTRUCTIONS:** Write 2-3 paragraphs that summarize what this system does, why it's being built, and the high-level approach. A reader should understand the essence of the project from this section alone.

[Brief description of the system and its purpose. Reference the PRD for detailed product requirements.]

### 1.2 Goals

> **🤖 AGENT INSTRUCTIONS:** List the technical goals this design aims to achieve. These should align with PRD goals but focus on technical outcomes (performance, scalability, maintainability, etc.).

| Goal                           | Description   | Measurable Target                          |
|--------------------------------|---------------|--------------------------------------------|
| [e.g., Low latency]            | [Description] | [e.g., p99 < 100ms]                        |
| [e.g., High availability]      | [Description] | [e.g., 99.9% uptime]                       |
| [e.g., Horizontal scalability] | [Description] | [e.g., Linear scaling to 10x current load] |

### 1.3 Non-Goals

> **🤖 AGENT INSTRUCTIONS:** Explicitly state what this design does NOT address. This prevents scope creep and sets expectations.

- [Technical capability explicitly not addressed]
- [Another non-goal]

### 1.4 Background

> **🤖 AGENT INSTRUCTIONS:** Provide context needed to understand this design. Include relevant history, current system state, and why this work is happening now. Link to PRD for product context.

[Background information, current state, why this design is needed]

---

## 2. System Architecture

### 2.1 High-Level Architecture

> **🤖 AGENT INSTRUCTIONS:** Include a diagram showing major components and their relationships. Use standard notation (boxes for components, arrows for data flow). Text description should explain what the diagram shows.
```
┌──────────────────────────────────────────────────────┐
│               [ARCHITECTURE DIAGRAM]                 │
│                                                      │
│   ┌──────────┐      ┌──────────┐      ┌──────────┐   │
│   │  Client  │ ───► │   API    │ ───► │ Service  │   │
│   │          │      │ Gateway  │      │    A     │   │
│   └──────────┘      └──────────┘      └──────────┘   │
│                           │                 │        │
│                           ▼                 ▼        │
│                     ┌──────────┐      ┌──────────┐   │
│                     │  Auth    │      │ Database │   │
│                     │ Service  │      │          │   │
│                     └──────────┘      └──────────┘   │
│                                                      │
└──────────────────────────────────────────────────────┘
```

[Description of the architecture and how components interact]

### 2.2 Component Overview

> **🤖 AGENT INSTRUCTIONS:** List each major component with a brief description of its responsibility. Use the Single Responsibility Principle — each component should have one clear purpose.

| Component     | Responsibility | Technology   |
|---------------|----------------|--------------|
| [Component A] | [What it does] | [Tech stack] |
| [Component B] | [What it does] | [Tech stack] |
| [Component C] | [What it does] | [Tech stack] |

---

## 3. Detailed Design

### 3.1 [Component A Name]

> **🤖 AGENT INSTRUCTIONS:** For each major component, describe its internal design. Include responsibilities, interfaces, data it manages, and key algorithms or logic. Add diagrams where helpful.

#### 3.1.1 Responsibilities

- [Primary responsibility]
- [Secondary responsibility]

#### 3.1.2 Interface
```
[API signature, method signatures, or interface definition]
```

#### 3.1.3 Internal Design

[Description of how this component works internally]

#### 3.1.4 Dependencies

| Dependency        | Purpose           |
|-------------------|-------------------|
| [Service/Library] | [Why it's needed] |

---

### 3.2 [Component B Name]

[Repeat structure for each major component]

---

## 4. Data Design

### 4.1 Data Models

> **🤖 AGENT INSTRUCTIONS:** Define the core data entities. Include field names, types, constraints, and relationships. Use a format appropriate to your storage (SQL schema, document structure, etc.).

#### 4.1.1 [Entity Name]
```sql
-- Example SQL schema
CREATE TABLE entity_name (
    id          UUID PRIMARY KEY,
    name        VARCHAR(255) NOT NULL,
    created_at  TIMESTAMP DEFAULT NOW(),
    updated_at  TIMESTAMP,
    -- Add fields as needed
);
```

**Field Descriptions:**

| Field | Type   | Description       | Constraints                 |
|-------|--------|-------------------|-----------------------------|
| id    | UUID   | Unique identifier | Primary key, auto-generated |
| name  | String | [Description]     | Required, max 255 chars     |

**Relationships:**
- [Relationship to other entities]

#### 4.1.2 [Another Entity]

[Repeat structure]

### 4.2 Data Flow

> **🤖 AGENT INSTRUCTIONS:** Describe how data moves through the system. Use sequence diagrams for complex flows.
```
┌────────┐     ┌─────────┐     ┌─────────┐     ┌──────────┐
│ Client │     │   API   │     │ Service │     │ Database │
└───┬────┘     └────┬────┘     └────┬────┘     └────┬─────┘
    │               │               │               │
    │ 1. Request    │               │               │
    │──────────────►│               │               │
    │               │ 2. Validate   │               │
    │               │──────────────►│               │
    │               │               │ 3. Query      │
    │               │               │──────────────►│
    │               │               │               │
    │               │               │ 4. Response   │
    │               │               │◄──────────────│
    │               │ 5. Transform  │               │
    │               │◄──────────────│               │
    │ 6. Response   │               │               │
    │◄──────────────│               │               │
    │               │               │               │
```

[Description of the data flow]

### 4.3 Data Storage

> **🤖 AGENT INSTRUCTIONS:** Describe storage decisions — database choice, partitioning strategy, indexing, caching, etc. Reference ADRs for major decisions.

| Storage Type   | Technology         | Purpose   | ADR Reference |
|----------------|--------------------|-----------|---------------|
| Primary DB     | [e.g., PostgreSQL] | [Purpose] | [ADR-XXX]     |
| Cache          | [e.g., Redis]      | [Purpose] | [ADR-XXX]     |
| Object Storage | [e.g., S3]         | [Purpose] |               |

---

## 5. API Design

### 5.1 API Overview

> **🤖 AGENT INSTRUCTIONS:** Describe the API style (REST, GraphQL, gRPC) and key design decisions. Reference ADRs for style decisions.

[API philosophy and approach]

### 5.2 Endpoints

> **🤖 AGENT INSTRUCTIONS:** Document each endpoint with method, path, purpose, request/response formats, and error codes. Be thorough — this is the contract other systems depend on.

#### 5.2.1 [Endpoint Group Name]

##### `POST /api/v1/resource`

**Description:** [What this endpoint does]

**Authentication:** [Required/Optional, method]

**Request:**
```json
{
  "field1": "string (required) - Description",
  "field2": 123,
  "field3": {
    "nested": "value"
  }
}
```

**Response (200 OK):**
```json
{
  "id": "uuid",
  "field1": "string",
  "created_at": "2026-01-15T12:00:00Z"
}
```

**Error Responses:**

| Status | Code            | Description        |
|--------|-----------------|--------------------|
| 400    | INVALID_REQUEST | [When this occurs] |
| 401    | UNAUTHORIZED    | [When this occurs] |
| 404    | NOT_FOUND       | [When this occurs] |
| 500    | INTERNAL_ERROR  | [When this occurs] |

##### `GET /api/v1/resource/{id}`

[Repeat structure for each endpoint]

### 5.3 API Versioning

> **🤖 AGENT INSTRUCTIONS:** Describe how API versions are managed and the backward compatibility strategy.

[Versioning approach and compatibility policy]

---

## 6. Security Design

> **🤖 AGENT INSTRUCTIONS:** Security should be designed in, not bolted on. Address authentication, authorization, data protection, and threat mitigation. Reference ADRs for security decisions.

### 6.1 Authentication

[How users/services authenticate]

### 6.2 Authorization

[How permissions are managed and enforced]

| Role     | Permissions        |
|----------|--------------------|
| [Role A] | [What they can do] |
| [Role B] | [What they can do] |

### 6.3 Data Protection

| Data Type         | Protection Method                  |
|-------------------|------------------------------------|
| [e.g., Passwords] | [e.g., bcrypt hashing]             |
| [e.g., PII]       | [e.g., AES-256 encryption at rest] |
| [e.g., API keys]  | [e.g., Encrypted in vault]         |

### 6.4 Threat Mitigation

| Threat                | Mitigation                           |
|-----------------------|--------------------------------------|
| [e.g., SQL Injection] | [e.g., Parameterized queries]        |
| [e.g., XSS]           | [e.g., Output encoding, CSP headers] |
| [e.g., CSRF]          | [e.g., CSRF tokens]                  |
| [e.g., Rate limiting] | [e.g., 100 req/min per user]         |

---

## 7. Reliability & Performance

### 7.1 Performance Requirements

> **🤖 AGENT INSTRUCTIONS:** Define specific, measurable performance targets. These should tie back to PRD success metrics where applicable.

| Metric                  | Target               | Measurement Method |
|-------------------------|----------------------|--------------------|
| API Response Time (p50) | [e.g., < 50ms]       | [How measured]     |
| API Response Time (p99) | [e.g., < 200ms]      | [How measured]     |
| Throughput              | [e.g., 1000 req/sec] | [How measured]     |
| Error Rate              | [e.g., < 0.1%]       | [How measured]     |

### 7.2 Scalability

> **🤖 AGENT INSTRUCTIONS:** Describe how the system scales. Identify bottlenecks and how they're addressed.

[Horizontal vs. vertical scaling approach, auto-scaling policies]

**Scaling Limits:**

| Component   | Current Capacity | Scaling Strategy | Bottleneck        |
|-------------|------------------|------------------|-------------------|
| [Component] | [Capacity]       | [How it scales]  | [Limiting factor] |

### 7.3 Availability & Fault Tolerance

[Uptime target, redundancy approach, failover strategy]

| Failure Scenario         | Impact   | Mitigation                               |
|--------------------------|----------|------------------------------------------|
| [e.g., Database failure] | [Impact] | [e.g., Automatic failover to replica]    |
| [e.g., Service crash]    | [Impact] | [e.g., Auto-restart, multiple instances] |

### 7.4 Disaster Recovery

| Metric                         | Target                           |
|--------------------------------|----------------------------------|
| Recovery Time Objective (RTO)  | [e.g., < 1 hour]                 |
| Recovery Point Objective (RPO) | [e.g., < 5 minutes of data loss] |

[Backup strategy, recovery procedures]

---

## 8. Observability

### 8.1 Logging

> **🤖 AGENT INSTRUCTIONS:** Define what gets logged, at what level, and how logs are structured.

| Log Level | What's Logged          | Retention                |
|-----------|------------------------|--------------------------|
| ERROR     | [Exceptions, failures] | [e.g., 90 days]          |
| WARN      | [Degraded conditions]  | [e.g., 30 days]          |
| INFO      | [Key operations]       | [e.g., 14 days]          |
| DEBUG     | [Detailed flow]        | [e.g., 3 days, dev only] |

**Log Format:**
```json
{
  "timestamp": "ISO-8601",
  "level": "INFO",
  "service": "service-name",
  "trace_id": "uuid",
  "message": "description",
  "context": {}
}
```

### 8.2 Metrics

| Metric                  | Type      | Description   | Alert Threshold |
|-------------------------|-----------|---------------|-----------------|
| [e.g., request_count]   | Counter   | [Description] | N/A             |
| [e.g., request_latency] | Histogram | [Description] | p99 > 500ms     |
| [e.g., error_rate]      | Gauge     | [Description] | > 1%            |

### 8.3 Tracing

[Distributed tracing approach, trace propagation]

### 8.4 Alerting

| Alert        | Condition       | Severity           | Response            |
|--------------|-----------------|--------------------|---------------------|
| [Alert name] | [When it fires] | [Critical/Warning] | [Expected response] |

---

## 9. Testing Strategy

> **🤖 AGENT INSTRUCTIONS:** Define how the system will be tested at each level. Testing should be proportional to risk.

### 9.1 Test Pyramid

| Level       | Scope                  | Coverage Target        | Tools                       |
|-------------|------------------------|------------------------|-----------------------------|
| Unit        | Individual functions   | [e.g., 80%]            | [e.g., Jest, pytest]        |
| Integration | Component interactions | [e.g., Key paths]      | [e.g., Testcontainers]      |
| E2E         | Full user flows        | [e.g., Critical paths] | [e.g., Playwright, Cypress] |

### 9.2 Test Scenarios

| Scenario                     | Type        | Priority |
|------------------------------|-------------|----------|
| [Key user flow A]            | E2E         | High     |
| [Integration with Service X] | Integration | High     |
| [Edge case Y]                | Unit        | Medium   |

### 9.3 Performance Testing

[Load testing approach, tools, success criteria]

---

## 10. Deployment & Operations

### 10.1 Deployment Architecture

> **🤖 AGENT INSTRUCTIONS:** Describe where and how the system runs. Include environment details.
```
┌─────────────────────────────────────────────────────────────────┐
│                    [DEPLOYMENT DIAGRAM]                          │
│                                                                  │
│   ┌─────────────────────────────────────────────────────┐       │
│   │                    Production                        │       │
│   │  ┌─────────┐  ┌─────────┐  ┌─────────┐             │       │
│   │  │ Service │  │ Service │  │ Service │             │       │
│   │  │  (x3)   │  │  (x3)   │  │  (x2)   │             │       │
│   │  └─────────┘  └─────────┘  └─────────┘             │       │
│   │       │            │            │                   │       │
│   │       └────────────┼────────────┘                   │       │
│   │                    ▼                                │       │
│   │              ┌──────────┐                           │       │
│   │              │ Database │                           │       │
│   │              │  (HA)    │                           │       │
│   │              └──────────┘                           │       │
│   └─────────────────────────────────────────────────────┘       │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 10.2 Environments

| Environment | Purpose                | Infrastructure                  |
|-------------|------------------------|---------------------------------|
| Development | Local development      | [e.g., Docker Compose]          |
| Staging     | Pre-production testing | [e.g., Kubernetes, scaled down] |
| Production  | Live traffic           | [e.g., Kubernetes, full scale]  |

### 10.3 CI/CD Pipeline
```
┌────────┐    ┌──────┐    ┌──────┐    ┌─────────┐    ┌────────┐
│ Commit │ ─► │ Test │ ─► │ Build│ ─► │ Deploy  │ ─► │ Verify │
│        │    │      │    │      │    │ Staging │    │        │
└────────┘    └──────┘    └──────┘    └─────────┘    └────────┘
                                            │
                                            ▼ (manual approval)
                                      ┌─────────┐
                                      │ Deploy  │
                                      │  Prod   │
                                      └─────────┘
```

### 10.4 Rollback Strategy

[How to roll back if deployment fails]

### 10.5 Feature Flags

| Flag        | Purpose            | Default  |
|-------------|--------------------|----------|
| [Flag name] | [What it controls] | [On/Off] |

---

## 11. Migration Plan

> **🤖 AGENT INSTRUCTIONS:** If this system replaces or modifies existing systems, describe the migration path. Include backward compatibility period, data migration, and rollback plan.

### 11.1 Migration Strategy

[Approach: Big bang, gradual rollout, parallel run, etc.]

### 11.2 Migration Steps

| Step | Description        | Duration | Rollback      |
|------|--------------------|----------|---------------|
| 1    | [Step description] | [Time]   | [How to undo] |
| 2    | [Step description] | [Time]   | [How to undo] |

### 11.3 Data Migration

[How existing data will be migrated, validation approach]

### 11.4 Backward Compatibility

[Compatibility guarantees during migration period]

---

## 12. Open Questions & Risks

### 12.1 Open Questions

> **🤖 AGENT INSTRUCTIONS:** Track unresolved technical questions. Assign owners and deadlines for resolution.

| Question             | Owner  | Target Date | Status          |
|----------------------|--------|-------------|-----------------|
| [Technical question] | [Name] | [Date]      | [Open/Resolved] |

### 12.2 Technical Risks

| Risk               | Likelihood | Impact  | Mitigation            |
|--------------------|------------|---------|-----------------------|
| [Risk description] | [H/M/L]    | [H/M/L] | [Mitigation strategy] |

### 12.3 Dependencies

| Dependency            | Owner  | Status   | Impact if Delayed |
|-----------------------|--------|----------|-------------------|
| [External dependency] | [Team] | [Status] | [Impact]          |

---

## 13. Timeline & Milestones

> **🤖 AGENT INSTRUCTIONS:** Provide rough estimates. Detailed project planning belongs in project management tools. Focus on major milestones.

| Milestone              | Target Date | Description                |
|------------------------|-------------|----------------------------|
| Design Review Complete | [Date]      | Tech spec approved         |
| MVP Complete           | [Date]      | Core functionality working |
| Beta Release           | [Date]      | Ready for limited users    |
| GA Release             | [Date]      | Production ready           |

---

## 14. Appendix

### 14.1 Glossary

| Term   | Definition   |
|--------|--------------|
| [Term] | [Definition] |

### 14.2 References

| Document          | Link    |
|-------------------|---------|
| PRD               | [Link]  |
| Related RFC(s)    | [Links] |
| Related ADR(s)    | [Links] |
| [Other reference] | [Link]  |

### 14.3 Change Log

[Track significant changes to this document]

---

## Approval

| Role                | Name | Date | Approval   |
|---------------------|------|------|------------|
| Tech Lead           |      |      | ☐ Approved |
| Engineering Manager |      |      | ☐ Approved |
| Security Review     |      |      | ☐ Approved |
| Product Owner       |      |      | ☐ Approved |

---

> **🤖 AGENT META-INSTRUCTIONS:**
>
> When helping complete this Technical Design Doc:
> 1. **Reference the PRD** — Every design choice should trace back to a product requirement
> 2. **Be specific with numbers** — Avoid "fast" or "scalable"; use measurable targets
> 3. **Include diagrams** — Visual representations clarify complex interactions
> 4. **Address failure modes** — Every component can fail; document how
> 5. **Consider operations** — Design for the full lifecycle: deploy, monitor, debug, update
> 6. **Flag ADR candidates** — When you make significant decisions, suggest creating an ADR
> 7. **Keep it maintainable** — This doc should be updated as implementation progresses
> 8. **Balance detail** — Enough to implement, not so much it becomes stale
