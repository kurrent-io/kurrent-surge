# Culture Fallback

## Overview

Culture fallback is the mechanism by which .NET resolves resources when an exact culture match isn't available. It follows a hierarchical search pattern from most specific to least specific culture, ensuring applications gracefully degrade to available resources.

## CultureInfo Hierarchy

### Culture Types

#### 1. Specific (Region-Specific) Culture

Combines language and region:
```csharp
new CultureInfo("en-US")    // English (United States)
new CultureInfo("es-MX")    // Spanish (Mexico)
new CultureInfo("fr-CA")    // French (Canada)
new CultureInfo("zh-CN")    // Chinese (China)
```

#### 2. Neutral (Language-Only) Culture

Language without region:
```csharp
new CultureInfo("en")       // English (neutral)
new CultureInfo("es")       // Spanish (neutral)
new CultureInfo("fr")       // French (neutral)
new CultureInfo("zh")       // Chinese (neutral)
```

#### 3. Invariant Culture

Culture-independent, used for internal data:
```csharp
CultureInfo.InvariantCulture  // Empty culture name ""
```

### Parent-Child Relationships

```csharp
CultureInfo specific = new CultureInfo("es-MX");
CultureInfo neutral = specific.Parent;      // "es"
CultureInfo invariant = neutral.Parent;     // "" (InvariantCulture)

Console.WriteLine($"Specific: {specific.Name}");    // es-MX
Console.WriteLine($"Neutral: {neutral.Name}");      // es
Console.WriteLine($"Invariant: {invariant.Name}");  // (empty string)
```

## Fallback Chain

### Basic Fallback Sequence

For a request with culture `es-MX`:

```
1. Resources.es-MX.resx    (Specific culture)
   ↓ Not found
2. Resources.es.resx       (Neutral parent culture)
   ↓ Not found
3. Resources.resx          (Default/invariant culture)
   ↓ Not found
4. null                    (Resource not found)
```

### Visual Example

```
User requests: CurrentUICulture = "es-MX"
Resource Key: "WelcomeMessage"

Search Order:
┌─────────────────────────────────┐
│ 1. Resources.es-MX.resx         │ ← Specific culture
│    es-MX/MyApp.resources.dll    │
└─────────────────────────────────┘
              ↓ Not found
┌─────────────────────────────────┐
│ 2. Resources.es.resx            │ ← Neutral parent
│    es/MyApp.resources.dll       │
└─────────────────────────────────┘
              ↓ Not found
┌─────────────────────────────────┐
│ 3. Resources.resx               │ ← Default
│    Main assembly                │
└─────────────────────────────────┘
              ↓ Not found
          Returns null
```

## CurrentCulture vs CurrentUICulture

### CurrentUICulture (Resource Lookup)

Used by ResourceManager to select resources:

```csharp
CultureInfo.CurrentUICulture = new CultureInfo("es-MX");

// ResourceManager uses CurrentUICulture for fallback
string value = Resources.WelcomeMessage;  // Searches es-MX → es → default
```

### CurrentCulture (Formatting)

Used for formatting numbers, dates, currencies:

```csharp
CultureInfo.CurrentCulture = new CultureInfo("es-MX");

decimal price = 1234.56m;
string formatted = price.ToString("C");  // Uses CurrentCulture: $1,234.56
```

### Best Practice: Set Both

```csharp
var culture = new CultureInfo("es-MX");
CultureInfo.CurrentCulture = culture;      // Formatting
CultureInfo.CurrentUICulture = culture;    // Resources

// Or set both at once
Thread.CurrentThread.CurrentCulture = culture;
Thread.CurrentThread.CurrentUICulture = culture;
```

## Fallback Behavior Examples

### Example 1: Complete Hierarchy

**Resources:**
```
Resources.resx          → "Welcome"
Resources.es.resx       → "Bienvenido"
Resources.es-MX.resx    → "¡Bienvenido!"
```

**Lookups:**
```csharp
// Specific culture
CultureInfo.CurrentUICulture = new CultureInfo("es-MX");
string value = Resources.WelcomeMessage;  // "¡Bienvenido!" (from es-MX)

// Neutral parent
CultureInfo.CurrentUICulture = new CultureInfo("es");
value = Resources.WelcomeMessage;  // "Bienvenido" (from es)

// Different Spanish region (no specific resource)
CultureInfo.CurrentUICulture = new CultureInfo("es-ES");
value = Resources.WelcomeMessage;  // "Bienvenido" (falls back to es)

// English
CultureInfo.CurrentUICulture = new CultureInfo("en");
value = Resources.WelcomeMessage;  // "Welcome" (from default)
```

### Example 2: Missing Neutral Culture

**Resources:**
```
Resources.resx          → "Welcome"
Resources.es-MX.resx    → "¡Bienvenido!"
(No Resources.es.resx)
```

