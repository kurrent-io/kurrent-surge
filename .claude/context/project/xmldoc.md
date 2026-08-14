# XML Documentation Standards

Guidelines for writing XML documentation comments in the Kurrent SDK.

**Scope:** All C# source files in `src/`. Generated files (`*.g.cs`, `/obj/`, `/bin/`) and source generator projects
(`netstandard2.0`) are exempt.

> This document covers **XML documentation on types and members**. For implementation-detail comments
> (the `// why` comments inside method bodies), see the Code Comments section in the coding conventions.

## Contents

- [General Principles](#general-principles) — public vs internal scope, quality standards, good vs poor docs
- [XML Documentation Tags](#xml-documentation-tags) — required (`<summary>`, `<param>`, `<returns>`), conditional, and
  banned tags
- [Templates by Construct Type](#templates-by-construct-type) — classes, interfaces, methods, properties, constructors,
  events, enums, records, internal members
- [Common Patterns in Kurrent SDK](#common-patterns-in-kurrent-sdk) — client classes, C# 14 extension blocks,
  configuration options
- [Best Practices](#best-practices) — DO and DON'T with examples
- [Special Cases](#special-cases) — generics, obsolete, experimental, IVariant
- [Validation](#validation) — pre-commit checklist
- [Resources](#resources) — Microsoft XML doc references

## General Principles

### Public vs Internal APIs

**Public APIs require complete, terse documentation:**

- All public classes, interfaces, methods, properties, and events must be documented
- `<summary>` (one sentence), `<param>`/`<returns>` on all members that have them, `<exception>` where thrown
- `<remarks>` only for hazards and contracts — see the remarks bar under Conditional Tags
- This documentation generates IntelliSense tooltips for SDK consumers

**Internal and private APIs are documented only when non-obvious:**

- A well-named member needs no docs — leave it bare
- When the name cannot carry the meaning, add a one-sentence `<summary>` and nothing else
- Never `<param>`, `<returns>`, `<remarks>`, or `<exception>` on internal members
- A `<see cref>` inside that summary sentence is allowed

### Quality Standards

Good XML documentation:

1. **Is complete** — all public API surface is documented
2. **Is terse** — one-sentence summaries, one-line params; verbose docs stop being read
3. **Is clear** — plain language that developers can understand
4. **Is accurate** — matches actual behavior
5. **States current behavior** — never history, rationale, or causality between components
6. **Stands alone** — a member's docs describe that member, not what other components do

### What Makes Good Documentation

**Good documentation:**

- Describes behavior, not implementation
- Says everything once, in one sentence where one sentence is enough
- Documents exceptions that can be thrown
- Names the replacement for deprecated APIs

**Poor documentation:**

- Simply restates the member name ("Gets or sets the name")
- Narrates history or design rationale ("does X because Y now does Z")
- Explains a member by describing another component's behavior
- Missing parameter descriptions
- Inconsistent terminology

## XML Documentation Tags

### Required Tags

#### `<summary>`

Brief description of the type or member. One sentence — behavior, not implementation. Always the multiline form —
never `<summary>text</summary>` on one line.

```csharp
/// <summary>
/// Represents the name of a stream in KurrentDB.
/// </summary>
```

#### `<param>`

Describes each parameter. Required for all public method/constructor parameters — partial parameter docs raise CS1573.
One line each, canonical phrasing; never multi-sentence prose.

```csharp
/// <param name="stream">The name of the stream to append to.</param>
/// <param name="cancellationToken">The cancellation token.</param>
```

#### `<returns>`

Describes the return value. Required for all public methods that return a value (except `void`).

```csharp
/// <returns>The service collection for method chaining.</returns>
```

### Conditional Tags

#### `<remarks>`

Hazards and contracts only: threading/concurrency, lifetime and disposal, ordering constraints, side effects,
non-obvious defaults, failure semantics, and a pointer to the member's companion type — a type the caller must use
with the member and that the signature does not already show. No tutorial-style usage guidance beyond that pointer, no
elaboration of the summary. A member that has none of these gets no `<remarks>`.

```csharp
/// <remarks>
/// Not thread-safe. Create one instance per consumer, or synchronize access externally.
/// </remarks>

/// <remarks>
/// Use <see cref="ExpectedStreamState"/> to control optimistic concurrency.
/// </remarks>
```

#### `<exception>`

Documents exceptions that can be thrown. Include for all public methods that throw.

```csharp
/// <exception cref="KurrentClientNotConfiguredException">
/// Thrown on first resolution when neither a registered <see cref="KurrentClientOptions"/> nor
/// a <c>ConnectionStrings:KurrentDB</c> entry can be found.
/// </exception>
/// <exception cref="InvalidOperationException">
/// A checkpoint has already been requested for this record.
/// </exception>
```

#### `<value>`

Describes the meaning of a property value when the summary cannot carry it.

```csharp
/// <value>
/// A dictionary where the key is the partition and the value is a
/// <see cref="PartitionProgress"/> containing the tracked and flushed positions.
/// </value>
```

#### `<list>`

Creates lists in XML documentation. Use `type="bullet"` for bulleted lists, `type="number"` for numbered lists.

**Bulleted list:**

```csharp
/// <list type="bullet">
///   <item><description><see cref="MessageTypeMapper"/> — assigned to <c>Schema.Mapper</c></description></item>
///   <item><description><see cref="ILoggerFactory"/> — assigned to <c>LoggerFactory</c></description></item>
/// </list>
```

**Numbered list:**

```csharp
/// <list type="number">
///   <item><description>A <see cref="KurrentClientOptions"/> instance registered in the service provider.</description></item>
///   <item><description>A connection string at <c>ConnectionStrings:KurrentDB</c> in <see cref="IConfiguration"/>.</description></item>
/// </list>
```

**List with descriptions:**

```csharp
/// <list type="bullet">
///   <item>
///     <term>First</term>
///     <description>"account-9E763770-0A8D" → category "account"</description>
///   </item>
///   <item>
///     <term>Last</term>
///     <description>"shopping-cart-1" → category "shopping-cart"</description>
///   </item>
/// </list>
```

**Important:** Do not use markdown-style lists with hyphens (`-`) or asterisks (`*`) in XML documentation — they will
not render correctly. Always use the `<list>` tag.

### Banned Tags

- `<example>` — banned everywhere. Usage examples live in the docs site and READMEs, not IntelliSense.
- `<seealso>` — banned everywhere. Cross-references are actionable-only — see Best Practices.

## Templates by Construct Type

### Classes

```csharp
/// <summary>
/// Represents [what this class models/encapsulates].
/// </summary>
public class ResourceName { }
```

**Example:**

```csharp
/// <summary>
/// Represents a KurrentDB stream processing unit with checkpoint support.
/// </summary>
public class KurrentProcessor { }
```

### Interfaces

```csharp
/// <summary>
/// Defines [the contract/behavior this interface requires].
/// </summary>
public interface IInterfaceName { }
```

**Example:**

```csharp
/// <summary>
/// Defines the contract for processing stream records one at a time.
/// </summary>
public interface IRecordProcessor { }
```

### Methods

```csharp
/// <summary>
/// [Action this method performs] [on/for what].
/// </summary>
/// <param name="paramName">One-line description.</param>
/// <returns>Description of what is returned.</returns>
/// <exception cref="ExceptionType">When this exception is thrown.</exception>
public ReturnType MethodName(Type paramName) { }
```

**Example:**

```csharp
/// <summary>
/// Appends one or more records to a stream.
/// </summary>
/// <param name="stream">The name of the stream to append to.</param>
/// <param name="records">The records to append.</param>
/// <returns>The result of the append operation, including the new stream revision.</returns>
/// <remarks>
/// Use <see cref="ExpectedStreamState"/> to control optimistic concurrency.
/// </remarks>
public ValueTask<AppendRecordsResult> AppendRecords(
    string stream,
    IEnumerable<RecordToWrite> records) { }
```

### Properties

```csharp
/// <summary>
/// Gets [or sets] [what this property represents].
/// </summary>
/// <value>
/// [Valid values and their meaning — only when the summary cannot carry them]
/// </value>
public Type PropertyName { get; set; }
```

**Example:**

```csharp
/// <summary>
/// Gets the address of the KurrentDB cluster.
/// </summary>
/// <value>
/// For direct connections, the single endpoint URI. For discovery mode, a synthetic discovery URI.
/// </value>
public Uri Address { get; }
```

### Constructors

```csharp
/// <summary>
/// Initializes a new instance of the <see cref="ClassName"/> class.
/// </summary>
/// <param name="paramName">Description of parameter.</param>
/// <exception cref="ExceptionType">When this exception is thrown.</exception>
public ClassName(Type paramName) { }
```

**Example:**

```csharp
/// <summary>
/// Initializes a new instance of the <see cref="KurrentException"/> class.
/// </summary>
/// <param name="code">The error code identifying the failure category.</param>
/// <param name="reason">A human-readable description of the error.</param>
/// <param name="innerException">The exception that caused this error, or <c>null</c> if none.</param>
public KurrentException(ErrorCode code, string reason, Exception? innerException = null) { }
```

### Events

```csharp
/// <summary>
/// Occurs when [condition that triggers the event].
/// </summary>
public event EventHandler? EventName;
```

### Enums

```csharp
/// <summary>
/// Specifies [what this enum represents/controls].
/// </summary>
public enum EnumName {
    /// <summary>
    /// [Description of this enum value].
    /// </summary>
    Value1,

    /// <summary>
    /// [Description of this enum value].
    /// </summary>
    Value2
}
```

### Records (Value Types)

```csharp
/// <summary>
/// [What this value type represents].
/// </summary>
/// <param name="ParamName">Description of the constructor parameter.</param>
public readonly record struct TypeName(Type ParamName) { }
```

**Example:**

```csharp
/// <summary>
/// Represents a position within a stream, combining the stream name and revision number.
/// </summary>
/// <param name="Stream">The name of the stream.</param>
/// <param name="Revision">The revision number within the stream.</param>
public readonly record struct RecordPosition(StreamName Stream, ulong Revision) { }
```

### Internal and Private Members

No docs by default. When the name cannot carry the meaning, one line:

```csharp
// GOOD — bare when the name says it all
internal static StreamName Normalize(StreamName stream) { }

/// <summary>
/// Caches resolved endpoints per node until the next gossip refresh.
/// </summary>
internal sealed class EndpointCache { }
```

## Common Patterns in Kurrent SDK

### Client Classes

Client classes provide access to a specific KurrentDB module (streams, registry, connectors, etc.).

```csharp
/// <summary>
/// Provides operations for [module description] in KurrentDB.
/// </summary>
public class ModuleClient { }
```

### Extension Methods — C# 14 `extension(Type)` Blocks

Extension methods use C# 14 `extension(Type)` blocks. The class-level summary documents the extension group; individual
members get their own XML docs inside the block.

```csharp
/// <summary>
/// Extensions for <see cref="IServiceCollection"/>.
/// </summary>
public static class KurrentClientServiceCollectionExtensions {
    /// <param name="services">The service collection.</param>
    extension(IServiceCollection services) {
        /// <summary>
        /// Registers a <see cref="KurrentClient"/> with the service collection.
        /// </summary>
        /// <param name="connectionString">The KurrentDB connection string.</param>
        /// <returns>The service collection for method chaining.</returns>
        public IServiceCollection AddKurrentClient(string connectionString) { }
    }
}
```

### Fluent Builders

Fluent builders extend `FluentBuilder<TBuilder, TResult>` and expose chainable methods that accumulate transformations
via `With(r => r with { ... })`.

```csharp
/// <summary>
/// Fluent builder for constructing <see cref="ResultType"/> instances.
/// </summary>
public class Builder : FluentBuilder<Builder, ResultType> {
    /// <summary>
    /// Sets [what this method configures].
    /// </summary>
    public Builder MethodName(Type value) =>
        With(r => r with { Property = value });
}
```

**Example:**

```csharp
/// <summary>
/// Fluent builder for constructing <see cref="ReadRecordsRequest"/> instances.
/// </summary>
public class Builder : FluentBuilder<Builder, ReadRecordsRequest> {
    /// <summary>
    /// Sets the starting position for the read operation in the global log.
    /// </summary>
    public Builder FromPosition(LogPosition position) =>
        With(r => r with { Start = position });

    /// <summary>
    /// Limit the number of records returned.
    /// </summary>
    public Builder Limit(long maxRecords) =>
        With(x => x with { Limit = maxRecords });
}
```

## Best Practices

### DO

- **Use consistent terminology** throughout the codebase
  - "stream" not "topic" or "queue"
  - "record" not "event" (in V2 API context)
  - "processor" not "consumer" or "subscriber"
  - "drone" for running processor instances
  - "checkpoint" not "offset" or "cursor"
  - "position" not "offset" or "sequence number"
  - "connection string" not "URI" or "address" (for `kurrentdb://` strings)

- **Start with a verb for methods**
  - "Appends records to a stream..."
  - "Configures the processor..."
  - "Gets the connection string..."

- **Use present tense for properties**
  - "Gets the endpoint..." not "Will get the endpoint..."
  - "Represents the configuration..." not "Will represent..."

- **Include the `<paramref>` tag when referring to parameters in text:**
  ```csharp
  /// <param name="name">The name of the stream.</param>
  /// <remarks>
  /// The <paramref name="name"/> must be unique within the application model.
  /// </remarks>
  ```

- **Use `<see cref="">` only when the reader must go there** — the replacement for an obsolete member, a companion
  type (one the caller must use with the member and that the signature does not already show), the type an extension
  block extends. Everything else is noise. API names always take `<see cref>`, never `<c>` — `<c>` is for literals and
  keywords (`null`, `true`), not for API members.

- **Document default values:**
  ```csharp
  /// <param name="timeout">The timeout in seconds. Defaults to 30 seconds if not specified.</param>
  ```

- **Explain null behavior:**
  ```csharp
  /// <returns>
  /// The stream position if found; otherwise, <c>null</c>.
  /// </returns>
  ```

### DON'T

- **Don't just restate the member name:**
  ```csharp
  // BAD
  /// <summary>
  /// Gets the stream name.
  /// </summary>
  public StreamName StreamName { get; }

  // GOOD
  /// <summary>
  /// Gets the name of the stream this record was appended to.
  /// </summary>
  public StreamName StreamName { get; }
  ```

- **Don't use implementation details in documentation:**
  ```csharp
  // BAD
  /// <summary>
  /// Sends AppendRecords RPC via the V2 gRPC channel.
  /// </summary>

  // GOOD
  /// <summary>
  /// Appends one or more records to a stream.
  /// </summary>
  ```

- **Don't leave placeholder or TODO comments:**
  ```csharp
  // BAD
  /// <summary>
  /// TODO: Add documentation
  /// </summary>
  ```

- **Don't use abbreviations without explanation:**
  ```csharp
  // BAD
  /// <summary>
  /// Gets the conn str for KDB.
  /// </summary>

  // GOOD
  /// <summary>
  /// Gets the connection string for KurrentDB.
  /// </summary>
  ```

- **Don't use markdown-style lists in XML documentation:**
  ```csharp
  // BAD — markdown lists don't render in XML docs
  /// Supports:
  /// - Option 1
  /// - Option 2

  // GOOD — use XML list tags
  /// <list type="bullet">
  /// <item>Option 1</item>
  /// <item>Option 2</item>
  /// </list>
  ```

- **Don't document internal APIs that need no explanation:**
  ```csharp
  // BAD — the name already says all of this
  /// <summary>
  /// Provides utilities for managing gRPC channel lifecycle.
  /// </summary>
  /// <remarks>
  /// <para>
  /// This class handles connection pooling, reconnection logic,
  /// and TLS certificate rotation...
  /// </para>
  /// </remarks>
  internal class ChannelManager

  // GOOD — bare, or one line when the name cannot carry it
  internal class ChannelManager
  ```

- **Don't write changelog or causality docs.** When behavior changes, rewrite the docs as if the new behavior had always
  been true — the reader has no old version to compare against; git holds the history:
  ```csharp
  // BAD — narrates another component's behavior and the design's history
  /// <summary>
  /// Appends records. Validation now happens here because SchemaGovernance
  /// no longer validates on read.
  /// </summary>

  // BAD — change narration; state only what IS
  /// <summary>
  /// Reads records. Previously returned an empty batch; now throws.
  /// </summary>

  // GOOD — states what this member does
  /// <summary>
  /// Appends one or more records to a stream.
  /// </summary>
  ```

- **Don't add remarks that elaborate instead of warn:**
  ```csharp
  // BAD — restates and pads the summary
  /// <remarks>
  /// This method is useful when you want to append records to a stream.
  /// </remarks>

  // GOOD — remarks carry a hazard or contract, or don't exist
  /// <remarks>
  /// The returned session must be disposed before the client is disposed.
  /// </remarks>
  ```

- **Don't include HTML tags** (except `<c>`, `<code>`, `<para>` which are standard XML doc tags)

## Special Cases

### Generic Type Parameters

```csharp
/// <typeparam name="T">The type of the variant case.</typeparam>
```

### Obsolete APIs

```csharp
/// <summary>
/// Gets the connection string.
/// </summary>
/// <remarks>
/// This property is obsolete. Use <see cref="ConnectionStringExpression"/> instead.
/// </remarks>
[Obsolete("Use ConnectionStringExpression instead.")]
public string ConnectionString { get; }
```

### Experimental APIs

```csharp
/// <summary>
/// Configures the processor to use batched dispatch (preview).
/// </summary>
/// <remarks>
/// This is an experimental feature and may change in future releases.
/// </remarks>
[Experimental("KURRENT001")]
public static KurrentProcessor CreateBatched(...) { }
```

### IVariant Discriminated Unions

Source-generated `IVariant` types need documentation on the base interface and each variant case. The source generator
produces implicit conversion operators — those don't need manual XML docs.

```csharp
/// <summary>
/// Represents the result of a stream consistency check.
/// </summary>
[Variant]
public partial interface IConsistencyCheck {
    /// <summary>
    /// The stream exists with the expected state.
    /// </summary>
    record Satisfied;

    /// <summary>
    /// The stream state does not match the expected state.
    /// </summary>
    record Violated(ConsistencyViolation Violation);
}
```

## Validation

Before committing code with XML documentation:

1. **Build the project** — XML doc warnings are errors
2. **Check IntelliSense** — hover over APIs in an IDE to see how the documentation appears
3. **Review consistency** — ensure terminology and style match existing documentation
4. **Spell check** — use a spell checker on documentation comments
5. **Run API documentation tools** — if available, generate HTML docs to review formatting

## Resources

- [C# XML Documentation Comments (Microsoft Docs)](https://learn.microsoft.com/dotnet/csharp/language-reference/xmldoc/)
- [Recommended XML Tags (Microsoft
  Docs)](https://learn.microsoft.com/dotnet/csharp/language-reference/xmldoc/recommended-tags)
- [.NET API Documentation Guidelines](https://github.com/dotnet/dotnet-api-docs/wiki)
