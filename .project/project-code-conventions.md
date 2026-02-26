# Code Conventions

Conventions that `.editorconfig` cannot enforce or only marks as `suggestion` (won't fail builds or `dotnet format`).

## Style

- Braces: K&R (opening brace on same line)
- Fields: `_camelCase`
- Variables: `var` everywhere

## Naming

- Test methods: `snake_case` (e.g., `appends_record_to_new_stream`)
- Namespaces: feature-based, file-scoped (e.g., `Kurrent.Client.Streams`, not `Kurrent.Client.Modules.Streams`)

## Async

- `ConfigureAwait(false)` on ALL awaits in library code (`src/`)
- Test methods return `ValueTask`, not `Task`

## Error Handling

- Never throw exceptions for expected failures; return `Result` types
- Never add error handling for scenarios that cannot occur

## Testing

- TUnit only (`[Test]`, `[Arguments]`, `[Category]`). No xUnit/NUnit attributes
- **`dotnet run`, not `dotnet test`** — TUnit uses Microsoft.Testing.Platform
- FluentDocker for containers, not TestContainers
- New tests use `Assert.That(...)` exclusively, not Shouldly
- Custom Result-aware extensions: `ShouldNotThrowOrFailAsync`, `ShouldFailAsync` (in `test/Kurrent.Testing/Shouldly/`)

## Type Mapping

- Use `MessageTypeMapper` registration, not reflection
- Partial class files follow: `{Module}Client.{Operation}.cs`, `{Module}Client.{Operation}.Models.cs`, `.Mappers.cs`,
  `.Extensions.cs`, `.IdiomaticExtensions.cs`, `.Errors.cs`, `.Models.Builders.cs`.
  See `src/Kurrent.Client/Modules/Streams/Operations/StreamsClient.Append.cs` for the canonical example

## Public API Surface

This is a published SDK. Do not rename, remove, or change the signature of any `public` member without explicit user
approval. New public API surface requires discussion.

## Multi-Target Compatibility

This project targets net8.0, net9.0, and net10.0. Use only APIs available in net8.0 unless behind `#if` guards. Build
with `--framework net8.0` to verify minimum-target compatibility.

## Proto Files

Before editing any `.proto` file, load the `/proto-style` skill. Never remove or renumber existing fields.
