---
paths:
  - "src/**/*.cs"
---

# AOT Compatibility - No Reflection

## NEVER use reflection for:

- Serialization (use source generators)
- Type mapping (use MessageTypeMapper registration)
- Dynamic code generation

## Required Patterns

- `JsonSerializerContext` for System.Text.Json
- `[KurrentOperationError]` attribute for error generation
- `IVariant<T0, T1>` for discriminated unions
