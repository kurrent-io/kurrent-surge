# Feature Spec: [Feature Name]

---

> **🤖 AGENT INSTRUCTIONS:** This template is MODULAR. Not every section is needed for every feature. Include sections that add value; skip sections that would just add noise. Guidance for when to include/skip each section is provided below.
>
> **Key principles:**
> - Sequence diagrams are almost always valuable — include multiple if needed
> - API contracts should be actual specs (protobuf, OpenAPI) — embed or link them
> - Security/Performance sections only when this feature has UNIQUE needs beyond system defaults
> - Reference the Architecture Doc for system-wide context — don't repeat it here

---

## Document Control

| Field                | Value                                          |
|----------------------|------------------------------------------------|
| **Feature**          | [Feature Name]                                 |
| **Author(s)**        | [Names]                                        |
| **Status**           | [Draft / In Review / Approved / Implemented]   |
| **Last Updated**     | [Date]                                         |
| **Architecture Doc** | [Link]                                         |
| **PRD Reference**    | [Link to relevant PRD section]                 |
| **Related Specs**    | [Links to specs this depends on or relates to] |

---

## 1. Overview

> **🤖 AGENT INSTRUCTIONS:** Always include. 2-3 paragraphs covering: what this feature does, why it's needed (link to PRD), and how it fits into the broader system (link to Architecture Doc).

### 1.1 Summary

[What this feature does and why it matters]

### 1.2 Goals

| Goal     | Metric         | Target         |
|----------|----------------|----------------|
| [Goal 1] | [How measured] | [Target value] |
| [Goal 2] | [How measured] | [Target value] |

### 1.3 Non-Goals

> **🤖 AGENT INSTRUCTIONS:** Include if there are likely misconceptions about scope. Skip if scope is obvious.

- [What this feature explicitly does NOT do]

### 1.4 User Stories

> **🤖 AGENT INSTRUCTIONS:** Include the key user stories from the PRD that this feature addresses. Link back to PRD for full list.

| ID   | User Story                                        | Acceptance Criteria |
|------|---------------------------------------------------|---------------------|
| US-1 | As a [user], I want to [action] so that [benefit] | [Criteria]          |
| US-2 | ...                                               | ...                 |

---

## 2. Component Overview

> **🤖 AGENT INSTRUCTIONS:** Include when the feature has multiple internal components or services. Skip for simple features with a single component.

### 2.1 Component Diagram
```
┌─────────────────────────────────────────────────────────────────┐
│                    [FEATURE NAME]                                │
│                                                                  │
│   ┌──────────────┐      ┌──────────────┐      ┌──────────────┐ │
│   │              │      │              │      │              │ │
│   │ Component A  │ ───► │ Component B  │ ───► │ Component C  │ │
│   │              │      │              │      │              │ │
│   └──────────────┘      └──────────────┘      └──────────────┘ │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2 Component Responsibilities

| Component     | Responsibility |
|---------------|----------------|
| [Component A] | [What it does] |
| [Component B] | [What it does] |
| [Component C] | [What it does] |

---

## 3. Detailed Design

> **🤖 AGENT INSTRUCTIONS:** Always include. This is the core of the spec. Describe how the feature works. Use sub-sections for each major component if the feature is complex.

### 3.1 [Component/Aspect A]

[Detailed description of how this part works]

### 3.2 [Component/Aspect B]

[Detailed description]

---

## 4. Sequence Diagrams

> **🤖 AGENT INSTRUCTIONS:** Almost always include. Multiple diagrams are normal and encouraged. Cover: happy path, key alternative flows, error scenarios. Sequence diagrams are often the most valuable part of a spec.

### 4.1 [Flow Name]: Happy Path
```
┌────────┐     ┌─────────┐     ┌─────────┐     ┌──────────┐
│ Client │     │ Service │     │ Service │     │ Database │
│        │     │    A    │     │    B    │     │          │
└───┬────┘     └────┬────┘     └────┬────┘     └────┬─────┘
    │               │               │               │
    │ 1. Request    │               │               │
    │──────────────►│               │               │
    │               │               │               │
    │               │ 2. Validate   │               │
    │               │──────────────►│               │
    │               │               │               │
    │               │ 3. Response   │               │
    │               │◄──────────────│               │
    │               │               │               │
    │               │ 4. Persist    │               │
    │               │───────────────┼──────────────►│
    │               │               │               │
    │               │ 5. Confirm    │               │
    │               │◄──────────────┼───────────────│
    │               │               │               │
    │ 6. Response   │               │               │
    │◄──────────────│               │               │
    │               │               │               │
