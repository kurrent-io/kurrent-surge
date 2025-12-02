# Microsoft Docs MCP Server - Tool Reference

Complete reference documentation for all microsoft-docs MCP server tools.

---

## Tool Overview

The Microsoft Learn MCP Server provides three complementary tools that form a powerful documentation retrieval system:

1. **microsoft_docs_search** - Semantic search for quick discovery
2. **microsoft_code_sample_search** - Code-specific search with language filtering
3. **microsoft_docs_fetch** - Full document retrieval for deep dives

---

## microsoft_docs_search

### Purpose
Perform semantic vector search across Microsoft's technical documentation to quickly find relevant content chunks.

### Parameters

| Parameter | Type   | Required | Description                                       |
|-----------|--------|----------|---------------------------------------------------|
| query     | string | Yes      | Search query describing what to find              |
| question  | string | No       | DEPRECATED - use `query` instead                  |

**Important:** The `question` parameter is deprecated. Always use `query` parameter.

### Returns
- Up to 10 high-quality content chunks (each max 500 tokens)
- Each result includes:
  - Article title
  - URL (microsoft.com domain)
  - Self-contained content excerpt optimized for fast retrieval

### Best Use Cases
1. **Quick reference lookups** - Finding specific APIs, commands, or configuration options
2. **Overview gathering** - Getting initial context before deep diving
3. **Feature verification** - Checking if a capability exists
4. **Troubleshooting** - Finding error messages or common issues
5. **API discovery** - Locating relevant endpoints or methods

### Query Optimization Tips

**Be Specific:**
- ✅ Good: "Azure CLI commands to create Container App with managed identity"
- ❌ Poor: "Azure containers"

**Include Technology Context:**
- ✅ Good: "implement IHttpClientFactory in .NET 8 minimal API"
- ❌ Poor: "http client factory"

**Use Technical Terms:**
- ✅ Good: "gpt-4.1-mini availability in EU regions"
- ❌ Poor: "where can I use the new model"

**Specify Versions When Relevant:**
- ✅ Good: "ASP.NET Core 9.0 authentication middleware"
- ❌ Poor: "ASP.NET authentication"

### Example Queries
```
"Azure Functions Durable Functions orchestration patterns"
"Entity Framework Core 9 complex type support"
"Azure OpenAI Service GPT-4 Turbo token limits"
"C# 14 primary constructors syntax"
"Microsoft Graph API batch requests"
"Azure Cosmos DB partition key best practices"
"SignalR Core connection management"
"Blazor WebAssembly authentication with Azure AD"
```

### When NOT to Use
- When you need the **complete** documentation page (use `microsoft_docs_fetch`)
- When you need **code examples specifically** (use `microsoft_code_sample_search`)
- When search results are insufficient and you know the exact URL (use `microsoft_docs_fetch`)

---

## microsoft_code_sample_search

### Purpose
Search for official Microsoft code snippets and examples with optional language-specific filtering.

### Parameters

| Parameter | Type   | Required | Description                                              |
|-----------|--------|----------|----------------------------------------------------------|
| query     | string | Yes      | Search query describing the code sample needed           |
| language  | string | No       | Programming language filter                              |

### Supported Languages
Eligible values for `language` parameter:
- `csharp`
- `javascript`
- `typescript`
- `python`
- `powershell`
- `azurecli`
- `al`
- `sql`
- `java`
- `kusto`
- `cpp`
- `go`
- `rust`
- `ruby`
- `php`

### Returns
- Up to 20 relevant, high-quality code samples
- Each result includes:
  - Code snippet
  - Context/description
  - Source documentation URL

### Best Use Cases
1. **Implementation examples** - Getting runnable code for specific features
2. **Best practices** - Seeing recommended patterns from Microsoft
3. **Syntax reference** - Finding correct API usage
4. **Integration patterns** - Discovering how to combine services
5. **Migration examples** - Finding modern equivalents of legacy code

### Query Optimization Tips

**Include Implementation Context:**
- ✅ Good: "harms evaluation using azure ai foundry evaluation sdk"
- ❌ Poor: "harms evaluation"

**Specify the Operation:**
- ✅ Good: "create Azure Container App with managed identity using Azure CLI"
- ❌ Poor: "Azure Container App"

**Be Framework-Specific:**
- ✅ Good: "Entity Framework Core complex type mapping"
- ❌ Poor: "database mapping"

**Use Language Filter When Possible:**
- ✅ Good: `query="authentication middleware"` + `language="csharp"`
- ❌ Poor: `query="csharp authentication middleware"` (language in query text)