**Lookups:**
```csharp
// Specific culture exists
CultureInfo.CurrentUICulture = new CultureInfo("es-MX");
string value = Resources.WelcomeMessage;  // "¡Bienvenido!" (from es-MX)

// Neutral not found, falls back to default
CultureInfo.CurrentUICulture = new CultureInfo("es");
value = Resources.WelcomeMessage;  // "Welcome" (from default, skips missing es)

// Other Spanish region, no neutral fallback
CultureInfo.CurrentUICulture = new CultureInfo("es-ES");
value = Resources.WelcomeMessage;  // "Welcome" (es-ES not found, es not found, default)
```

### Example 3: Partial Translation

**Resources:**
```
Resources.resx:
  - WelcomeMessage → "Welcome"
  - GoodbyeMessage → "Goodbye"

Resources.es.resx:
  - WelcomeMessage → "Bienvenido"
  (GoodbyeMessage missing)
```

**Lookups:**
```csharp
CultureInfo.CurrentUICulture = new CultureInfo("es");

string welcome = Resources.WelcomeMessage;  // "Bienvenido" (from es)
string goodbye = Resources.GoodbyeMessage;  // "Goodbye" (falls back to default)
```

## Controlling Fallback

### FallBackToParentUICultures

Controls whether fallback to parent cultures is enabled:

```csharp
// Enable fallback (default)
ResourceManager.FallbackLocation = UltimateResourceFallbackLocation.MainAssembly;

// Disable fallback to parent cultures
// (Falls directly to default if specific culture not found)
```

### NeutralResourcesLanguageAttribute

Specifies the default culture embedded in the main assembly:

```csharp
[assembly: NeutralResourcesLanguage("en-US", UltimateResourceFallbackLocation.MainAssembly)]
```

**Options:**
- `MainAssembly`: Default resources in main assembly (recommended)
- `Satellite`: Default resources in satellite assembly (rare)

**In AssemblyInfo.cs or Project File:**
```csharp
using System.Resources;

[assembly: NeutralResourcesLanguage("en-US", UltimateResourceFallbackLocation.MainAssembly)]
```

**Or in .csproj:**
```xml
<PropertyGroup>
  <NeutralLanguage>en-US</NeutralLanguage>
</PropertyGroup>
```

## Culture Negotiation in Web Applications

### ASP.NET Core Request Localization

```csharp
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[]
    {
        new CultureInfo("en-US"),
        new CultureInfo("es-MX"),
        new CultureInfo("fr-CA")
    };

    options.DefaultRequestCulture = new RequestCulture("en-US");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;

    // Enable fallback to parent cultures
    options.FallBackToParentCultures = true;
    options.FallBackToParentUICultures = true;
});
```

### Culture Provider Order

Culture providers are tried in order until one returns a culture:

```csharp
options.RequestCultureProviders = new List<IRequestCultureProvider>
{
    new QueryStringRequestCultureProvider(),        // 1. Check ?culture=es-MX
    new CookieRequestCultureProvider(),             // 2. Check cookie
    new AcceptLanguageHeaderRequestCultureProvider() // 3. Check HTTP header
};
```

**Fallback within each provider:**
```
HTTP Accept-Language: es-MX, es;q=0.9, en;q=0.8

1. Try es-MX → Supported? Yes → Use es-MX
2. If not supported, try es → Supported? Yes → Use es
3. If not supported, try en → Supported? Yes → Use en
4. If none supported → Use DefaultRequestCulture
```

## Custom Fallback Logic

### Extension Method for Fallback with Defaults

```csharp
public static class ResourceManagerExtensions
{
    public static string GetStringWithFallback(
        this ResourceManager rm,
        string key,
        CultureInfo culture = null,
        string defaultValue = null)
    {
        culture ??= CultureInfo.CurrentUICulture;

        // Try specific culture
        string value = rm.GetString(key, culture);
        if (value != null) return value;

        // Try parent culture
        if (culture.Parent != CultureInfo.InvariantCulture)
        {
            value = rm.GetString(key, culture.Parent);
            if (value != null) return value;
        }

        // Try invariant
        value = rm.GetString(key, CultureInfo.InvariantCulture);
        if (value != null) return value;

        // Return default or key
        return defaultValue ?? $"[{key}]";
    }
}

// Usage
string message = rm.GetStringWithFallback("Key", null, "Default message");
```

### Logging Fallback Behavior

