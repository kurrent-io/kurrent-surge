# Microsoft Docs MCP - Optimization Patterns

Advanced patterns and strategies for maximizing efficiency with microsoft-docs MCP server tools.

---

## The Three-Tier Strategy

### Tier 1: Quick Discovery (microsoft_docs_search)
**Cost:** Low tokens (~5k max)
**Speed:** Fast
**Use:** 70% of queries

When to use:
- Initial exploration
- Quick reference lookups
- API discovery
- Feature verification
- URL identification

### Tier 2: Code Implementation (microsoft_code_sample_search)
**Cost:** Medium tokens (variable)
**Speed:** Fast
**Use:** 20% of queries

When to use:
- Need runnable examples
- Best practice implementations
- Syntax verification
- Integration patterns

### Tier 3: Deep Understanding (microsoft_docs_fetch)
**Cost:** High tokens (10k+)
**Speed:** Slower (larger payloads)
**Use:** 10% of queries

When to use:
- Search insufficient
- Complete tutorials needed
- Comprehensive guides
- All configuration options required

---

## Query Formulation Patterns

### Pattern 1: Technology Stack Specificity
Include the complete technology context in queries.

**Structure:** `[Technology] [Version] [Feature] [Operation]`

**Examples:**
```
✅ "ASP.NET Core 9.0 minimal API endpoint filters"
✅ "Entity Framework Core 9 complex types mapping"
✅ "Azure Functions v4 Durable Functions orchestration"

❌ "API filters"
❌ "complex types"
❌ "orchestration"
```

### Pattern 2: Problem-Solution Framing
Frame queries as problems to be solved.

**Structure:** `[Action Verb] [Technology] [Context]`

**Examples:**
```
✅ "implement authentication in Blazor WebAssembly with Azure AD"
✅ "configure retry policies for Azure Service Bus"
✅ "optimize Entity Framework Core queries for large datasets"

❌ "blazor authentication"
❌ "service bus retry"
❌ "ef core performance"
```

### Pattern 3: Comparative Queries
When choosing between options, search for comparisons.

**Structure:** `[Option A] vs [Option B] [Context]`

**Examples:**
```
✅ "Azure Functions vs Container Apps for microservices"
✅ "SignalR vs Azure Web PubSub real-time communication"
✅ "Cosmos DB vs Azure SQL Database for multi-region"

❌ "should I use functions or containers"
❌ "signalr or pubsub"
❌ "database comparison"
```

### Pattern 4: Version-Aware Queries
Always include version numbers when relevant.

**Structure:** `[Technology] [Version Number] [Feature]`

**Examples:**
```
✅ "C# 14 primary constructors"
✅ ".NET 9 LINQ improvements"
✅ "Azure OpenAI API version 2024-02-01"

❌ "C# primary constructors" (might return old versions)
❌ ".NET LINQ" (unclear which version)
❌ "Azure OpenAI API" (version matters for breaking changes)
```

---

## Workflow Optimization Patterns

### Pattern 1: Search → Sample → Fetch
**Best for:** Implementing new features

```
Step 1: High-level understanding
microsoft_docs_search(query="feature overview")

Step 2: Implementation examples
microsoft_code_sample_search(query="feature implementation", language="...")

Step 3: Complete details (if needed)
microsoft_docs_fetch(url=[best URL from step 1])
```

**Example:**
```
Implementing Azure Key Vault in .NET app

1. microsoft_docs_search("Azure Key Vault .NET SDK secrets management")
   → Understand concepts, get URLs

2. microsoft_code_sample_search("azure key vault secret retrieval", language="csharp")
   → Get runnable code

3. microsoft_docs_fetch([configuration guide URL])
   → Deep dive on permissions and setup
```

### Pattern 2: Search → Fetch (Skip Samples)
**Best for:** Architectural decisions, conceptual learning

```
Step 1: Find relevant documentation
microsoft_docs_search(query="architectural pattern")

Step 2: Deep dive
microsoft_docs_fetch(url=[architecture guide URL])
```

**Example:**
```
Choosing message queue service

1. microsoft_docs_search("Azure messaging services comparison")
   → Find comparison guide URL

2. microsoft_docs_fetch([comparison guide URL])
   → Read complete comparison
```