### Example Queries

**Without Language Filter:**
```
"Azure OpenAI streaming completion"
"deploy Azure Functions from GitHub Actions"
"Microsoft Graph batch API usage"
```

**With Language Filter:**
```python
# Python examples
query="azure storage blob upload", language="python"
query="cosmos db query optimization", language="python"

# C# examples
query="dependency injection configuration", language="csharp"
query="minimal API endpoint filters", language="csharp"

# TypeScript examples
query="azure static web apps authentication", language="typescript"
query="react app azure ad b2c", language="typescript"
```

### When NOT to Use
- When you need **conceptual documentation** (use `microsoft_docs_search`)
- When you have a specific documentation URL (use `microsoft_docs_fetch`)
- When you need **full context** around examples (use `microsoft_docs_fetch` after finding URL)

---

## microsoft_docs_fetch

### Purpose
Retrieve and convert a complete Microsoft documentation page to markdown format for comprehensive analysis.

### Parameters

| Parameter | Type   | Required | Description                                    |
|-----------|--------|----------|------------------------------------------------|
| url       | string | Yes      | URL of Microsoft documentation page to retrieve|

### URL Requirements
- Must be a valid link from the microsoft.com domain
- Typically from:
  - learn.microsoft.com
  - docs.microsoft.com (legacy)
  - azure.microsoft.com/documentation

### Returns
Complete markdown-formatted documentation page including:
- Headings and structure
- Code blocks with syntax highlighting preserved
- Tables
- Links
- Inline formatting

### Best Use Cases
1. **Deep learning** - Understanding complete concepts end-to-end
2. **Tutorial completion** - Following step-by-step guides
3. **Troubleshooting sections** - Reading all diagnostic information
4. **Prerequisites verification** - Checking all requirements
5. **Complete API reference** - Getting all methods/properties of a type
6. **Configuration options** - Reviewing all available settings

### Workflow Integration

**Sequential Pattern:**
```
1. Use microsoft_docs_search to find relevant pages
2. Identify high-value URLs in search results
3. Use microsoft_docs_fetch to retrieve complete content
```

**Example Flow:**
```
Step 1 (Search):
Query: "Azure Functions Durable Functions orchestration patterns"
→ Returns: 10 content chunks with URLs

Step 2 (Analyze):
Identify most relevant URL from results:
https://learn.microsoft.com/azure/azure-functions/durable/...

Step 3 (Fetch):
url="https://learn.microsoft.com/azure/azure-functions/durable/..."
→ Returns: Complete documentation page in markdown
```

### When to Use Fetch Over Search

Use `microsoft_docs_fetch` when:
- Search results provide **incomplete information**
- Need **step-by-step procedures**
- Require **troubleshooting sections**
- Need **full context** for a feature
- Working through **tutorials**
- Verifying **all configuration options**
- Reading **comprehensive guides**

Continue with `microsoft_docs_search` when:
- Need **quick reference**
- Looking for **multiple perspectives**
- Exploring **related topics**
- Initial **discovery phase**

### Example Usage Patterns

**Pattern 1: Search → Fetch**
```
Search: "Entity Framework Core 9 complex types"
→ Find URL in results
Fetch: https://learn.microsoft.com/ef/core/what-is-new/ef-core-9.0/whatsnew#complex-types
→ Get complete implementation guide
```

**Pattern 2: Direct Fetch (Known URL)**
```
Fetch: https://learn.microsoft.com/azure/architecture/best-practices/
→ Review complete best practices guide
```

**Pattern 3: Verification Flow**
```
Search: "IHttpClientFactory .NET 8 minimal API"
→ Get overview
Fetch: [URL from search results]
→ Verify complete implementation details
```

---

## Query Strategy Matrix

| Need                          | Primary Tool                    | Secondary Tool              | Tertiary Tool              |
|-------------------------------|---------------------------------|-----------------------------|----------------------------|
| Quick API reference           | microsoft_docs_search           | -                           | -                          |
| Code implementation           | microsoft_code_sample_search    | microsoft_docs_search       | microsoft_docs_fetch       |
| Complete tutorial             | microsoft_docs_fetch            | -                           | -                          |
| Feature discovery             | microsoft_docs_search           | microsoft_code_sample_search| -                          |
| Troubleshooting               | microsoft_docs_search           | microsoft_docs_fetch        | -                          |
| Best practices                | microsoft_code_sample_search    | microsoft_docs_fetch        | -                          |
| Configuration options         | microsoft_docs_search           | microsoft_docs_fetch        | -                          |
| Architectural guidance        | microsoft_docs_fetch            | microsoft_docs_search       | -                          |
| Verification/validation       | microsoft_docs_search           | microsoft_docs_fetch        | microsoft_code_sample_search|

