# XML Documentation Standards

Rules for XML documentation comments in the Kurrent SDK.

**Scope:** All C# source files in `src/`. Generated files (`*.g.cs`, `/obj/`, `/bin/`) and source generator projects
(`netstandard2.0`) are exempt. For `// why` comments inside method bodies, see the Code Comments section in the coding
conventions.

## The Six Rules

1. **Docs state current behavior.** Never history, rationale, or causality between components. "Appends one or more
   records to a stream" — not "validation now happens here because the governance layer changed". A doc comment is a
   contract, not a changelog — and a contract matches what the code actually does. When behavior changes, rewrite the
   docs as if the new behavior had always been true — the reader has no old version to compare against; git holds the
   history.

2. **Public members get complete, terse docs.** One-sentence `<summary>`, always in the multiline form — never
   `<summary>text</summary>` on one line. `<param>`/`<returns>` on everything that has them, one line each — partial
   parameter docs raise CS1573. `<exception>` for every exception thrown. Overrides and interface implementations
   use `<inheritdoc/>` — it counts as documented. Verbose docs go unread.

3. **`<remarks>` earns its place or is absent.** Permitted content: threading/concurrency, lifetime and disposal,
   ordering constraints, side effects, non-obvious defaults, failure semantics, and a pointer to the member's
   companion type as rule 5 defines it (`Use <see cref="CompanionType"/> to control [behavior].`). No tutorial-style
   usage guidance beyond that pointer. No elaboration of the summary. Most members have no remarks.

4. **Internal and private members get docs only when non-obvious.** A well-named member stays bare. When the name cannot
   carry the meaning, a one-sentence `<summary>` — never `<param>`, `<returns>`, `<remarks>`, or `<exception>`. A
   `<see cref>` inside that sentence is allowed.

5. **Cross-references are actionable-only.** `<see cref>` only when the reader must go there: the replacement for an
   obsolete member, a companion type, the type an extension block extends, the standard constructor boilerplate. A
   companion type is one the caller must use with this member and that the signature does not already show —
   `ExpectedStreamState` for an append. `<seealso>` is banned. API names always take `<see cref>`, never `<c>` —
   `<c>` is for literals and keywords (`null`, `true`). A member's docs describe that member — the fewer pointers to
   other components, the better.

6. **`<example>` is banned.** Usage examples live in the docs site and READMEs, not IntelliSense.

## Tags

| Tag                                 | Public                           | Internal              | Content                                                                    |
|-------------------------------------|----------------------------------|-----------------------|----------------------------------------------------------------------------|
| `<summary>`                         | Required                         | Only when non-obvious | One sentence. Behavior, not implementation. Always multiline form.         |
| `<param>`                           | Required on all                  | Never                 | One line, canonical phrasing.                                              |
| `<returns>`                         | Required (non-void)              | Never                 | One line. Include the `null` case.                                         |
| `<exception>`                       | Required where thrown            | Never                 | The condition that triggers it.                                            |
| `<typeparam>`                       | Required                         | Never                 | One line.                                                                  |
| `<inheritdoc/>`                     | On overrides + implementations   | Fine                  | Replaces the doc block; counts as documented.                              |
| `<remarks>`                         | Hazards + contracts only         | Never                 | See rule 3.                                                                |
| `<value>`                           | Only when summary can't carry it | Never                 | Valid values and their meaning.                                            |
| `<see cref>`                        | Actionable-only                  | Allowed in summary    | See rule 5.                                                                |
| `<paramref>`                        | Fine                             | Allowed in summary    | When referring to a parameter in text.                                     |
| `<c>`, `<code>`, `<para>`, `<list>` | Fine                             | Allowed in summary    | Formatting. Use `<list>`, never markdown dashes — they don't render. No other HTML. |
| `<example>`                         | Banned                           | Banned                | —                                                                          |
| `<seealso>`                         | Banned                           | Banned                | —                                                                          |

