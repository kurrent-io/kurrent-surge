# Nullable Reference Types

- `<Nullable>enable</Nullable>` is ON
- `T` = guaranteed non-null (no defensive checks needed)
- `T?` = only for valid domain nullability
- NO `!` null-forgiving without explicit justification

```csharp
// WRONG - unnecessary null check
public void Process(Order order) {
    if (order == null) throw new ArgumentNullException();
```

```csharp
// CORRECT - trust the type system
public void Process(Order order) {
    // order is guaranteed non-null
```
