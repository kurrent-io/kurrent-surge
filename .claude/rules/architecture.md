# Architecture Patterns

## Namespaces: Feature-Based (NOT folder-based)

```csharp
// File: src/.../Modules/Streams/StreamsClient.cs
namespace Kurrent.Client.Streams;  // NOT Kurrent.Client.Modules.Streams
```

## Partial Class Organization

```
StreamsClient.cs                    # Base definition
StreamsClient.Append.cs             # Operation
StreamsClient.Append.Models.cs      # DTOs
StreamsClient.Append.Mappers.cs     # Protocol mapping
```

## Module Pattern

- Inherit from `ClientModuleBase`
- Provides: Logger, SerializerProvider, ServerCapabilities, TypeMapper