### Pattern 3: Sample → Fetch (Skip Search)
**Best for:** Known technology, need implementation details

```
Step 1: Get code examples
microsoft_code_sample_search(query="specific implementation", language="...")

Step 2: Context from snippet source
microsoft_docs_fetch(url=[URL from code sample])
```

**Example:**
```
Implementing specific design pattern

1. microsoft_code_sample_search("repository pattern entity framework", language="csharp")
   → Find pattern implementation

2. microsoft_docs_fetch([pattern explanation URL from sample])
   → Understand pattern context
```

### Pattern 4: Parallel Search Strategy
**Best for:** Comparing multiple technologies

```
Parallel execution:
- microsoft_docs_search(query="technology A [feature]")
- microsoft_docs_search(query="technology B [feature]")

Then compare results
```

**Example:**
```
Comparing authentication approaches

Parallel:
1. microsoft_docs_search("Azure AD B2C authentication flow")
2. microsoft_docs_search("Azure AD authentication flow")

Compare results to make decision
```

---

## Context Window Management

### Token Budget Strategy

**Total Context Budget:** Assume ~200k tokens typical
**System/Tools Overhead:** ~50k tokens
**Available for Work:** ~150k tokens

**Allocation Strategy:**
- Reserve 50k for conversation history
- Reserve 30k for code under development
- Use remaining 70k for documentation

**Documentation Budget:**
- Search results: 5k tokens each (can afford ~14 searches)
- Code samples: 10k tokens each (can afford ~7 searches)
- Fetch results: 15-20k tokens each (can afford ~3-4 fetches)

### Progressive Loading Pattern

Load documentation progressively as needed:

```
Phase 1: Broad search (low token cost)
→ Identify specific areas

Phase 2: Targeted searches (medium token cost)
→ Get detailed information

Phase 3: Selective fetches (high token cost)
→ Only for critical deep dives
```

**Example:**
```
Building complete Azure solution

Phase 1 - Overview (1 search = 5k tokens):
microsoft_docs_search("Azure solution architecture best practices")

Phase 2 - Components (3 searches = 15k tokens):
microsoft_docs_search("Azure App Service deployment")
microsoft_docs_search("Azure SQL Database security")
microsoft_docs_search("Azure Key Vault integration")

Phase 3 - Deep Dives (2 fetches = 30k tokens):
microsoft_docs_fetch([App Service URL])
microsoft_docs_fetch([Key Vault URL])

Total: 50k tokens for documentation
```

---

## Language Parameter Optimization

### When to Use Language Parameter

**Always use when:**
- You know the target language
- Working within a specific codebase
- Migrating code between languages (specify target language)

**Language parameter advantages:**
1. Filters out irrelevant results
2. Reduces token consumption
3. Faster result processing
4. More precise examples

### Multi-Language Scenarios

When working with multiple languages, run **sequential** searches:

```python
# Example: Full-stack implementation

# Backend (Python)
microsoft_code_sample_search(
    query="azure functions http trigger",
    language="python"
)

# Frontend (TypeScript)
microsoft_code_sample_search(
    query="azure functions http client",
    language="typescript"
)

# Infrastructure (CLI)
microsoft_code_sample_search(
    query="deploy azure functions",
    language="azurecli"
)
```

### Language Aliases

Remember language parameter values:
- C#: `csharp` (not "cs" or "c-sharp")
- Azure CLI: `azurecli` (not "azcli" or "bash")
- JavaScript: `javascript` (not "js")
- TypeScript: `typescript` (not "ts")

---

## URL Management Patterns

### Extracting URLs from Search Results

Search results include URLs. Always extract and evaluate before fetching.

