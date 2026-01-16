# Product Brief: [Product Name]

---

> **🤖 AGENT INSTRUCTIONS:** This is a lightweight product brief — NOT a full PRD. Keep it concise (ideally 2-4 pages). Focus on clarity and alignment, not exhaustive detail. Use this to get stakeholder buy-in before investing in a comprehensive PRD.

---

## Document Info

| Field            | Value                          |
|------------------|--------------------------------|
| **Product Name** | [Product Name]                 |
| **Author**       | [Name]                         |
| **Date**         | [Date]                         |
| **Status**       | [Draft / In Review / Approved] |

---

## 1. Problem Statement
*What pain point are you solving, and why does it matter now?*

> **🤖 AGENT INSTRUCTIONS:** Write 2-4 sentences maximum. Be specific and quantifiable — avoid vague statements like "users have trouble with X." Include WHO experiences the problem, WHAT the problem is, and the IMPACT (time lost, money wasted, frustration caused). If you don't have data, ask the author for specifics or flag it as an assumption to validate.

[Describe the specific problem your product addresses. Be concrete — e.g., "Data engineers spend 5+ hours weekly managing stream processing pipelines because existing tools require manual configuration for each data source..."]

---

## 2. Target Users
*Who is this for?*

> **🤖 AGENT INSTRUCTIONS:** Limit to 2-3 personas maximum for a brief. Focus on the PRIMARY user first. Include just enough detail to make decisions — role, context, and their #1 need. Avoid generic descriptions like "developers who want efficiency." If the author lists too many personas, push back — who is the MOST important user?

| Persona       | Description                                                                                       | Key Need                                         |
|---------------|---------------------------------------------------------------------------------------------------|--------------------------------------------------|
| **Primary**   | [e.g., Data Engineers at mid-size companies (50-500 employees) managing real-time data pipelines] | [Their single most important goal or pain point] |
| **Secondary** | [e.g., Platform/DevOps teams responsible for infrastructure]                                      | [Their single most important goal or pain point] |

---

## 3. Product Vision (One-Liner)
*The "elevator pitch"*

> **🤖 AGENT INSTRUCTIONS:** This should be ONE sentence that anyone in the company can remember and repeat. Use the format: "[Product] enables [target user] to [achieve outcome] by [key differentiator]." Test it: if you can't say it in one breath, it's too long. Avoid jargon and marketing fluff.

> [Product Name] enables [target user] to [achieve outcome] by [how it works differently from alternatives].

---

## 4. Goals & Success Metrics

> **🤖 AGENT INSTRUCTIONS:** Limit to 3-5 metrics maximum. Each metric MUST be measurable with a specific target and timeframe. Push back on vague goals like "improve user experience" — ask "how will we know if the experience is better?" If the author doesn't have baselines, note that establishing baselines should be a pre-launch task.

| Goal                          | Metric                                 | Target                                    |
|-------------------------------|----------------------------------------|-------------------------------------------|
| [e.g., Faster pipeline setup] | [e.g., Time to first working pipeline] | [e.g., < 10 minutes (down from 2+ hours)] |
| [e.g., User adoption]         | [e.g., Weekly active users]            | [e.g., 500 WAU by end of Q2]              |
| [e.g., User satisfaction]     | [e.g., NPS score]                      | [e.g., > 40]                              |

---

## 5. Core Features (MVP)

> **🤖 AGENT INSTRUCTIONS:** Be ruthless here — an MVP should have 3-7 features maximum. If there are more, challenge which are truly "Must Have" for launch vs. fast-follow. Each feature needs a clear user story that ties back to the personas and problem statement. If a feature doesn't connect to a stated user need, question whether it belongs.

| Feature     | User Story                                           | Priority        |
|-------------|------------------------------------------------------|-----------------|
| [Feature 1] | As a [persona], I want to [action] so that [benefit] | **Must Have**   |
| [Feature 2] | As a [persona], I want to [action] so that [benefit] | **Must Have**   |
| [Feature 3] | As a [persona], I want to [action] so that [benefit] | **Should Have** |
| [Feature 4] | As a [persona], I want to [action] so that [benefit] | **Could Have**  |

> **Priority Guide:**
> - **Must Have:** Launch is impossible without this
> - **Should Have:** Important, but launch could happen without it
> - **Could Have:** Nice to have if time permits

---

## 6. Out of Scope (This Release)
*What you're explicitly NOT building*

> **🤖 AGENT INSTRUCTIONS:** This section is critical for preventing scope creep. List features that stakeholders might expect but WON'T be included. Be explicit about WHY each is deferred — this helps when stakeholders push back. If the author hasn't thought about this, prompt them: "What features might users expect that you're intentionally leaving out?"

- **[Feature/capability to defer]:** [Reason — e.g., "Requires integration with X which isn't ready until Q3"]
- **[Another deferred item]:** [Reason]
- **[Another deferred item]:** [Reason]

---

## 7. Open Questions / Risks

> **🤖 AGENT INSTRUCTIONS:** Every brief should have open questions — if there are none, the author hasn't thought deeply enough. Prompt for questions in these areas: technical feasibility, user validation, dependencies on other teams, market timing, resource constraints. For risks, include likelihood and potential mitigation even at this early stage.

### Open Questions
- [Any unresolved decisions that could impact scope or approach?]
- [Any assumptions that need validation before committing?]

### Risks
| Risk                                           | Impact                                 | Mitigation                                               |
|------------------------------------------------|----------------------------------------|----------------------------------------------------------|
| [e.g., Dependency on Platform team's API]      | [e.g., Could delay launch by 4+ weeks] | [e.g., Early alignment meeting scheduled for next week]  |
| [e.g., Unvalidated assumption about user need] | [e.g., Could build wrong solution]     | [e.g., Running 5 user interviews before finalizing spec] |

---

## 8. Next Steps

> **🤖 AGENT INSTRUCTIONS:** A brief should lead to action. List 3-5 concrete next steps with owners and dates. Typical next steps: stakeholder review, user validation, technical feasibility check, full PRD creation, design kickoff.

| Action                                              | Owner  | Due Date |
|-----------------------------------------------------|--------|----------|
| [e.g., Review brief with engineering lead]          | [Name] | [Date]   |
| [e.g., Conduct user interviews to validate problem] | [Name] | [Date]   |
| [e.g., Create full PRD if brief is approved]        | [Name] | [Date]   |

---

> **🤖 AGENT META-INSTRUCTIONS:**
>
> When helping complete this Product Brief:
> 1. **Keep it short** — if a section is getting long, it probably belongs in a full PRD, not a brief
> 2. **Ask "so what?"** — every statement should have a clear implication for what to build
> 3. **Connect the dots** — ensure features trace back to user needs, and metrics trace back to goals
> 4. **Flag gaps** — if key information is missing (e.g., no success metrics, no personas), explicitly note it
> 5. **Challenge "Must Haves"** — if everything is Must Have, nothing is. Push for real prioritization
> 6. **Keep it actionable** — this brief should enable a go/no-go decision; if it doesn't, something's missing
