# Kurrent.Client Code Style

## Braces: K&R Style (NON-NEGOTIABLE)

```csharp
// CORRECT
public class Foo {
    public void Bar() {
        if (condition) {
            // code
        }
    }
}

// WRONG - Allman style
public class Foo
{
```

## Naming

- Private fields: `_camelCase`
- Test methods: `snake_case`
- Variables: `var` everywhere

## Modifiers

- Omit `private` and `internal` when they're the default

## Expression Bodies

- Prefer `=>` for simple members