```

**Description:** [Brief explanation of this flow]

### 4.2 [Flow Name]: [Alternative/Error Scenario]
```
[Another sequence diagram]
```

**Description:** [Explanation]

### 4.3 [Additional Flow]

> **🤖 AGENT INSTRUCTIONS:** Add as many sequence diagrams as needed to fully explain the feature's behavior. Common scenarios to cover:
> - Happy path (always)
> - Validation failure
> - Downstream service failure
> - Concurrent access / race conditions
> - Timeout handling

---

## 5. API Contracts

> **🤖 AGENT INSTRUCTIONS:** Include when this feature exposes APIs. Embed actual contract definitions (protobuf, OpenAPI, GraphQL schema) or link to files in the repo. Avoid hand-written API descriptions that will get out of sync.

### 5.1 API Overview

| Endpoint/Method      | Purpose        |
|----------------------|----------------|
| [POST /resource]     | [What it does] |
| [GET /resource/{id}] | [What it does] |
| [rpc CreateThing]    | [What it does] |

### 5.2 Contract Definitions

> **🤖 AGENT INSTRUCTIONS:** Choose the appropriate format for your API style.

#### Option A: Protocol Buffers
```protobuf
syntax = "proto3";

package feature.v1;

service FeatureService {
  rpc CreateThing(CreateThingRequest) returns (CreateThingResponse);
  rpc GetThing(GetThingRequest) returns (GetThingResponse);
}

message CreateThingRequest {
  string name = 1;
  string description = 2;
  map<string, string> metadata = 3;
}

message CreateThingResponse {
  Thing thing = 1;
}

message Thing {
  string id = 1;
  string name = 2;
  string description = 3;
  google.protobuf.Timestamp created_at = 4;
}
```

#### Option B: OpenAPI (REST)
```yaml
openapi: 3.0.0
info:
  title: Feature API
  version: 1.0.0

paths:
  /api/v1/things:
    post:
      summary: Create a new thing
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CreateThingRequest'
      responses:
        '201':
          description: Created
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Thing'

components:
  schemas:
    CreateThingRequest:
      type: object
      required:
        - name
      properties:
        name:
          type: string
        description:
          type: string

    Thing:
      type: object
      properties:
        id:
          type: string
          format: uuid
        name:
          type: string
        created_at:
          type: string
          format: date-time
```

#### Option C: Link to Contract Files

| Contract        | Location                          | Format      |
|-----------------|-----------------------------------|-------------|
| Feature Service | [/proto/feature/v1/feature.proto] | Protobuf    |
| REST API        | [/api/openapi/feature.yaml]       | OpenAPI 3.0 |

### 5.3 Error Responses

| Error Code               | Condition          | Response       |
|--------------------------|--------------------|----------------|
| [400 / INVALID_ARGUMENT] | [When this occurs] | [Error detail] |
| [404 / NOT_FOUND]        | [When this occurs] | [Error detail] |
| [409 / ALREADY_EXISTS]   | [When this occurs] | [Error detail] |

---

## 6. Data Models

> **🤖 AGENT INSTRUCTIONS:** Include when this feature owns/manages data. Show schemas, relationships, and important indexes. Skip if the feature doesn't persist data or only uses data owned by other features.

### 6.1 Entity Relationship Diagram
```
┌──────────────┐       ┌──────────────┐       ┌──────────────┐
│    Thing     │       │  ThingPart   │       │   Category   │
├──────────────┤       ├──────────────┤       ├──────────────┤
│ id (PK)      │──┐    │ id (PK)      │    ┌──│ id (PK)      │
│ name         │  │    │ thing_id(FK) │────┘  │ name         │
│ category_id  │──┼───►│ name         │       │ description  │
│ created_at   │  │    │ position     │       └──────────────┘
│ updated_at   │  │    │ created_at   │
└──────────────┘  │    └──────────────┘
                  │           ▲
                  └───────────┘
```

### 6.2 Schema Definitions

#### Thing
```sql
CREATE TABLE things (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name            VARCHAR(255) NOT NULL,
    description     TEXT,
    category_id     UUID REFERENCES categories(id),
    status          VARCHAR(50) NOT NULL DEFAULT 'draft',
    metadata        JSONB DEFAULT '{}',
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ,
    created_by      UUID NOT NULL REFERENCES users(id),
    
    CONSTRAINT valid_status CHECK (status IN ('draft', 'active', 'archived'))
);

