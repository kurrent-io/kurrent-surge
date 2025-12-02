# ResourceManager Guide

## Overview

The `ResourceManager` class provides convenient access to culture-specific resources at runtime. It implements the resource fallback mechanism and caching for optimal performance in .NET applications.

## ResourceManager Class

### Namespace
```csharp
using System.Resources;
using System.Globalization;
using System.Reflection;
```

### Basic Usage

#### Auto-Generated Designer.cs Pattern (Recommended)

```csharp
// Resources.Designer.cs provides strongly-typed access
string message = Resources.WelcomeMessage;
string error = Resources.ErrorInvalidInput;

// With culture override
Resources.Culture = new CultureInfo("es-MX");
string localizedMessage = Resources.WelcomeMessage;
```

#### Direct ResourceManager Instantiation

```csharp
// Create ResourceManager for a resource file
var rm = new ResourceManager(
    "MyApp.Properties.Resources",           // Full type name
    typeof(Program).Assembly                 // Assembly containing resources
);

// Get resource string
string value = rm.GetString("WelcomeMessage");

// Get resource with specific culture
string spanishValue = rm.GetString("WelcomeMessage", new CultureInfo("es"));
```

### ResourceManager Constructors

#### 1. Type-Based Constructor

```csharp
// Most common - uses the resource type
var rm = new ResourceManager(typeof(Resources));
```

#### 2. String-Based Constructor

```csharp
// Requires full namespace and assembly
var rm = new ResourceManager(
    "MyApp.Resources.ErrorMessages",
    Assembly.GetExecutingAssembly()
);
```

#### 3. Custom ResourceSet Constructor

```csharp
// Advanced: custom ResourceSet for specialized loading
var rm = new ResourceManager(
    "MyApp.Resources.CustomResources",
    Assembly.GetExecutingAssembly(),
    typeof(MyCustomResourceSet)
);
```

## Core Methods

### GetString

Retrieves a localized string resource.

```csharp
// Get string using current UI culture
string value = rm.GetString("ResourceKey");

// Get string for specific culture
string value = rm.GetString("ResourceKey", new CultureInfo("fr-CA"));

// Handle missing resources
string value = rm.GetString("NonExistentKey");
if (value == null)
{
    // Resource not found in any culture
    value = "Default value";
}
```

### GetObject

Retrieves a non-string resource (images, icons, binary data).

```csharp
// Get an image resource
var icon = (System.Drawing.Icon)rm.GetObject("AppIcon");

// Get with specific culture
var localizedIcon = (System.Drawing.Icon)rm.GetObject("AppIcon", new CultureInfo("ja"));

// Get any object type
object resource = rm.GetObject("MyResource");
if (resource is Bitmap bitmap)
{
    // Process bitmap
}
else if (resource is byte[] data)
{
    // Process binary data
}
```

### GetStream

Retrieves a resource as a `Stream` (useful for large binary resources).

```csharp
// Get resource as stream
using (Stream stream = rm.GetStream("LargeFile"))
{
    // Process stream without loading entire file into memory
    byte[] buffer = new byte[4096];
    int bytesRead;
    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
    {
        // Process buffer
    }
}

// With specific culture
using (Stream stream = rm.GetStream("LargeFile", new CultureInfo("de")))
{
    // Process German version of the file
}
```

### GetResourceSet

Retrieves all resources for a specific culture.

```csharp
// Get all resources for a culture
ResourceSet rs = rm.GetResourceSet(
    new CultureInfo("es-MX"),
    createIfNotExists: true,
    tryParents: true
);

// Enumerate all resources
foreach (DictionaryEntry entry in rs)
{
    string key = (string)entry.Key;
    object value = entry.Value;
    Console.WriteLine($"{key} = {value}");
}
```

### ReleaseAllResources

Releases all cached `ResourceSet` objects.

```csharp
// Free memory by releasing cached resource sets
rm.ReleaseAllResources();

// Useful when:
// - Dynamically switching cultures frequently
// - Memory-constrained environments
// - Long-running applications with periodic culture changes
```

## Resource Probing Algorithm

### Probing Sequence

When requesting a resource, `ResourceManager` searches in this order:

```
1. Specific culture satellite assembly (es-MX)
2. Neutral culture satellite assembly (es)
3. Default culture in main assembly
4. Returns null if not found
```

