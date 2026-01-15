---
name: emoji-standards
description: Apply standardized emoji taxonomy for professional documentation, status reports, and technical output. Covers log levels, status hierarchy, test types, process indicators, and when NOT to use emojis. Use for "what emoji for", "status indicator", "format output", "emoji for error", "success symbol", or any documentation formatting task.
---

# Emoji Standards for Professional Documentation

Expert-level guidance for consistent emoji usage across documentation, reports, logs, and technical output.

## Quick Decision Matrix

| Context                  | Use Emojis?   | Rationale                                      |
|--------------------------|---------------|------------------------------------------------|
| Status reports/summaries | ✅ Yes        | Visual scanning, quick status identification   |
| Todo lists               | ✅ Yes        | State tracking at a glance                     |
| Log output (human-read)  | ✅ Yes        | Severity instantly visible                     |
| Commit messages          | ⚠️ Sparingly  | Type prefix only (feat, fix), not body         |
| PR descriptions          | ✅ Yes        | Status sections, test results                  |
| Code comments            | ❌ No         | Unprofessional, distracting                    |
| API documentation        | ❌ No         | Formal technical writing                       |
| Error messages (user)    | ❌ No         | Can seem unprofessional to end users           |
| Internal dev tools       | ✅ Yes        | Quick visual parsing                           |

## Core Taxonomy

### Log Levels (Colored Circles - Primary)

```
Trace       → ⚪ White Circle
Debug       → 🔵 Blue Circle
Information → 🟢 Green Circle
Warning     → 🟡 Yellow Circle
Error       → 🟠 Orange Circle
Critical    → 🔴 Red Circle
None        → ⚫ Black Circle
```

**Usage:**
```
🟢 Build succeeded
🟡 3 warnings found
🟠 Test failed: ConnectionTests
🔴 CRITICAL: Database unreachable
```

### Status Hierarchy

| Emoji | Meaning                              | When to Use                          |
|-------|--------------------------------------|--------------------------------------|
| 🟢    | Operational/Success/Passed/OK        | Tests passed, service healthy        |
| 🟡    | Warning/Caution/Review Required      | Non-blocking issues, needs attention |
| 🟠    | Error/Failure/Issue Detected         | Failed but recoverable               |
| 🔴    | Critical/Emergency/Priority Alpha    | System down, data loss risk          |
| 🔵    | Information/Debug/Technical Details  | FYI, context, diagnostics            |
| ⚪    | Low Priority/Minimal/Trace           | Verbose logging, minor notes         |
| ⚫    | Disabled/None/Inactive               | Feature off, skipped                 |

### Operational States (Binary Outcomes)

| Emoji | Meaning                        | When to Use                              |
|-------|--------------------------------|------------------------------------------|
| ✅    | Confirmed/Approved/Completed   | Task done, PR approved, test passed      |
| ❌    | Rejected/Failed/Prohibited     | Task blocked, PR rejected, test failed   |
| ⚠️    | Attention Required/Caution     | Needs human review, potential issue      |
| 🚨    | Critical Alert/Emergency       | Immediate action required                |
| ℹ️    | Information/Reference/Notice   | FYI, no action needed                    |

### Process States

| Emoji | Meaning                        | When to Use                              |
|-------|--------------------------------|------------------------------------------|
| 🔄    | In Progress/Processing         | Currently running, work ongoing          |
| 🔍    | Investigation/Analysis         | Debugging, researching                   |
| 📖    | Documentation/Reference        | Docs update, reference material          |
| 🔧    | Maintenance/Configuration      | Config change, infra work                |
| 🤖    | Automated Process/AI System    | Bot action, CI/CD, generated             |

### Performance & Optimization

| Emoji | Meaning              | When to Use                                    |
|-------|----------------------|------------------------------------------------|
| 🚀    | Performance/Speed    | Perf improvements, optimization                |
| 📊    | Metrics/Data         | Benchmarks, statistics, analysis               |

### Test Types

| Emoji | Test Type                    |
|-------|------------------------------|
| 🧪    | Unit Tests                   |
| 🔗    | Integration Tests            |
| 🚀    | Performance Tests            |
| 📦    | Black Box Tests              |
| 🔒    | Security/Penetration Tests   |
| 🔄    | Regression Tests             |
| 🎯    | End-to-End/Acceptance Tests  |
| 🔬    | Component/Module Tests       |
| 🛡️    | Defensive/Resilience Tests   |

