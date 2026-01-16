# Product Requirements Document (PRD)
## [Product Name]

---

> **🤖 AGENT INSTRUCTIONS:** This PRD template includes guidance for AI agents in blockquotes like this one. Human authors should replace all `[bracketed placeholders]` with actual content. Agents should use these instructions to help draft, review, or refine each section.

---

## Document Control

| Field            | Value                          |
|------------------|--------------------------------|
| **Product Name** | [Product Name]                 |
| **Version**      | [e.g., 1.0]                    |
| **Author**       | [Name, Role]                   |
| **Last Updated** | [Date]                         |
| **Status**       | [Draft / In Review / Approved] |

### Version History

| Version | Date   | Author | Changes       |
|---------|--------|--------|---------------|
| 0.1     | [Date] | [Name] | Initial draft |

---

## 1. Executive Summary

> **🤖 AGENT INSTRUCTIONS:** Write a concise 2-4 paragraph summary that a busy executive could read in 60 seconds. Cover: what the product is, the problem it solves, who it's for, and why it matters now. Avoid technical jargon.

[Write executive summary here]

---

## 2. Problem Statement

### 2.1 Problem Description

> **🤖 AGENT INSTRUCTIONS:** Describe the problem in concrete, measurable terms. Include data points where possible (e.g., "Teams spend an average of X hours per week on Y"). Avoid vague statements like "users struggle with" — specify HOW they struggle and what the impact is.

[Detailed problem description]

### 2.2 Current State & Workarounds

> **🤖 AGENT INSTRUCTIONS:** Document how users currently solve this problem (even if poorly). This establishes the baseline you're improving upon and helps validate that the problem is real.

[How users currently handle this problem]

### 2.3 Market Opportunity

> **🤖 AGENT INSTRUCTIONS:** Explain why this problem is worth solving NOW. Consider market timing, competitive landscape, technological enablers, or business strategic alignment.