**Example:**
```csharp
CultureInfo.CurrentUICulture = new CultureInfo("es-MX");
string value = rm.GetString("Message");

// Searches:
// 1. MyApp.resources.dll in es-MX/ folder
// 2. MyApp.resources.dll in es/ folder
// 3. MyApp.dll (embedded Resources.resources)
// 4. Returns null if nowhere found
```

### Probing Paths

For an application at `C:\MyApp\MyApp.exe` requesting Spanish (Mexico) resources:

```
Probing order:
1. C:\MyApp\es-MX\MyApp.resources.dll
2. C:\MyApp\es\MyApp.resources.dll
3. C:\MyApp\MyApp.exe (embedded default resources)
```

### Controlling Probing with IgnoreCase

```csharp
// Case-sensitive probing (default, faster)
var rm = new ResourceManager("MyApp.Resources", assembly);

// Enable case-insensitive search (slower but more forgiving)
rm.IgnoreCase = true;
string value = rm.GetString("welcomemessage"); // Finds "WelcomeMessage"
```

## ResourceSet Class

### Overview

`ResourceSet` represents a collection of resources for a specific culture. `ResourceManager` returns `ResourceSet` instances.

### Direct ResourceSet Usage

```csharp
// Get ResourceSet for a specific culture
CultureInfo culture = new CultureInfo("fr");
ResourceSet resourceSet = rm.GetResourceSet(culture, createIfNotExists: true, tryParents: true);

// Enumerate all resources
IDictionaryEnumerator enumerator = resourceSet.GetEnumerator();
while (enumerator.MoveNext())
{
    string key = (string)enumerator.Key;
    object value = enumerator.Value;
    Console.WriteLine($"{key} = {value}");
}

// Get specific resource from the set
string value = resourceSet.GetString("WelcomeMessage");
object obj = resourceSet.GetObject("AppIcon");

// Dispose when done (releases file handles)
resourceSet.Dispose();
```

### Custom ResourceSet

```csharp
// Create a custom ResourceSet for specialized scenarios
public class CustomResourceSet : ResourceSet
{
    public CustomResourceSet(Stream stream) : base(stream)
    {
    }

    public override string GetString(string name)
    {
        string value = base.GetString(name);
        // Apply custom processing (e.g., decryption, transformation)
        return ProcessValue(value);
    }

    private string ProcessValue(string value)
    {
        // Custom logic
        return value?.ToUpper(); // Example: always return uppercase
    }
}
```

## ResourceReader vs ResourceManager

### ResourceReader

- **Purpose**: Low-level reading of `.resources` files
- **Use Case**: Direct file access, custom resource processing
- **Performance**: Faster for single-culture scenarios
- **Culture Support**: Manual - no automatic fallback

```csharp
// Direct reading of compiled .resources file
using (var reader = new ResourceReader("MyApp.Properties.Resources.resources"))
{
    foreach (DictionaryEntry entry in reader)
    {
        Console.WriteLine($"{entry.Key} = {entry.Value}");
    }
}
```

### ResourceManager

- **Purpose**: High-level culture-aware resource access
- **Use Case**: Production applications with localization
- **Performance**: Cached, optimized for repeated access
- **Culture Support**: Automatic fallback, probing

```csharp
// Culture-aware access with automatic fallback
var rm = new ResourceManager("MyApp.Properties.Resources", assembly);
string value = rm.GetString("Key"); // Automatically uses CurrentUICulture
```

### When to Use Each

**Use ResourceReader when:**
- Reading resources from external `.resources` files
- Building custom resource management tools
- Single-culture scenarios where performance is critical
- Enumerating all resources in a file

**Use ResourceManager when:**
- Building multi-language applications
- Leveraging automatic culture fallback
- Using satellite assemblies
- Caching is beneficial (repeated access)

## Performance Considerations

### Caching

`ResourceManager` caches `ResourceSet` objects for performance:

```csharp
// First access - loads and caches ResourceSet
string value1 = rm.GetString("Message1"); // Cache miss - loads from disk

// Subsequent access - uses cached ResourceSet
string value2 = rm.GetString("Message2"); // Cache hit - fast
string value3 = rm.GetString("Message3"); // Cache hit - fast
```

### Memory Management