### Lists

Markdown dashes do not render in XML docs. The three `<list>` shapes:

```csharp
/// <list type="bullet">
///   <item><description>Bulleted entry.</description></item>
/// </list>
///
/// <list type="number">
///   <item><description>Numbered entry.</description></item>
/// </list>
///
/// <list type="bullet">
///   <item>
///     <term>First</term>
///     <description>"account-9E763770-0A8D" → category "account"</description>
///   </item>
/// </list>
```

## Canonical Phrasing

Pre-approved one-liners. Reuse them verbatim — do not restyle per member.

| Shape                          | Phrasing                                            |
|--------------------------------|------------------------------------------------------|
| `CancellationToken` parameter  | `The cancellation token.`                            |
| Options parameter              | `The options for [operation].`                       |
| Stream name parameter          | `The name of the stream.`                            |
| Return with a null case        | `The [thing] if found; otherwise, <c>null</c>.`      |
| Builder return                 | `The builder for method chaining.`                   |
| Service collection return      | `The service collection for method chaining.`        |
| Class summary                  | `Represents [what it models]…`                       |
| Interface summary              | `Defines [the contract]…`                            |
| Event summary                  | `Occurs when [condition].`                           |
| Constructor summary            | `Initializes a new instance of the <see cref="ClassName"/> class.` |
| Module client summary          | `Provides operations for [module] in KurrentDB.`     |

Style: methods start with a verb ("Appends…", "Configures…"). Properties in present tense ("Gets…", never "Will get…").
State non-obvious defaults inline: `Defaults to 30 seconds.` The constructor boilerplate carries the one summary
`<see cref>` that rule 5 sanctions.

## Terminology

One word per concept, everywhere:

| Use               | Never                                     |
|-------------------|--------------------------------------------|
| stream            | topic, queue                              |
| record            | event (in V2 API context)                 |
| processor         | consumer, subscriber                      |
| drone             | — (a running processor instance)          |
| checkpoint        | offset, cursor                            |
| position          | offset, sequence number                   |
| connection string | URI, address (for `kurrentdb://` strings) |

## Shapes

Method — the complete public shape; add nothing else:

```csharp
/// <summary>
/// Appends one or more records to a stream.
/// </summary>
/// <param name="stream">The name of the stream to append to.</param>
/// <param name="records">The records to append.</param>
/// <param name="cancellationToken">The cancellation token.</param>
/// <returns>The result of the append operation, including the new stream revision.</returns>
/// <exception cref="StreamDeletedException">The stream has been deleted.</exception>
public ValueTask<AppendRecordsResult> AppendRecords(
    string stream,
    IEnumerable<RecordToWrite> records,
    CancellationToken cancellationToken = default) { }
```

Remarks that earn their place — a contract the signature cannot express:

```csharp
/// <summary>
/// Creates a session for batched appends.
/// </summary>
/// <returns>The append session.</returns>
/// <remarks>
/// Not thread-safe. The session must be disposed before the client is disposed.
/// </remarks>
public AppendSession CreateSession() { }
```

Companion-type pointer — the type that controls the operation, always cref'd, never `<c>`:

```csharp
/// <remarks>
/// Use <see cref="ExpectedStreamState"/> to control optimistic concurrency.
/// </remarks>
```

Property — `<value>` only when the summary cannot carry the value semantics:

```csharp
/// <summary>
/// Gets the address of the KurrentDB cluster.
/// </summary>
/// <value>
/// For direct connections, the single endpoint URI. For discovery mode, a synthetic discovery URI.
/// </value>
public Uri Address { get; }
```

Record struct — constructor parameters documented on the type:

```csharp
/// <summary>
/// Represents a position within a stream.
/// </summary>
/// <param name="Stream">The name of the stream.</param>
/// <param name="Revision">The revision number within the stream.</param>
public readonly record struct RecordPosition(StreamName Stream, ulong Revision) { }
```