[Why solve this now? What's the opportunity?]

---

## 3. Goals & Objectives

### 3.1 Business Goals

> **🤖 AGENT INSTRUCTIONS:** List 2-4 measurable business outcomes this product should drive. Use the format: "[Metric] from [baseline] to [target] by [timeframe]". Examples: revenue, market share, customer acquisition, retention.

| Goal                             | Current Baseline | Target    | Timeframe |
|----------------------------------|------------------|-----------|-----------|
| [e.g., New customer acquisition] | [X/month]        | [Y/month] | [Q2 2026] |

### 3.2 Product Goals

> **🤖 AGENT INSTRUCTIONS:** List 2-4 measurable product outcomes. Focus on user behavior metrics: adoption, activation, engagement, retention, task success rate.

| Goal                    | Metric                    | Target | Timeframe             |
|-------------------------|---------------------------|--------|-----------------------|
| [e.g., User activation] | [% completing onboarding] | [80%]  | [30 days post-launch] |

### 3.3 Non-Goals

> **🤖 AGENT INSTRUCTIONS:** Explicitly state what this product is NOT trying to achieve. This prevents scope creep and aligns stakeholders. Be specific — "We are not building X because Y."

- [Non-goal 1 and rationale]
- [Non-goal 2 and rationale]

---

## 4. Target Users & Personas

> **🤖 AGENT INSTRUCTIONS:** Create 2-4 detailed personas. Each should feel like a real person. Include specific details about their work context, tools they use, and emotional drivers. Avoid generic descriptions.

### 4.1 Primary Persona: [Name]

| Attribute                 | Details                            |
|---------------------------|------------------------------------|
| **Role**                  | [Job title, team]                  |
| **Company Type**          | [Size, industry]                   |
| **Experience Level**      | [Junior/Mid/Senior, years in role] |
| **Technical Proficiency** | [Low/Medium/High with specifics]   |

**Goals & Motivations:**
- [What they're trying to achieve]
- [What success looks like for them]

**Pain Points & Frustrations:**
- [Specific frustration 1]
- [Specific frustration 2]

**Current Tools & Workflow:**
- [Tools they use today]
- [How they currently accomplish this task]

**Quote:** *"[A representative quote that captures their perspective]"*

### 4.2 Secondary Persona: [Name]

[Repeat structure above]

### 4.3 Anti-Personas (Who This Is NOT For)

> **🤖 AGENT INSTRUCTIONS:** Define who should NOT use this product. This helps focus design decisions and prevents feature creep from edge-case users.

- **[Anti-persona type]:** [Why this product isn't for them]

---

## 5. User Scenarios & Use Cases

> **🤖 AGENT INSTRUCTIONS:** Write scenarios as narratives that show how a persona accomplishes a goal using the product. Use cases should be structured and testable. Cover the happy path first, then edge cases.

### 5.1 User Scenarios

#### Scenario 1: [Scenario Name]

**Persona:** [Which persona]  
**Context:** [Where/when this happens]  
**Narrative:**

> [Write a 1-2 paragraph story describing how the user discovers, engages with, and successfully uses the product to accomplish their goal. Include emotional beats — their initial frustration, the "aha" moment, and satisfaction at the end.]

### 5.2 Use Cases

#### Use Case 1: [Use Case Name]

| Field               | Description                                     |
|---------------------|-------------------------------------------------|
| **ID**              | UC-001                                          |
| **Actor**           | [Primary user type]                             |
| **Preconditions**   | [What must be true before this use case begins] |
| **Trigger**         | [What initiates this use case]                  |
| **Main Flow**       | 1. [Step 1]<br>2. [Step 2]<br>3. [Step 3]       |
| **Alternate Flows** | [Variations from the main flow]                 |
| **Exception Flows** | [Error conditions and how they're handled]      |
| **Postconditions**  | [What is true after successful completion]      |

---

## 6. Requirements

### 6.1 Functional Requirements

> **🤖 AGENT INSTRUCTIONS:** Organize features by logical groupings (modules, areas, or user journey stages). Each feature should have clear user stories with testable acceptance criteria. Use MoSCoW prioritization consistently.

#### 6.1.1 [Feature Area 1]

##### Feature: [Feature Name]

| Field                   | Description                                                                            |
|-------------------------|----------------------------------------------------------------------------------------|
| **ID**                  | FR-001                                                                                 |
| **Priority**            | [Must Have / Should Have / Could Have / Won't Have]                                    |
| **User Story**          | As a [persona], I want to [action] so that [benefit]                                   |
| **Description**         | [Detailed description of the feature]                                                  |
| **Acceptance Criteria** | - [ ] [Criterion 1 — specific, testable]<br>- [ ] [Criterion 2]<br>- [ ] [Criterion 3] |
| **Dependencies**        | [Other features or systems this depends on]                                            |

##### Feature: [Feature Name 2]

[Repeat structure]

#### 6.1.2 [Feature Area 2]

[Repeat structure for each feature area]

### 6.2 Non-Functional Requirements

> **🤖 AGENT INSTRUCTIONS:** Non-functional requirements define HOW the system should behave, not WHAT it does. Be specific with numbers — avoid "fast" or "secure" without measurable targets.

#### 6.2.1 Performance

| Requirement               | Target              | Measurement Method        |
|---------------------------|---------------------|---------------------------|
| [e.g., API response time] | [< 200ms p95]       | [How this will be tested] |
| [e.g., Throughput]        | [X requests/second] | [How this will be tested] |

#### 6.2.2 Reliability & Availability

| Requirement                      | Target                     |
|----------------------------------|----------------------------|
| [e.g., Uptime SLA]               | [99.9%]                    |
| [e.g., Recovery Time Objective]  | [< 1 hour]                 |
| [e.g., Recovery Point Objective] | [< 5 minutes of data loss] |

#### 6.2.3 Security & Compliance

> **🤖 AGENT INSTRUCTIONS:** List specific security requirements and compliance standards. Reference specific frameworks (SOC 2, GDPR, HIPAA) if applicable.

- [Security requirement 1]
- [Compliance requirement 1]

#### 6.2.4 Accessibility

- [e.g., WCAG 2.1 AA compliance]
- [Specific accessibility requirements]

#### 6.2.5 Internationalization / Localization

- [Supported languages]
- [Localization requirements]

---

## 7. User Experience

> **🤖 AGENT INSTRUCTIONS:** This section bridges requirements and design. Include enough detail for designers to understand intent, but don't over-specify visual design. Link to external design files rather than embedding them.

### 7.1 UX Principles

> **🤖 AGENT INSTRUCTIONS:** Define 3-5 guiding principles that should inform all design decisions for this product.

1. **[Principle 1]:** [Explanation]
2. **[Principle 2]:** [Explanation]

### 7.2 Information Architecture

> **🤖 AGENT INSTRUCTIONS:** Describe the high-level structure — main sections, navigation hierarchy, and how users move between areas.

[Description or link to sitemap diagram]

### 7.3 Key User Flows

> **🤖 AGENT INSTRUCTIONS:** Document the most critical user journeys step-by-step. These are the paths that MUST work well for the product to succeed.

#### Flow 1: [Flow Name, e.g., "First-time user onboarding"]
```
[Step 1] → [Step 2] → [Step 3] → [Success State]
           ↓
        [Error State] → [Recovery Path]
```

**Notes:** [Any important details about this flow]

### 7.4 Design Assets

| Asset      | Link                 | Status        |
|------------|----------------------|---------------|
| Wireframes | [Link to Figma/etc.] | [Draft/Final] |
| Mockups    | [Link]               | [Status]      |
| Prototype  | [Link]               | [Status]      |

---

## 8. Technical Considerations

> **🤖 AGENT INSTRUCTIONS:** This section provides context for engineering, NOT implementation instructions. A PRD defines WHAT, not HOW. Include just enough technical context for engineers to estimate and identify risks.

### 8.1 Architecture Overview

[High-level description of how this fits into the broader system — 1-2 paragraphs max, or a simple diagram]

### 8.2 Integration Points

| System        | Integration Type            | Description                               |
|---------------|-----------------------------|-------------------------------------------|
| [System name] | [API/Webhook/Database/etc.] | [What data or functionality is exchanged] |

### 8.3 Data Requirements

> **🤖 AGENT INSTRUCTIONS:** Describe what data the product needs, where it comes from, and any data governance considerations. Do NOT design the schema — that's for the technical spec.

- **Data needed:** [What information is required]
- **Data sources:** [Where it comes from]
- **Data sensitivity:** [PII, confidential, public, etc.]
- **Retention requirements:** [How long data must be kept]

### 8.4 API Requirements (if applicable)

> **🤖 AGENT INSTRUCTIONS:** If this product exposes or consumes APIs, describe them at a functional level. Detailed API specs belong in a separate technical document.

[High-level API requirements]

### 8.5 Known Technical Constraints

- [Constraint 1 and its impact]
- [Constraint 2 and its impact]

---

## 9. Scope & Phasing

### 9.1 In Scope — MVP / Phase 1

> **🤖 AGENT INSTRUCTIONS:** List only what will be delivered in the first release. Be ruthless — MVPs should be minimal. Cross-reference feature IDs from Section 6.

| Feature ID | Feature Name   | Priority  |
|------------|----------------|-----------|
| FR-001     | [Feature name] | Must Have |
| FR-002     | [Feature name] | Must Have |

### 9.2 Out of Scope

> **🤖 AGENT INSTRUCTIONS:** Explicitly list features that will NOT be in this release, even if users might expect them. This prevents scope creep and sets clear expectations.

| Feature   | Reason for Exclusion |
|-----------|----------------------|
| [Feature] | [Why it's deferred]  |

### 9.3 Future Phases Roadmap

> **🤖 AGENT INSTRUCTIONS:** Provide a high-level view of future releases. Don't over-commit — use tentative language and ranges rather than specific dates.

| Phase   | Tentative Timing | Key Features   |
|---------|------------------|----------------|
| Phase 2 | [Q3 2026]        | [Feature list] |
| Phase 3 | [Q4 2026]        | [Feature list] |

---

## 10. Dependencies & Risks

### 10.1 Dependencies

> **🤖 AGENT INSTRUCTIONS:** List anything external that this project depends on — other teams, systems, vendors, decisions. For each, identify the owner and current status.

| Dependency   | Owner         | Status                     | Impact if Delayed    |
|--------------|---------------|----------------------------|----------------------|
| [Dependency] | [Team/Person] | [On track/At risk/Blocked] | [Impact description] |

### 10.2 Assumptions

> **🤖 AGENT INSTRUCTIONS:** Document assumptions being made. If an assumption proves false, the PRD may need to be revised.

- [Assumption 1]
- [Assumption 2]

### 10.3 Risks & Mitigations

| Risk               | Likelihood        | Impact            | Mitigation Strategy                  |
|--------------------|-------------------|-------------------|--------------------------------------|
| [Risk description] | [High/Medium/Low] | [High/Medium/Low] | [How to reduce likelihood or impact] |

---

## 11. Timeline & Milestones

> **🤖 AGENT INSTRUCTIONS:** Provide a high-level timeline. Detailed project plans belong in separate project management tools. Focus on key milestones and decision points.

| Milestone               | Target Date | Description        | Go/No-Go Criteria         |
|-------------------------|-------------|--------------------|---------------------------|
| [e.g., Design Complete] | [Date]      | [What's delivered] | [Criteria for proceeding] |
| [e.g., Dev Complete]    | [Date]      | [What's delivered] | [Criteria for proceeding] |
| [e.g., Launch]          | [Date]      | [What's delivered] | [Launch criteria]         |

---

## 12. Appendix

### 12.1 Glossary

> **🤖 AGENT INSTRUCTIONS:** Define any terms that might be unfamiliar to readers. Include product-specific terminology and acronyms.

| Term   | Definition   |
|--------|--------------|
| [Term] | [Definition] |

### 12.2 Research & References

> **🤖 AGENT INSTRUCTIONS:** Link to supporting materials — user research, competitive analysis, technical research. Summarize key findings rather than just linking.

| Document                      | Link   | Key Findings           |
|-------------------------------|--------|------------------------|
| [e.g., User Research Summary] | [Link] | [1-2 sentence summary] |

### 12.3 Competitive Analysis

| Competitor     | Strengths   | Weaknesses   | Our Differentiation          |
|----------------|-------------|--------------|------------------------------|
| [Competitor 1] | [Strengths] | [Weaknesses] | [How we're different/better] |

### 12.4 Open Questions

> **🤖 AGENT INSTRUCTIONS:** Track unresolved questions that need answers before or during development. Assign owners and target dates for resolution.

| Question   | Owner    | Target Resolution Date | Status          |
|------------|----------|------------------------|-----------------|
| [Question] | [Person] | [Date]                 | [Open/Resolved] |

---

## Document Approval

| Role             | Name | Date | Signature |
|------------------|------|------|-----------|
| Product Owner    |      |      |           |
| Engineering Lead |      |      |           |
| Design Lead      |      |      |           |
| Stakeholder      |      |      |           |

---

> **🤖 AGENT META-INSTRUCTIONS:**
>
> When helping to complete this PRD:
> 1. **Ask clarifying questions** before filling sections — don't assume
> 2. **Be specific and measurable** — avoid vague language like "fast," "easy," or "intuitive"
> 3. **Maintain consistency** — use the same terminology throughout; update the glossary
> 4. **Cross-reference** — ensure features map to user stories, user stories map to personas, metrics map to goals
> 5. **Challenge assumptions** — if something seems unclear or contradictory, flag it
> 6. **Prioritize ruthlessly** — push back on "everything is Must Have" thinking
> 7. **Keep it living** — update the version history when making significant changes