```csharp
// Long-running application with infrequent resource access
var rm = new ResourceManager(typeof(Resources));

// Use resources
string value = rm.GetString("Message");

// Release cached ResourceSets to free memory
rm.ReleaseAllResources();

// Later, resources can be accessed again (will reload and cache)
```

### Culture Switching Performance

```csharp
// Expensive: Creating new ResourceManager for each culture
// ❌ Bad
var rmEnglish = new ResourceManager(typeof(Resources));
var rmSpanish = new ResourceManager(typeof(Resources));
var rmFrench = new ResourceManager(typeof(Resources));

// Efficient: One ResourceManager, change CurrentUICulture
// ✓ Good
var rm = new ResourceManager(typeof(Resources));

CultureInfo.CurrentUICulture = new CultureInfo("en");
string english = rm.GetString("Message");

CultureInfo.CurrentUICulture = new CultureInfo("es");
string spanish = rm.GetString("Message");

CultureInfo.CurrentUICulture = new CultureInfo("fr");
string french = rm.GetString("Message");
```

### Lazy Loading

Resources are loaded on-demand:

```csharp
// ResourceManager created but no resources loaded yet
var rm = new ResourceManager(typeof(Resources));

// First GetString loads the ResourceSet for CurrentUICulture
string value = rm.GetString("Message"); // Loads es-MX resources

// Changing culture loads different ResourceSet
CultureInfo.CurrentUICulture = new CultureInfo("fr");
string frenchValue = rm.GetString("Message"); // Loads fr resources
```

## Strongly-Typed Resource Classes

### Auto-Generated Pattern

Visual Studio generates strongly-typed resource classes:

```csharp
// Resources.Designer.cs (generated)
internal class Resources
{
    private static ResourceManager resourceMan;
    private static CultureInfo resourceCulture;

    internal static ResourceManager ResourceManager
    {
        get
        {
            if (resourceMan == null)
            {
                resourceMan = new ResourceManager(
                    "MyApp.Properties.Resources",
                    typeof(Resources).Assembly
                );
            }
            return resourceMan;
        }
    }

    internal static CultureInfo Culture
    {
        get { return resourceCulture; }
        set { resourceCulture = value; }
    }

    internal static string WelcomeMessage
    {
        get { return ResourceManager.GetString("WelcomeMessage", resourceCulture); }
    }

    internal static System.Drawing.Icon AppIcon
    {
        get { return (System.Drawing.Icon)ResourceManager.GetObject("AppIcon", resourceCulture); }
    }
}
```

### Benefits

1. **IntelliSense Support**: Compile-time checking of resource keys
2. **Type Safety**: Correct types for non-string resources
3. **Refactoring**: Rename refactoring works across codebase
4. **Documentation**: XML comments from resource comments

### Usage

```csharp
// Strongly-typed access (compile-time checked)
string message = Resources.WelcomeMessage;
Icon icon = Resources.AppIcon;

// Weakly-typed access (runtime checked, error-prone)
string message = rm.GetString("WelcomeMessage"); // Typo risk
```

## Advanced Patterns

### Thread-Safe Culture-Specific Resource Access

```csharp
public class LocalizedResourceManager
{
    private readonly ResourceManager _resourceManager;

    public LocalizedResourceManager(Type resourceType)
    {
        _resourceManager = new ResourceManager(resourceType);
    }

    public string GetString(string key, CultureInfo culture = null)
    {
        culture ??= CultureInfo.CurrentUICulture;
        return _resourceManager.GetString(key, culture);
    }

    public string GetStringFormatted(string key, CultureInfo culture, params object[] args)
    {
        string format = GetString(key, culture);
        return string.Format(culture, format, args);
    }
}

// Usage
var manager = new LocalizedResourceManager(typeof(Resources));
string message = manager.GetString("WelcomeMessage", new CultureInfo("es"));
```

### Resource Fallback with Default Values

```csharp
public static class ResourceExtensions
{
    public static string GetStringOrDefault(
        this ResourceManager rm,
        string key,
        string defaultValue = null,
        CultureInfo culture = null)
    {
        culture ??= CultureInfo.CurrentUICulture;
        string value = rm.GetString(key, culture);
        return value ?? defaultValue ?? key;
    }
}

// Usage
string message = rm.GetStringOrDefault("MaybeExistsKey", "Default message");
```

### Multi-Assembly Resource Access