-- Indexes
CREATE INDEX idx_things_category ON things(category_id);
CREATE INDEX idx_things_status ON things(status);
CREATE INDEX idx_things_created_by ON things(created_by);
CREATE INDEX idx_things_metadata ON things USING GIN(metadata);
```

**Field Descriptions:**

| Field       | Description         | Constraints                   |
|-------------|---------------------|-------------------------------|
| id          | Unique identifier   | Auto-generated UUID           |
| name        | Display name        | Required, max 255 chars       |
| category_id | Parent category     | Optional, FK to categories    |
| status      | Lifecycle state     | Enum: draft, active, archived |
| metadata    | Flexible attributes | JSON object                   |

#### ThingPart
```sql
CREATE TABLE thing_parts (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    thing_id        UUID NOT NULL REFERENCES things(id) ON DELETE CASCADE,
    name            VARCHAR(255) NOT NULL,
    position        INTEGER NOT NULL DEFAULT 0,
    config          JSONB NOT NULL DEFAULT '{}',
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_thing_parts_thing ON thing_parts(thing_id);
CREATE UNIQUE INDEX idx_thing_parts_position ON thing_parts(thing_id, position);
```

### 6.3 Data Access Patterns

> **🤖 AGENT INSTRUCTIONS:** Document how data will be queried. This informs index design and caching strategy.

| Query Pattern           | Frequency | Index Support             |
|-------------------------|-----------|---------------------------|
| Get thing by ID         | Very High | PK                        |
| List things by category | High      | idx_things_category       |
| List things by status   | Medium    | idx_things_status         |
| Search metadata         | Low       | idx_things_metadata (GIN) |

---

## 7. Dependencies

> **🤖 AGENT INSTRUCTIONS:** Include when this feature depends on other features/services or external systems. Skip if the feature is self-contained.

### 7.1 Internal Dependencies

| Dependency             | Type    | Purpose                             | Spec Reference      |
|------------------------|---------|-------------------------------------|---------------------|
| [Auth Service]         | Service | User authentication & authorization | [Link to Auth Spec] |
| [Notification Service] | Service | Send user notifications             | [Link to Spec]      |
| [Shared Library X]     | Library | Common utilities                    | [Link to repo]      |

### 7.2 External Dependencies

| Dependency        | Type     | Purpose   | Fallback Strategy             |
|-------------------|----------|-----------|-------------------------------|
| [External API A]  | REST API | [Purpose] | [What happens if unavailable] |
| [Cloud Service B] | SaaS     | [Purpose] | [Fallback]                    |

---

## 8. Security Considerations

> **🤖 AGENT INSTRUCTIONS:** Include ONLY when this feature has security needs BEYOND system defaults documented in the Auth/Security Platform Spec. Skip if standard auth/authz applies without modification. Examples of when to include: feature handles sensitive data, has unique permission model, or exposes public endpoints.

### 8.1 Authentication

[How users authenticate for this feature, if different from system default]

### 8.2 Authorization

| Action       | Required Permission       | Notes                             |
|--------------|---------------------------|-----------------------------------|
| Create thing | `things:create`           | [Any special rules]               |
| Read thing   | `things:read` OR owner    | Users can always read their own   |
| Delete thing | `things:delete` AND owner | Must be owner AND have permission |

### 8.3 Data Sensitivity

| Data Element   | Sensitivity               | Protection                  |
|----------------|---------------------------|-----------------------------|
| [User input X] | [PII/Confidential/Public] | [Encryption, masking, etc.] |

### 8.4 Security Risks & Mitigations

| Risk                                             | Mitigation                                      |
|--------------------------------------------------|-------------------------------------------------|
| [e.g., Injection via user input]                 | [e.g., Parameterized queries, input validation] |
| [e.g., Unauthorized access to other users' data] | [e.g., Row-level security, ownership checks]    |

---

## 9. Performance Considerations

> **🤖 AGENT INSTRUCTIONS:** Include ONLY when this feature has specific performance requirements beyond system defaults, handles high-volume operations, or has known performance risks. Skip for features with typical load characteristics.

### 9.1 Performance Requirements

| Metric                        | Target                  | Rationale                 |
|-------------------------------|-------------------------|---------------------------|
| [e.g., API latency p99]       | [< 200ms]               | [User-facing, impacts UX] |
| [e.g., Throughput]            | [1000 req/sec]          | [Expected peak load]      |
| [e.g., Batch processing time] | [< 5 min for 10k items] | [SLA requirement]         |

### 9.2 Scalability Approach

[How this feature scales — caching strategy, partitioning, async processing, etc.]

### 9.3 Performance Risks

| Risk                               | Likelihood | Mitigation                          |
|------------------------------------|------------|-------------------------------------|
| [e.g., N+1 query on list endpoint] | Medium     | [Eager loading, query optimization] |
| [e.g., Large payload processing]   | Low        | [Streaming, pagination]             |

---

## 10. Error Handling

> **🤖 AGENT INSTRUCTIONS:** Include when the feature has complex error scenarios, integrates with external systems that can fail, or has specific recovery requirements. Can be brief or skipped for simple CRUD features.

### 10.1 Error Categories

| Error Type         | Example                | Handling Strategy                           |
|--------------------|------------------------|---------------------------------------------|
| Validation Error   | Invalid input          | Return 400 with details, no retry           |
| Not Found          | Resource doesn't exist | Return 404, client should not retry         |
| Conflict           | Duplicate creation     | Return 409, client may retry with changes   |
| Downstream Failure | Service B unavailable  | Retry with backoff, return 503 if exhausted |
| Internal Error     | Unexpected exception   | Log, alert, return 500                      |

### 10.2 Retry & Recovery

| Scenario                      | Retry Strategy                   | Circuit Breaker              |
|-------------------------------|----------------------------------|------------------------------|
| [Database connection failure] | [3 retries, exponential backoff] | [Open after 5 failures]      |
| [External API timeout]        | [2 retries, 1s/2s delays]        | [Open after 10 failures/min] |

### 10.3 Failure Modes

| Failure                | Impact                | Detection            | Recovery                        |
|------------------------|-----------------------|----------------------|---------------------------------|
| [Component A crashes]  | [Feature degraded]    | [Health check fails] | [Auto-restart, traffic reroute] |
| [Database unavailable] | [Feature unavailable] | [Connection timeout] | [Failover to replica]           |

---

## 11. Testing Strategy

> **🤖 AGENT INSTRUCTIONS:** Include when the feature requires specific testing approaches, has complex scenarios, or has high reliability requirements. Can be brief (just key test cases) or skipped for trivial features where standard testing applies.

### 11.1 Test Coverage Goals

| Test Type   | Coverage Target  | Focus Areas                          |
|-------------|------------------|--------------------------------------|
| Unit        | [80%]            | [Business logic, validation]         |
| Integration | [Key paths]      | [Database operations, service calls] |
| E2E         | [Critical flows] | [User journeys from PRD]             |

### 11.2 Key Test Scenarios

| Scenario                                | Type        | Priority | Notes                |
|-----------------------------------------|-------------|----------|----------------------|
| [Happy path: create and retrieve thing] | E2E         | High     | [Core functionality] |
| [Validation rejects invalid input]      | Unit        | High     | [Security critical]  |
| [Handles downstream service timeout]    | Integration | Medium   | [Resilience]         |
| [Concurrent updates don't corrupt data] | Integration | Medium   | [Data integrity]     |
| [Pagination works with large datasets]  | Integration | Low      | [Performance]        |

### 11.3 Test Data Requirements

| Data Need                        | Approach               |
|----------------------------------|------------------------|
| [Seed data for testing]          | [Fixtures / Factories] |
| [Large dataset for perf testing] | [Generated via script] |
| [External service responses]     | [Mocked / Recorded]    |

---

## 12. Observability

> **🤖 AGENT INSTRUCTIONS:** Include when the feature needs specific monitoring beyond system defaults. Can be brief — just list key metrics and log events. Skip if standard observability from Platform Spec applies without additions.

### 12.1 Key Metrics

| Metric                              | Type      | Description         | Alert Threshold |
|-------------------------------------|-----------|---------------------|-----------------|
| `feature_things_created_total`      | Counter   | Things created      | N/A             |
| `feature_things_operation_duration` | Histogram | Operation latency   | p99 > 500ms     |
| `feature_things_errors_total`       | Counter   | Error count by type | > 10/min        |

### 12.2 Key Log Events

| Event                   | Level | When                       | Key Fields            |
|-------------------------|-------|----------------------------|-----------------------|
| `thing.created`         | INFO  | Thing successfully created | thing_id, user_id     |
| `thing.creation_failed` | ERROR | Creation failed            | error, user_id, input |
| `thing.accessed`        | DEBUG | Thing retrieved            | thing_id, user_id     |

### 12.3 Dashboards & Alerts

| Dashboard/Alert            | Purpose                                   |
|----------------------------|-------------------------------------------|
| [Feature Health Dashboard] | [Overview of feature metrics]             |
| [Error Rate Alert]         | [Fires when error rate exceeds threshold] |

---

## 13. Rollout Plan

> **🤖 AGENT INSTRUCTIONS:** Include for significant features that need gradual rollout, feature flags, or migration. Skip for simple features or when standard deployment applies.

### 13.1 Feature Flags

| Flag                           | Purpose               | Default | Cleanup Target  |
|--------------------------------|-----------------------|---------|-----------------|
| `feature_things_enabled`       | Enable entire feature | false   | [Date after GA] |
| `feature_things_new_algorithm` | Test new processing   | false   | [Date]          |

### 13.2 Rollout Phases

| Phase       | Audience     | Success Criteria     | Rollback Trigger       |
|-------------|--------------|----------------------|------------------------|
| 1. Internal | Team only    | No errors            | Any error              |
| 2. Beta     | 10% of users | Error rate < 0.1%    | Error rate > 1%        |
| 3. GA       | All users    | Metrics meet targets | Significant regression |

### 13.3 Migration

> **🤖 AGENT INSTRUCTIONS:** Include only if this feature replaces existing functionality or requires data migration.

| Migration Step | Description   | Reversible |
|----------------|---------------|------------|
| [Step 1]       | [Description] | [Yes/No]   |
| [Step 2]       | [Description] | [Yes/No]   |

---

## 14. Open Questions

> **🤖 AGENT INSTRUCTIONS:** Always include, even if empty. Track unresolved decisions and questions that need answers.

| Question               | Owner  | Due Date | Status          | Resolution           |
|------------------------|--------|----------|-----------------|----------------------|
| [Technical question 1] | [Name] | [Date]   | [Open/Resolved] | [Answer if resolved] |
| [Design decision 2]    | [Name] | [Date]   | [Open]          |                      |

---

## 15. Related ADRs

> **🤖 AGENT INSTRUCTIONS:** Link to ADRs that document decisions made in this spec. Add ADRs as you make significant decisions.

| ADR       | Decision                     | Date   |
|-----------|------------------------------|--------|
| [ADR-XXX] | [Decision made in this spec] | [Date] |

---

## 16. References

| Document              | Link    |
|-----------------------|---------|
| PRD                   | [Link]  |
| Architecture Doc      | [Link]  |
| Related Feature Specs | [Links] |
| Design Mockups        | [Link]  |
| [Other relevant docs] | [Link]  |

---

## Appendix

> **🤖 AGENT INSTRUCTIONS:** Use for supplementary material that supports the spec but would clutter the main document. Examples: detailed calculations, research findings, meeting notes, extensive examples.

### A. [Appendix Topic]

[Supplementary content]

---

> **🤖 AGENT META-INSTRUCTIONS:**
>
> **Section Inclusion Guide:**
>
> | Section            | When to Include                                | When to Skip                                    |
> |--------------------|------------------------------------------------|-------------------------------------------------|
> | Overview           | Always                                         | Never                                           |
> | Component Overview | Feature has 2+ internal components             | Simple, single-component feature                |
> | Detailed Design    | Always                                         | Never                                           |
> | Sequence Diagrams  | Almost always (aim for 2-5)                    | Extremely simple CRUD with no interesting flows |
> | API Contracts      | Feature exposes APIs                           | Feature has no API surface                      |
> | Data Models        | Feature owns/persists data                     | Feature only reads data owned elsewhere         |
> | Dependencies       | Feature depends on other services              | Self-contained feature                          |
> | Security           | Unique security needs beyond defaults          | Standard auth/authz applies                     |
> | Performance        | Specific perf requirements or risks            | Typical load, no special needs                  |
> | Error Handling     | Complex error scenarios, external integrations | Simple CRUD, obvious error handling             |
> | Testing Strategy   | Complex feature, high reliability needs        | Simple feature, standard testing                |
> | Observability      | Specific metrics/logs beyond defaults          | Standard observability applies                  |
> | Rollout Plan       | Gradual rollout, feature flags, migration      | Simple deploy, no migration                     |
> | Open Questions     | Always                                         | Never                                           |
> | Related ADRs       | Always (even if empty initially)               | Never                                           |
>
> **General Guidance:**
> 1. **Sequence diagrams are gold** — When in doubt, add another diagram
> 2. **Use real contracts** — Embed or link actual protobuf/OpenAPI files
> 3. **Reference, don't repeat** — Link to Architecture Doc and Platform Specs for shared concerns
> 4. **Right-size the spec** — A 2-page spec is fine for simple features; 10+ pages is fine for complex ones
> 5. **Update as you build** — Specs are living documents during implementation
> 6. **Create ADRs as you go** — When you make a significant decision, document it in an ADR