Enum — every value gets one line:

```csharp
/// <summary>
/// Specifies how the consumer starts when no checkpoint exists.
/// </summary>
public enum StartPosition {
    /// <summary>
    /// Start from the earliest available record.
    /// </summary>
    Earliest,

    /// <summary>
    /// Start from the next record appended after subscription.
    /// </summary>
    Latest
}
```

C# 14 extension block — the class summary names the extended type; members carry their own docs inside:

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

Fluent builder — summary-only members:

```csharp
/// <summary>
/// Fluent builder for constructing <see cref="ReadRecordsRequest"/> instances.
/// </summary>
public class Builder : FluentBuilder<Builder, ReadRecordsRequest> {
    /// <summary>
    /// Sets the starting position for the read in the global log.
    /// </summary>
    public Builder FromPosition(LogPosition position) =>
        With(r => r with { Start = position });
}
```

IVariant union — base interface plus each case; generated conversion operators need nothing:

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

Obsolete — remarks carry the replacement, the one always-actionable cross-reference:

```csharp
/// <summary>
/// Gets the connection string.
/// </summary>
/// <remarks>
/// Obsolete. Use <see cref="ConnectionStringExpression"/> instead.
/// </remarks>
[Obsolete("Use ConnectionStringExpression instead.")]
public string ConnectionString { get; }
```

Experimental — stability is a contract:

```csharp
/// <summary>
/// Configures the processor to use batched dispatch.
/// </summary>
/// <remarks>
/// Experimental. May change in future releases.
/// </remarks>
[Experimental("KURRENT001")]
public static KurrentProcessor CreateBatched(...) { }
```

Internal — bare by default, one line when the name cannot carry it:

```csharp
internal static StreamName Normalize(StreamName stream) { }

/// <summary>
/// Caches resolved endpoints per node until the next gossip refresh.
/// </summary>
internal sealed class EndpointCache { }
```

## Anti-Patterns

```csharp
// BAD — restates the member name
/// <summary>
/// Gets the stream name.
/// </summary>
public StreamName StreamName { get; }

// GOOD — adds the information the name cannot carry
/// <summary>
/// Gets the name of the stream this record was appended to.
/// </summary>
public StreamName StreamName { get; }

// BAD — single-line summary; always use the multiline form
/// <summary>Appends one or more records to a stream.</summary>

// BAD — implementation detail
/// <summary>
/// Sends AppendRecords RPC via the V2 gRPC channel.
/// </summary>

// BAD — changelog / causality
/// <summary>
/// Appends records. Validation now happens here because SchemaGovernance
/// no longer validates on read.
/// </summary>

// BAD — change narration; state only what IS
/// <summary>
/// Reads records. Previously returned an empty batch; now throws.
/// </summary>

// BAD — remarks elaborating the summary
/// <remarks>
/// This method is useful when you want to append records.
/// </remarks>

// BAD — internal member with public-grade docs
/// <summary>
/// Creates the channel.
/// </summary>
/// <param name="address">The address.</param>
/// <returns>The channel.</returns>
internal GrpcChannel Create(Uri address) { }

// BAD — placeholder
/// <summary>
/// TODO: Add documentation
/// </summary>

// BAD — unexplained abbreviations
/// <summary>
/// Gets the conn str for KDB.
/// </summary>
public string ConnectionString { get; }
```

## Validation

1. Build — XML doc warnings are errors
2. Hover in an IDE — check IntelliSense rendering
3. Terminology and phrasing match this document
4. Spell check

## Resources

- [C# XML Documentation Comments](https://learn.microsoft.com/dotnet/csharp/language-reference/xmldoc/)
- [Recommended XML Tags](https://learn.microsoft.com/dotnet/csharp/language-reference/xmldoc/recommended-tags)
- [.NET API Documentation Guidelines](https://github.com/dotnet/dotnet-api-docs/wiki)