```csharp
public class MultiAssemblyResourceManager
{
    private readonly Dictionary<string, ResourceManager> _managers = new();

    public void RegisterAssembly(string name, Assembly assembly, string resourceBaseName)
    {
        _managers[name] = new ResourceManager(resourceBaseName, assembly);
    }

    public string GetString(string assemblyName, string key)
    {
        if (_managers.TryGetValue(assemblyName, out var rm))
        {
            return rm.GetString(key);
        }
        throw new InvalidOperationException($"Assembly '{assemblyName}' not registered");
    }
}

// Usage
var manager = new MultiAssemblyResourceManager();
manager.RegisterAssembly("Core", typeof(CoreResources).Assembly, "MyApp.Core.Resources");
manager.RegisterAssembly("UI", typeof(UIResources).Assembly, "MyApp.UI.Resources");

string coreMessage = manager.GetString("Core", "ErrorMessage");
string uiMessage = manager.GetString("UI", "ButtonLabel");
```

## Best Practices

### 1. Singleton Pattern for ResourceManager

```csharp
// ✓ Good - single instance, lazy initialization
public class Resources
{
    private static readonly Lazy<ResourceManager> _resourceManager =
        new Lazy<ResourceManager>(() =>
            new ResourceManager("MyApp.Resources", typeof(Resources).Assembly));

    public static ResourceManager ResourceManager => _resourceManager.Value;
}
```

### 2. Use CurrentUICulture, Not CurrentCulture

```csharp
// ✓ Good - use CurrentUICulture for resource lookup
CultureInfo.CurrentUICulture = new CultureInfo("es-MX");
string value = rm.GetString("Message");

// ❌ Bad - CurrentCulture is for formatting, not resources
CultureInfo.CurrentCulture = new CultureInfo("es-MX");
string value = rm.GetString("Message", CultureInfo.CurrentCulture);
```

### 3. Handle Missing Resources Gracefully

```csharp
public static string GetStringOrKey(this ResourceManager rm, string key)
{
    string value = rm.GetString(key);
    if (value == null)
    {
        // Log warning
        Debug.WriteLine($"Missing resource: {key}");
        // Return key as fallback
        return $"[{key}]";
    }
    return value;
}
```

### 4. Release Resources in Long-Running Applications

```csharp
// Periodically release cached resources
Timer timer = new Timer(_ =>
{
    rm.ReleaseAllResources();
    GC.Collect();
}, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
```

## Common Issues and Solutions

### Issue: Resources Return Null

**Causes:**
- Incorrect namespace in ResourceManager constructor
- Resource not embedded in assembly
- Typo in resource key

**Solution:**
```csharp
// Verify namespace
var rm = new ResourceManager(
    "MyApp.Properties.Resources",  // Check full namespace
    Assembly.GetExecutingAssembly()
);

// Check embedded resource
// In .csproj:
// <EmbeddedResource Include="Resources\Resources.resx" />
```

### Issue: Wrong Culture Resources Loaded

**Cause:** Using `CurrentCulture` instead of `CurrentUICulture`

**Solution:**
```csharp
// ✓ Correct
CultureInfo.CurrentUICulture = new CultureInfo("es");
string value = rm.GetString("Message");

// ❌ Wrong
CultureInfo.CurrentCulture = new CultureInfo("es");
string value = rm.GetString("Message", CultureInfo.CurrentCulture);
```

### Issue: Memory Leaks from ResourceManager

**Cause:** Not disposing ResourceSets or keeping too many cached

**Solution:**
```csharp
// Periodically release resources
rm.ReleaseAllResources();

// Or dispose ResourceSets explicitly
using (ResourceSet rs = rm.GetResourceSet(culture, true, true))
{
    // Use resource set
}
```

## Summary

- **ResourceManager**: High-level, culture-aware resource access with automatic fallback
- **ResourceReader**: Low-level reading of `.resources` files
- **ResourceSet**: Collection of resources for a specific culture
- **Caching**: ResourceManager caches ResourceSets for performance
- **Probing**: Automatic search for culture-specific resources with fallback
- **Strongly-Typed**: Designer.cs provides compile-time checked access
- **Best Practices**: Singleton pattern, use CurrentUICulture, handle missing resources

`ResourceManager` is the foundation of .NET localization, providing efficient, culture-aware access to application resources.
