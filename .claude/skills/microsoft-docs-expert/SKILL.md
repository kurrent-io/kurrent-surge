---
name: microsoft-docs-expert
description: Master optimal usage of Microsoft Learn MCP server tools (microsoft_docs_search, microsoft_code_sample_search, microsoft_docs_fetch) for efficient Microsoft/Azure documentation retrieval. Use this skill when working with any Microsoft or Azure technologies (.NET, C#, Azure services, Microsoft 365, etc.) to access current official documentation through an optimized three-tier workflow (search → code samples → fetch). This skill is essential for implementation tasks, troubleshooting, API discovery, and architectural decisions involving Microsoft technologies.
---

# Microsoft Docs Expert

## Overview

Master the microsoft-docs MCP server for optimal Microsoft and Azure documentation retrieval. This skill provides the procedural knowledge to seamlessly use three complementary tools that access real-time official Microsoft documentation.

**Core Tools:**
1. **microsoft_docs_search** - Semantic search returning up to 10 content chunks (~5k tokens)
2. **microsoft_code_sample_search** - Code-specific search with language filtering, up to 20 samples
3. **microsoft_docs_fetch** - Complete documentation page retrieval (10k+ tokens)

**When to use this skill:** Any work involving Microsoft or Azure technologies, including .NET, C#, ASP.NET Core, Entity Framework, Azure services, Microsoft 365, Microsoft Graph API, or any other Microsoft technology stack.

## Workflow Decision Tree

Use this decision tree to determine which tool(s) to use:

```
START: What do you need?
│
├─ Quick reference / API lookup / Feature verification
│  └─→ microsoft_docs_search
│      └─→ DONE (70% of queries)
│
├─ Code implementation examples / Best practices
│  └─→ microsoft_code_sample_search (with language parameter)
│      ├─→ DONE (if examples sufficient)
│      └─→ microsoft_docs_fetch (if need context around examples)
│
├─ Complete tutorial / All configuration options / Troubleshooting guide
│  └─→ microsoft_docs_search (find URL)
│      └─→ microsoft_docs_fetch (retrieve complete page)
│
├─ Architecture decision / Comparison / Deep learning
│  └─→ microsoft_docs_search (find comparison/guide URL)
│      └─→ microsoft_docs_fetch (complete architectural guide)
│
└─ Implementation task (typical workflow)
   └─→ microsoft_docs_search (overview + identify URLs)
       └─→ microsoft_code_sample_search (implementation examples)
           └─→ microsoft_docs_fetch (deep dive if needed)
```

## Core Workflow Patterns

### Pattern 1: Quick Lookup (70% of queries)

**Use:** Finding specific APIs, commands, configuration options, or feature verification.

**Workflow:**
```
1. Formulate specific query with technology context
2. Call microsoft_docs_search
3. Extract answer from results
```

**Query formulation:**
- Include technology name + version
- Be specific: ✅ "Azure CLI create Container App managed identity" vs ❌ "azure containers"
- Use technical terms, not natural language descriptions

**Example:**
```
Query: "Entity Framework Core 9 complex types mapping"
→ Returns 10 content chunks
→ Answer found in results
→ DONE
```

**Token cost:** ~2-5k tokens

---

### Pattern 2: Code Implementation (20% of queries)

**Use:** Need runnable code examples and best practice implementations.

**Workflow:**
```
1. Determine programming language
2. Call microsoft_code_sample_search with language parameter
3. Review code samples
4. If context needed → fetch documentation page
```

**Language parameter optimization:**
- Always specify language when known
- Reduces irrelevant results
- Supported: csharp, javascript, typescript, python, powershell, azurecli, java, go, rust, etc.

**Example:**
```
Query: "azure key vault secret retrieval"
Language: "csharp"
→ Returns 20 C# code samples
→ Working code obtained
→ DONE
```

**Token cost:** ~3-8k tokens

---

### Pattern 3: Complete Understanding (10% of queries)

**Use:** Need full documentation page for tutorials, troubleshooting, or comprehensive guides.

**Workflow:**
```
1. Search to identify relevant documentation URL
2. Evaluate URL quality from results
3. Fetch complete page
```

**URL quality indicators:**
- learn.microsoft.com > docs.microsoft.com
- /tutorials/, /guide/, /architecture/ paths = comprehensive content
- /quickstart/, /overview/ paths = may lack depth

**Example:**
```
Step 1: microsoft_docs_search("Azure Functions Durable orchestration")
→ Identify best URL in results

Step 2: microsoft_docs_fetch("https://learn.microsoft.com/azure/azure-functions/durable/...")
→ Complete tutorial retrieved
→ DONE
```

**Token cost:** ~15-30k tokens (use strategically)

---

### Pattern 4: Full Implementation Workflow

**Use:** Implementing new features end-to-end.

**Workflow:**
```
1. Search: Get overview + identify URLs
2. Code Samples: Get implementation examples
3. Fetch: Deep dive on specific areas (if needed)
```

**Example: Implementing Azure OpenAI streaming in Python**
```
Step 1: microsoft_docs_search
Query: "Azure OpenAI streaming completion Python"
→ Understand concepts
→ Identify relevant URLs
→ Cost: ~5k tokens

Step 2: microsoft_code_sample_search
Query: "azure openai streaming"
Language: "python"
→ Get working code
→ Cost: ~6k tokens

Step 3: microsoft_docs_fetch (if needed)
URL: [configuration guide from step 1]
→ Deep dive on setup
→ Cost: ~15k tokens

Total: ~26k tokens for complete implementation guidance
```

---

## Query Optimization Rules

Follow these rules for optimal results on first try:

### Rule 1: Specificity Formula
**Structure:** `[Technology] [Version] [Feature] [Operation]`

**Examples:**
- ✅ "ASP.NET Core 9.0 minimal API endpoint filters"
- ✅ "Azure Functions v4 Durable Functions orchestration patterns"
- ❌ "API filters"
- ❌ "orchestration"

### Rule 2: Include Version Numbers
Always include version numbers when relevant to avoid outdated information.

**Examples:**
- ✅ "C# 14 primary constructors syntax"
- ✅ ".NET 9 LINQ improvements"
- ❌ "C# primary constructors" (might return C# 12 examples)

### Rule 3: Action-Oriented Queries
Frame queries as problems to solve or actions to perform.

**Structure:** `[Action Verb] [Technology] [Context]`

**Examples:**
- ✅ "implement authentication in Blazor WebAssembly Azure AD"
- ✅ "configure retry policies Azure Service Bus"
- ❌ "blazor authentication"
- ❌ "service bus retry"

### Rule 4: Always Use Language Parameter
For code sample searches, always specify the language parameter.

**Examples:**
```python
# Good
microsoft_code_sample_search(
    query="dependency injection configuration",
    language="csharp"
)

# Bad - language in query text
microsoft_code_sample_search(
    query="csharp dependency injection configuration"
)
```

---

## Token Management Strategy

Optimize context window usage with this token budget approach:

### Token Costs
- **Search:** ~2-5k tokens per call
- **Code Samples:** ~3-8k tokens per call
- **Fetch:** ~15-30k tokens per call (can be higher)

### Optimization Strategy
1. **Always start with search** (lowest cost, sufficient 70% of time)
2. **Use code samples when need implementation** (medium cost, targeted)
3. **Fetch only when necessary** (highest cost, complete information)

### Budget Allocation
For typical implementation task:
- 2-3 searches: 10-15k tokens
- 1-2 code sample searches: 6-16k tokens
- 1-2 fetches: 20-40k tokens
- **Total: 36-71k tokens** (reasonable for complex task)

**Warning:** Fetching 5+ pages = 75k+ tokens = context bloat. Fetch selectively.

---

## Tool Interplay Examples

### Example 1: API Integration
```
Goal: Integrate Microsoft Graph API for calendar access

Step 1: microsoft_docs_search
Query: "Microsoft Graph API calendar events access"
Action: Understand endpoints, identify URLs
Cost: 5k tokens

Step 2: microsoft_code_sample_search
Query: "microsoft graph calendar events"
Language: "csharp"
Action: Get authentication + API call code
Cost: 7k tokens

Result: Working implementation
Total: 12k tokens
```

### Example 2: Troubleshooting
```
Goal: Fix "401 Unauthorized" in Azure Key Vault

Step 1: microsoft_docs_search
Query: "Azure Key Vault 401 unauthorized authentication"
Action: Find troubleshooting guide URL
Cost: 4k tokens

Step 2: microsoft_docs_fetch
URL: [troubleshooting guide]
Action: Read complete diagnostic steps
Cost: 18k tokens

Result: Root cause identified
Total: 22k tokens
```

### Example 3: Architecture Decision
```
Goal: Choose between Azure Functions and Container Apps

Step 1: microsoft_docs_search
Query: "Azure Functions vs Container Apps comparison"
Action: Find comparison documentation
Cost: 5k tokens

Step 2: microsoft_docs_fetch
URL: [comparison guide]
Action: Read complete comparison
Cost: 20k tokens

Step 3: microsoft_docs_search (for each option)
Query: "Azure Functions best practices"
Query: "Azure Container Apps best practices"
Action: Understand operational considerations
Cost: 10k tokens (2 searches)

Result: Informed decision
Total: 35k tokens
```

---

## Common Pitfalls to Avoid

### Pitfall 1: Fetching First
**Problem:** Using microsoft_docs_fetch as first resort
**Impact:** Wastes tokens, slower results
**Solution:** Always search first to identify best URLs

### Pitfall 2: Vague Queries
**Problem:** Generic queries like "Azure" or "authentication"
**Impact:** Irrelevant results, wasted calls
**Solution:** Follow specificity formula, include context

### Pitfall 3: Ignoring Language Parameter
**Problem:** Not specifying language in code sample searches
**Impact:** Results include irrelevant languages
**Solution:** Always specify language when known

### Pitfall 4: Over-Fetching
**Problem:** Fetching 5+ documentation pages
**Impact:** Context window bloat (75k+ tokens)
**Solution:** Fetch maximum 2-3 pages per task, use search for others

### Pitfall 5: Not Combining Tools
**Problem:** Using only one tool when multiple would be better
**Impact:** Incomplete understanding or implementation
**Solution:** Use search → code samples → fetch workflow

---

## Error Recovery Patterns

### No Results from Search
```
Problem: Query too specific or wrong terminology

Recovery:
1. Broaden query (remove version numbers)
2. Try alternative terminology
3. Search for related concepts
4. Break into smaller queries
```

### Fetch Returns 404
```
Problem: Invalid or moved URL

Recovery:
1. Search for topic to find current URL
2. Check URL format (docs.microsoft.com → learn.microsoft.com)
3. Remove URL fragments (#anchors)
```

### Results Missing Information
```
Problem: Search excerpts insufficient

Recovery:
1. Identify best URL from search results
2. Use microsoft_docs_fetch on that URL
3. If still insufficient, search sub-topics
```

---

## Real-Time Documentation Advantage

The microsoft-docs tools provide **current** official documentation, essential for:

**Recently Released Features:**
- .NET 9 (November 2024)
- C# 14 syntax (2024)
- New Azure services
- Updated API versions

**Avoiding Deprecated Approaches:**
- Identify when features are deprecated
- Find migration paths
- Use current best practices

**When to Use Tools vs. Model Knowledge:**
- **Use tools:** Specific APIs, new features (<1 year), exact syntax, configuration options
- **Use model knowledge:** Stable concepts, fundamental patterns, historical context

---

## Detailed References

For comprehensive information, consult these reference files:

### @references/tool_reference.md
Complete documentation for all three tools:
- Detailed parameter specifications
- Return value structures
- Best use cases for each tool
- Query optimization techniques
- Real-world query examples
- Tool reliability notes

### @references/optimization_patterns.md
Advanced optimization strategies:
- Three-tier strategy (discovery → implementation → deep dive)
- Query formulation patterns
- Workflow optimization patterns
- Context window management
- Token efficiency metrics
- Multi-language scenarios
- URL management patterns
- Performance optimization checklist

---

## Quick Reference

### When to Use Each Tool

| Need                    | Tool                        | Token Cost |
|-------------------------|-----------------------------|------------|
| Quick API lookup        | microsoft_docs_search       | ~2-5k      |
| Code examples           | microsoft_code_sample_search| ~3-8k      |
| Complete tutorial       | microsoft_docs_fetch        | ~15-30k    |
| Feature discovery       | microsoft_docs_search       | ~2-5k      |
| Troubleshooting         | search → fetch              | ~20-35k    |
| Implementation          | search → samples → fetch    | ~25-45k    |

### Tool Parameter Quick Reference

**microsoft_docs_search:**
```
query: string (required) - descriptive search query
```

**microsoft_code_sample_search:**
```
query: string (required) - implementation-focused query
language: string (optional but recommended) - programming language filter
  Values: csharp, javascript, typescript, python, powershell, azurecli,
          java, go, rust, ruby, php, cpp, sql, kusto, al
```

**microsoft_docs_fetch:**
```
url: string (required) - Microsoft Learn documentation URL
  Must be from microsoft.com domain
```

---

## Mastery Checklist

Achieve skill mastery when consistently demonstrating:

- [ ] Choose correct tool instantly without reference
- [ ] Queries return relevant results >80% on first try
- [ ] Use search before fetch automatically
- [ ] Always include language parameter for code searches
- [ ] Evaluate URLs from search before fetching
- [ ] Fetch maximum 2-3 pages per implementation task
- [ ] Recognize when search results are sufficient vs. need fetch
- [ ] Understand token costs and optimize accordingly
- [ ] Handle errors with effective retry strategies
- [ ] Combine tools appropriately for complex tasks

**Time to mastery:** Immediate - follow workflow patterns and optimization rules for instant effectiveness.