**URL Quality Indicators:**
1. **learn.microsoft.com** > docs.microsoft.com (newer platform)
2. **/azure/** paths for Azure-specific
3. **/dotnet/** paths for .NET-specific
4. **/ef/** paths for Entity Framework
5. URL structure indicates content depth

**Example URL Analysis:**
```
High-value URLs (fetch these):
✅ learn.microsoft.com/azure/architecture/guide/...
   → Comprehensive guide

✅ learn.microsoft.com/aspnet/core/tutorials/...
   → Complete tutorial

Medium-value URLs (fetch if needed):
⚠️ learn.microsoft.com/azure/service-name/quickstart
   → Quick start, may lack depth

⚠️ learn.microsoft.com/dotnet/api/...
   → API reference, specific to one type

Low-value URLs (search usually sufficient):
❌ learn.microsoft.com/azure/service-name/overview
   → Overview, search excerpts likely enough
```

### URL Construction for Direct Fetch

When you know the URL pattern, construct it directly:

**Pattern Recognition:**
```
Azure services:
learn.microsoft.com/azure/[service-name]/[topic]

.NET APIs:
learn.microsoft.com/dotnet/api/[namespace].[class]

ASP.NET Core:
learn.microsoft.com/aspnet/core/[area]/[topic]

Entity Framework:
learn.microsoft.com/ef/core/[topic]
```

**Example:**
```
Known: Need EF Core migration documentation
Construct: learn.microsoft.com/ef/core/managing-schemas/migrations/

Direct fetch:
microsoft_docs_fetch("https://learn.microsoft.com/ef/core/managing-schemas/migrations/")
```

---

## Error Handling & Recovery

### Common Error Scenarios

**1. No Results from Search**
```
Problem: Query too specific or uses wrong terminology

Recovery:
1. Broaden query terms
2. Remove version numbers (try more generic)
3. Use alternative terminology
4. Search for related concepts first
```

**2. Fetch Returns 404**
```
Problem: URL invalid or page moved

Recovery:
1. Search for the topic to find current URL
2. Check if URL format changed (docs.microsoft.com → learn.microsoft.com)
3. Remove URL fragments (#anchors)
4. Navigate to parent path
```

**3. Too Many Irrelevant Results**
```
Problem: Query too broad

Recovery:
1. Add more specific terms
2. Include version numbers
3. Add context keywords
4. Use language parameter for code searches
```

**4. Results Missing Expected Information**
```
Problem: Search excerpts don't contain needed details

Recovery:
1. Identify best URL from search results
2. Use microsoft_docs_fetch on that URL
3. If still insufficient, search for more specific sub-topics
```

### Retry Strategy

When initial query fails, follow this escalation:

```
Level 1: Adjust Query
- Rephrase with different keywords
- Add/remove specificity
- Include/exclude version numbers

Level 2: Change Tool
- If search fails → try code sample search
- If code search fails → try regular search
- If both fail → fetch known documentation areas

Level 3: Divide and Conquer
- Break complex query into smaller parts
- Search each component separately
- Synthesize results
```

---

## Real-Time Documentation Access

### Advantages of Real-Time Access

The microsoft-docs tools provide **current** documentation, superior to LLM training data for:

**Recently Released Features:**
- .NET 9 features (released November 2024)
- C# 14 syntax (released 2024)
- Azure services in preview
- Updated API versions

**Deprecated Features:**
- Can identify when features are deprecated
- Find migration paths
- Avoid obsolete approaches

**Best Practices Evolution:**
- Current recommendations may differ from training data
- Security best practices update frequently
- Performance optimization techniques improve

### When Training Data May Be Sufficient

LLM knowledge adequate for:
- Stable, long-term APIs (e.g., basic LINQ)
- Fundamental concepts (e.g., dependency injection basics)
- Historical context
- General architectural patterns

microsoft-docs tools essential for:
- Specific API versions
- New features (<1 year old)
- Configuration options
- Exact syntax
- Complete parameter lists
- Current error codes

---

## Performance Optimization Checklist

Before making a tool call, verify:

### Pre-Search Checklist
- [ ] Query includes specific technology names
- [ ] Version numbers included if relevant
- [ ] Action verbs used for implementation queries
- [ ] Query is 5-15 words (not too short, not too long)

### Pre-Code-Sample-Search Checklist
- [ ] Language parameter specified (if known)
- [ ] Query describes implementation need clearly
- [ ] Framework/SDK name included
- [ ] Operation/method name included if known

### Pre-Fetch Checklist
- [ ] URL validated from previous search results
- [ ] Confirmed URL is from microsoft.com domain
- [ ] Verified search results were insufficient
- [ ] Determined complete page is needed (not just excerpts)
- [ ] Context window has sufficient remaining tokens

### Post-Results Checklist
- [ ] Results evaluated for relevance
- [ ] Determined if additional searches needed
- [ ] Identified URLs worth fetching
- [ ] Decided if code samples would help

---

## Token Efficiency Metrics

Understand token costs to optimize usage:

### Cost per Tool Call

**microsoft_docs_search:**
- Input: ~100-200 tokens (query + parameters)
- Output: ~2,000-5,000 tokens (10 results × 200-500 tokens each)
- **Total: ~2,100-5,200 tokens per search**

**microsoft_code_sample_search:**
- Input: ~100-200 tokens (query + parameters)
- Output: ~3,000-8,000 tokens (20 samples, variable length)
- **Total: ~3,100-8,200 tokens per search**

**microsoft_docs_fetch:**
- Input: ~100-200 tokens (URL parameter)
- Output: ~5,000-30,000 tokens (complete page, highly variable)
- **Total: ~5,100-30,200 tokens per fetch**

### ROI Analysis

**High ROI:**
- microsoft_docs_search for quick lookups (2-5k tokens → immediate answer)
- microsoft_code_sample_search for implementation (3-8k tokens → working code)

**Medium ROI:**
- microsoft_docs_fetch for tutorials (10-20k tokens → complete understanding)

**Low ROI:**
- microsoft_docs_fetch when search would suffice (20k+ tokens for info available in 5k search)
- Multiple searches for same information (could have fetched once)

### Cost Optimization Rules

**Rule 1:** Always start with search, never fetch first
**Rule 2:** Use language parameter to reduce code sample tokens
**Rule 3:** Fetch maximum 3-4 pages per complex task
**Rule 4:** Prefer code sample search over fetch for examples
**Rule 5:** Cache knowledge from fetched pages in conversation context

---

## Advanced Patterns

### Pattern 1: Hierarchical Search Refinement
```
Level 1 (Broad):
microsoft_docs_search("Azure messaging services")

Level 2 (Narrow):
microsoft_docs_search("Azure Service Bus topics")

Level 3 (Specific):
microsoft_docs_search("Azure Service Bus dead letter queue handling")

Level 4 (Implementation):
microsoft_code_sample_search("service bus dead letter queue", language="csharp")
```

### Pattern 2: Cross-Reference Validation
```
Source 1:
microsoft_docs_search("feature X recommended approach")

Source 2:
microsoft_code_sample_search("feature X implementation")

Validate:
Check if code samples match recommended approach from search
If mismatch → fetch complete documentation
```

### Pattern 3: Version-Aware Migration
```
Step 1 - Old version:
microsoft_docs_search("technology X version Y feature")

Step 2 - New version:
microsoft_docs_search("technology X version Z feature")

Step 3 - Migration guide:
microsoft_docs_search("migrate technology X Y to Z")

Step 4 - Updated examples:
microsoft_code_sample_search("technology X version Z", language="...")
```

### Pattern 4: Multi-Service Integration
```
For each service:
1. microsoft_docs_search("[service] integration overview")
2. microsoft_code_sample_search("[service A] to [service B] integration")

Then:
3. microsoft_docs_fetch([complete integration guide URL])
```

---

## Skill Mastery Indicators

You've mastered this skill when you can:

1. **Choose the right tool** for any documentation need instantly
2. **Formulate optimal queries** that return relevant results on first try
3. **Manage token budget** by using search before fetch
4. **Extract URLs efficiently** from search results for targeted fetching
5. **Use language parameters** automatically for code searches
6. **Recognize when to escalate** from search to fetch
7. **Optimize workflows** by using appropriate tool combinations
8. **Handle errors gracefully** with effective retry strategies
9. **Balance comprehensiveness with efficiency** in documentation retrieval

Mastery Checklist:
- [ ] Can identify which tool to use without consulting reference
- [ ] Queries return relevant results >80% of time on first try
- [ ] Use fewer than 3 fetches for typical implementation tasks
- [ ] Always include language parameter for code searches
- [ ] Extract and evaluate URLs before fetching
- [ ] Recognize when search results are sufficient vs. need fetch
- [ ] Can construct common Microsoft Learn URLs directly
- [ ] Understand token costs and optimize accordingly