### Human Attention Indicators (Use Sparingly)

| Emoji | Meaning                              | When to Use                                |
|-------|--------------------------------------|--------------------------------------------|
| 💀    | Severe Failure/Termination/Death     | Unrecoverable, process killed              |
| 🔥    | Urgent Action Required               | Drop everything, fix now                   |

**Warning:** Overuse of 💀 and 🔥 dilutes their impact. Reserve for genuine emergencies.

## Practical Patterns

### Todo Lists

```
✅ Implement user authentication
🔄 Write integration tests
❌ Blocked: waiting on API spec
⚪ Low priority: optimize caching
```

### Test Results Summary

```
## Test Results
🟢 Unit Tests: 142/142 passed
🟡 Integration Tests: 28/30 passed (2 flaky)
🔴 E2E Tests: 5/12 passed - BLOCKING
```

### PR Status Section

```
## Checklist
✅ Tests passing
✅ Code reviewed
⚠️ Performance impact: +15ms p99 latency
🔄 Waiting for security review
```

### Build Output

```
🔵 Building project...
🟢 Build succeeded (12.3s)
🟡 2 warnings: CS0618 (obsolete API usage)
🚀 Running tests...
🟢 All 847 tests passed
```

### Incident Status

```
🔴 INCIDENT: Database connection pool exhausted
🔄 Investigating: @oncall checking connection limits
✅ RESOLVED: Increased pool size, deployed fix
```

## Anti-Patterns

### Don't Do This

```csharp
// ❌ BAD: Emojis in code comments
// 🚀 This method is FAST! 💪
public void ProcessData() { }
```

```
// ❌ BAD: Emoji in error messages shown to users
throw new Exception("🔴 Database connection failed!");
```

```
// ❌ BAD: Too many emojis (visual noise)
🎉🚀✨ Build succeeded! 🎊🔥💯
```

```
// ❌ BAD: Inconsistent usage
✔️ Task 1 done     (using check mark)
✅ Task 2 done     (using checkbox)
☑️ Task 3 done     (using ballot box)
```

### Do This Instead

```
// ✅ GOOD: Consistent, minimal, informative
🟢 Build succeeded
✅ All tasks completed
```

## Suggested Additions

### Development Workflow

| Emoji | Meaning              | Suggested Use                    |
|-------|----------------------|----------------------------------|
| 🏗️    | Work in Progress     | Scaffold, initial implementation |
| 📝    | Draft/WIP            | Not ready for review             |
| 🧹    | Cleanup/Refactor     | Code cleanup, tech debt          |
| 🐛    | Bug                  | Bug fix, debugging               |
| 💡    | Idea/Suggestion      | Proposal, enhancement idea       |
| 🔒    | Security             | Security fix, auth change        |
| ⬆️    | Upgrade              | Dependency update                |
| ⬇️    | Downgrade            | Rollback, version decrease       |
| 🏷️    | Release/Tag          | Version release                  |

### Code Review

| Emoji | Meaning              | Reviewer Comment Use             |
|-------|----------------------|----------------------------------|
| 💭    | Question             | Need clarification               |
| 🔧    | Suggestion           | Optional improvement             |
| ⚠️    | Concern              | Should address before merge      |
| 🚫    | Blocking             | Must fix before merge            |
| 👍    | Approval             | Looks good                       |

### Documentation

| Emoji | Meaning              | Docs Use                         |
|-------|----------------------|----------------------------------|
| 📌    | Important            | Key information, pin this        |
| 📎    | Related              | See also, cross-reference        |
| 🔗    | Link                 | External reference               |
| ⏰    | Time-sensitive       | Deadline, SLA                    |
| 🗓️    | Scheduled            | Planned date                     |

## Platform Considerations

### Terminal Output
- Most modern terminals render emoji correctly
- Some CI systems may not (use sparingly in CI logs)
- Windows CMD may have rendering issues

### Cross-Platform Rendering
- Stick to common emoji (no skin tones, no complex sequences)
- Colored circles (🔴🟢🟡) render consistently everywhere
- Avoid emoji that look different across platforms

## References

- Project taxonomy: `.agents/emoji.instructions.md`
- [Unicode Emoji Charts](https://unicode.org/emoji/charts/full-emoji-list.html)
- [GitHub Emoji Cheat Sheet](https://github.com/ikatyang/emoji-cheat-sheet)