```csharp
public class LoggingResourceManager : ResourceManager
{
    private readonly ILogger _logger;

    public LoggingResourceManager(string baseName, Assembly assembly, ILogger logger)
        : base(baseName, assembly)
    {
        _logger = logger;
    }

    public override string GetString(string name, CultureInfo culture)
    {
        culture ??= CultureInfo.CurrentUICulture;

        _logger.LogDebug($"Requesting resource '{name}' for culture '{culture.Name}'");

        string value = base.GetString(name, culture);

        if (value == null)
        {
            _logger.LogWarning($"Resource '{name}' not found for culture '{culture.Name}'");
        }
        else
        {
            // Determine which culture provided the value
            var actualCulture = GetActualCulture(name, culture);
            if (actualCulture.Name != culture.Name)
            {
                _logger.LogInformation(
                    $"Resource '{name}' for '{culture.Name}' fell back to '{actualCulture.Name}'"
                );
            }
        }

        return value;
    }

    private CultureInfo GetActualCulture(string name, CultureInfo requestedCulture)
    {
        // Check specific culture
        ResourceSet rs = InternalGetResourceSet(requestedCulture, false, false);
        if (rs?.GetString(name) != null)
            return requestedCulture;

        // Check parent cultures
        CultureInfo parent = requestedCulture.Parent;
        while (parent != CultureInfo.InvariantCulture)
        {
            rs = InternalGetResourceSet(parent, false, false);
            if (rs?.GetString(name) != null)
                return parent;
            parent = parent.Parent;
        }

        // Must be in default resources
        return CultureInfo.InvariantCulture;
    }
}
```

## Testing Fallback Behavior

### Unit Test Example

```csharp
[Test]
[Arguments("es-MX", "¡Bienvenido!")]   // Specific culture
[Arguments("es-ES", "Bienvenido")]     // Falls back to es
[Arguments("es", "Bienvenido")]        // Neutral culture
[Arguments("fr", "Welcome")]           // Falls back to default
public void resource_fallback_works_correctly(string cultureName, string expected)
{
    // Arrange
    CultureInfo.CurrentUICulture = new CultureInfo(cultureName);

    // Act
    string actual = Resources.WelcomeMessage;

    // Assert
    actual.ShouldBe(expected);
}
```

### Integration Test for ASP.NET Core

```csharp
[Test]
public async Task request_with_unsupported_culture_falls_back_to_parent()
{
    // Arrange
    using var client = _factory.CreateClient();

    // Request with unsupported specific culture
    client.DefaultRequestHeaders.AcceptLanguage.Add(
        new StringWithQualityHeaderValue("es-AR")  // Argentinian Spanish (unsupported)
    );

    // Act
    var response = await client.GetAsync("/home/index");
    var content = await response.Content.ReadAsStringAsync();

    // Assert - should fall back to 'es' (neutral Spanish)
    content.ShouldContain("Bienvenido");  // Spanish, not English
}
```

## Best Practices

### 1. Always Provide Default Resources

```xml
<!-- Resources.resx (default) - ALWAYS REQUIRED -->
<data name="Message">
  <value>Default message in English</value>
</data>
```

### 2. Use Neutral Cultures When Possible

```
✓ Good:
Resources.es.resx           (Covers all Spanish regions)

❌ Unnecessary:
Resources.es-ES.resx
Resources.es-MX.resx
Resources.es-AR.resx
(Only needed if regions differ significantly)
```

### 3. Set Both CurrentCulture and CurrentUICulture

```csharp
var culture = new CultureInfo("es-MX");
CultureInfo.CurrentCulture = culture;      // Formatting
CultureInfo.CurrentUICulture = culture;    // Resources
```

### 4. Test Fallback Paths

Ensure resources degrade gracefully:
- Test with unsupported cultures
- Test with partially translated resources
- Verify fallback to default works

### 5. Log Missing Resources in Production

```csharp
LocalizedString ls = _localizer["Key"];
if (ls.ResourceNotFound)
{
    _logger.LogWarning($"Missing resource: {ls.Name} for culture {CultureInfo.CurrentUICulture.Name}");
}
```

## Common Pitfalls

### ❌ Not Setting CurrentUICulture

```csharp
// Wrong - only sets formatting culture
CultureInfo.CurrentCulture = new CultureInfo("es");

string value = Resources.Message;  // Still uses default CurrentUICulture (en-US)
```

### ❌ Assuming Exact Culture Match

```csharp
// Wrong - assumes es-AR resources exist
CultureInfo.CurrentUICulture = new CultureInfo("es-AR");
string value = Resources.Message;
// Falls back to 'es' or default if es-AR not provided
```

### ❌ Missing Default Resources

```csharp
// Wrong - no default Resources.resx
Resources.es.resx  // Only Spanish
Resources.fr.resx  // Only French

// Fallback fails for unsupported cultures
CultureInfo.CurrentUICulture = new CultureInfo("de");
string value = Resources.Message;  // null - no German, no default
```

## Summary

- **Fallback Chain**: Specific Culture → Neutral Culture → Default Culture → null
- **CurrentUICulture**: Controls resource lookup
- **CurrentCulture**: Controls formatting (numbers, dates)
- **Partial Translation**: Missing keys fall back to parent culture
- **Parent Cultures**: Automatically traversed during fallback
- **Best Practices**: Always provide defaults, use neutral cultures, test fallback paths

Culture fallback ensures applications gracefully degrade when exact translations aren't available, providing the best available resource for any requested culture.