---

## Performance Considerations

### Context Token Management

**Search Results:**
- Returns up to 10 chunks × 500 tokens = ~5,000 tokens
- Efficient for quick lookups
- Minimal context window impact

**Code Sample Search:**
- Returns up to 20 samples
- Variable token count depending on snippet size
- Moderate context window impact

**Fetch Results:**
- Complete documentation page
- Can be very large (10k+ tokens for comprehensive guides)
- Significant context window impact
- **Use strategically** - fetch only when search is insufficient

### Optimal Search-First Strategy

Always start with `microsoft_docs_search` because:
1. Faster results (10 chunks vs full pages)
2. Lower token consumption
3. Provides URLs for targeted fetching
4. Sufficient for 70% of queries

Escalate to `microsoft_docs_fetch` only when:
- Search results are incomplete
- Specific URL identified as highly relevant
- Complete procedure/tutorial needed
- Full context required for implementation

---

## Common Pitfalls & Solutions

### Pitfall 1: Over-fetching
**Problem:** Using `microsoft_docs_fetch` as first resort
**Impact:** Context window bloat, slower performance
**Solution:** Start with `microsoft_docs_search`, fetch only high-value URLs

### Pitfall 2: Vague Queries
**Problem:** Generic search terms like "Azure" or "C#"
**Impact:** Irrelevant results, wasted tool calls
**Solution:** Include specific APIs, versions, features in queries

### Pitfall 3: Ignoring Language Parameter
**Problem:** Not using `language` parameter in code sample search
**Impact:** Results include irrelevant languages
**Solution:** Always specify language when known

### Pitfall 4: Fetching Without Search
**Problem:** Guessing documentation URLs instead of searching
**Impact:** Wrong pages, 404 errors, wasted time
**Solution:** Search first to identify correct URLs

### Pitfall 5: Not Combining Tools
**Problem:** Using only one tool when multiple would be better
**Impact:** Incomplete understanding
**Solution:** Use search → code samples → fetch workflow

---

## Real-World Query Examples

### Scenario 1: Implementing New Feature
```
Goal: Implement Azure OpenAI streaming in Python

Step 1 - Overview:
microsoft_docs_search(query="Azure OpenAI streaming completion python")

Step 2 - Code Examples:
microsoft_code_sample_search(query="azure openai streaming", language="python")

Step 3 - Deep Dive (if needed):
microsoft_docs_fetch(url=[URL from search results])
```

### Scenario 2: Troubleshooting Error
```
Goal: Fix "401 Unauthorized" in Microsoft Graph API

Step 1 - Search Error:
microsoft_docs_search(query="Microsoft Graph API 401 unauthorized authentication")

Step 2 - Complete Troubleshooting Guide:
microsoft_docs_fetch(url=[troubleshooting URL from results])
```

### Scenario 3: Migrating Code
```
Goal: Migrate from HttpClient to IHttpClientFactory

Step 1 - Search Modern Approach:
microsoft_docs_search(query="IHttpClientFactory .NET 8 best practices")

Step 2 - Find Implementation Examples:
microsoft_code_sample_search(query="IHttpClientFactory dependency injection", language="csharp")

Step 3 - Complete Migration Guide:
microsoft_docs_fetch(url=[migration guide URL])
```

### Scenario 4: Architecture Decision
```
Goal: Choose between Azure Functions and Container Apps

Step 1 - Search Comparison:
microsoft_docs_search(query="Azure Functions vs Container Apps comparison")

Step 2 - Deep Dive on Each:
microsoft_docs_fetch(url=[Functions URL])
microsoft_docs_fetch(url=[Container Apps URL])

Step 3 - Best Practices:
microsoft_docs_search(query="Azure Functions best practices")
microsoft_docs_search(query="Azure Container Apps best practices")
```

---

## Tool Reliability Notes

### Content Currency
All tools access the **latest published Microsoft documentation** as it becomes available. This makes them superior to LLM training data for:
- New API versions
- Recent feature announcements
- Current best practices
- Updated configuration options

### Content Quality
All content comes from official Microsoft sources:
- Microsoft Learn (learn.microsoft.com)
- Azure Documentation
- .NET Documentation
- Microsoft 365 Documentation

### Trust Level
**High trust** - All results are from official Microsoft technical documentation, not community content or third-party sources.
