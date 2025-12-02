---
name: resx-expert
description: Master of .NET resource file (.resx) localization providing comprehensive expertise in creating, managing, and deploying localized resources for .NET applications. Covers ResourceManager, IStringLocalizer, satellite assemblies, composite formatting, culture fallback, ASP.NET Core integration, and best practices for multi-language applications.
---

# RESX Expert

This skill provides guidance on effectively localizing applications using .NET resource files (.resx), localization covering the complete lifecycle from resource creation to deployment.

It covers best practices, patterns, and anti-patterns for creating, managing, and deploying localized resources in .NET applications.

Covers ResourceManager, IStringLocalizer, satellite assemblies, composite formatting, culture fallback, ASP.NET Core integration, and best practices for multi-language applications.

## When to Use This Skill

### Automatic Triggers
Invoke this skill when the user mentions:
- "localization", "resx", "resources", "resource files", "error messages", "UI text", "strings"
- "internationalization", "i18n", "l10n", "globalization"
- Working with `.resx` files
- Multi-language support or translations
- `IStringLocalizer`, `ResourceManager`, or satellite assemblies
- Culture-specific formatting or translations
- ASP.NET Core localization
- Culture fallback or culture providers

### Explicit Requests
- "How do I create resource files?"
- "How to localize .NET applications?"
- "Best practices for .resx localization"
- "Implement multi-language support in .NET"
- "Add localization to..."
- "Create resource files for..."
- "Support multiple languages in..."
- "Localize strings/error messages/UI text"
- "Set up satellite assemblies"
- "Use ResourceManager/IStringLocalizer"
- "How do I localize..."

## Core Capabilities

### Resource File Management
- Create and organize `.resx` resource files with proper naming conventions
- Understand XML schema and structure
- Manage Designer.cs auto-generated files
- Handle embedded resources and external file references

### Resource Access Patterns
- **ResourceManager** (Classic .NET) - Strongly-typed resource access
- **IStringLocalizer** (ASP.NET Core) - Dependency injection-based localization
- **IViewLocalizer** - View-specific resource localization
- **IHtmlLocalizer** - HTML content with safe rendering

### Composite Formatting
- Parameterized resource strings with `{0}`, `{1}`, etc.
- Culture-specific formatting (dates, numbers, currency)
- Format specifiers (`{0:N2}`, `{0:C}`, `{0:D}`)
- Pluralization and gender-specific translations

### Satellite Assemblies
- Build culture-specific satellite assemblies
- Deploy with proper folder structure
- Understand resource probing algorithm
- Troubleshoot missing resources

### ASP.NET Core Integration
- Configure localization services and middleware
- Implement culture providers (query string, cookie, header)
- Set up RequestLocalizationMiddleware
- Use DataAnnotations localization

### Culture Handling
- Understand culture fallback chains (specific → neutral → default)
- Differentiate CurrentCulture vs CurrentUICulture
- Implement runtime culture switching
- Configure supported cultures

## Core Concepts

### Resource File Naming Conventions

**Neutral (Default) Resources:**
```
Resources.resx              # Default/neutral culture
ErrorMessages.resx          # Category-specific resources
```

**Culture-Specific Resources:**
```
Resources.es.resx           # Spanish (neutral)
Resources.es-MX.resx        # Mexican Spanish (specific)
Resources.fr.resx           # French (neutral)
Resources.fr-CA.resx        # Canadian French (specific)
```

**Location-Based Resources (ASP.NET Core):**
```
Views.Home.Index.resx       # Resources for Views/Home/Index.cshtml
Controllers.HomeController.resx  # Resources for Controllers/HomeController.cs
```

### Culture Fallback Chain

The .NET runtime resolves resources using this fallback sequence:

```
Specific Culture → Neutral Culture → Default Resources → Hardcoded String

Example: es-MX request
1. Resources.es-MX.resx (if exists)
2. Resources.es.resx (if exists)
3. Resources.resx (always present)
4. Hardcoded string (if no resource found)
```

### Composite Formatting Patterns

**Resource Definition:**
```xml
<data name="WelcomeMessage" xml:space="preserve">
  <value>Welcome, {0}! You have {1} new messages.</value>
  <comment>Greeting with username and message count. {0}=username, {1}=count</comment>
</data>
```

**Usage Patterns:**
```csharp
// String.Format style
string message = string.Format(Resources.WelcomeMessage, userName, messageCount);

// IStringLocalizer style (ASP.NET Core)
string message = _localizer["WelcomeMessage", userName, messageCount];
```

## Resource Access Patterns

### ResourceManager Pattern (Classic .NET)

```csharp
// Auto-generated Designer.cs provides strongly-typed access
string welcome = Resources.WelcomeMessage;

// Direct ResourceManager usage
var rm = new ResourceManager("MyApp.Resources", Assembly.GetExecutingAssembly());
string value = rm.GetString("WelcomeMessage", CultureInfo.CurrentUICulture);
```

### IStringLocalizer Pattern (ASP.NET Core)

```csharp
public class HomeController : Controller
{
    private readonly IStringLocalizer<HomeController> _localizer;

    public HomeController(IStringLocalizer<HomeController> localizer)
    {
        _localizer = localizer;
    }

    public IActionResult Index()
    {
        ViewData["Message"] = _localizer["WelcomeMessage"];

        // With parameters
        ViewData["Greeting"] = _localizer["WelcomeMessageFormat", userName, messageCount];

        return View();
    }
}
```

### IHtmlLocalizer Pattern (HTML Content)

```csharp
@inject IHtmlLocalizer<IndexModel> Localizer

<h1>@Localizer["PageTitle"]</h1>
<p>@Localizer["HtmlContent"]</p>  <!-- Renders HTML safely -->
```

### IViewLocalizer Pattern (View-Specific)

```csharp
@inject IViewLocalizer Localizer

<h1>@Localizer["Title"]</h1>
<!-- Automatically looks for Views.Home.Index.resx -->
```

## ASP.NET Core Integration

### Service Registration

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add localization services
builder.Services.AddLocalization(options =>
    options.ResourcesPath = "Resources");

// Configure supported cultures
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
});

// Add controllers with localization
builder.Services.AddControllersWithViews()
    .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
    .AddDataAnnotationsLocalization();

var app = builder.Build();

// Use request localization middleware
app.UseRequestLocalization();

app.MapControllers();
app.Run();
```

### Culture Provider Configuration

```csharp
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.RequestCultureProviders = new List<IRequestCultureProvider>
    {
        new QueryStringRequestCultureProvider(),           // ?culture=es-MX
        new CookieRequestCultureProvider(),                // Cookie
        new AcceptLanguageHeaderRequestCultureProvider()   // HTTP Accept-Language
    };
});
```

## Best Practices

### 1. Naming Conventions

**Resource Keys:**
- Use PascalCase: `WelcomeMessage`, `ErrorInvalidInput`
- Be descriptive: `Login_InvalidCredentials` not `Error1`
- Group related resources: `Validation_Required`, `Validation_TooLong`

**Resource Files:**
- Place in `Resources/` folder
- Match namespace structure for shared resources
- Use location-based naming for view-specific resources

### 2. Composite Formatting

**Always use parameters for dynamic content:**
```xml
<!-- ✓ Good -->
<data name="ItemsFound">
  <value>{0} items found in {1} seconds</value>
</data>

<!-- ✗ Bad - hardcoded values -->
<data name="ItemsFound">
  <value>5 items found</value>
</data>
```

### 3. Designer.cs Management

- Let Visual Studio or ResXFileCodeGenerator create it
- Don't manually edit Designer.cs
- Commit to source control
- Regenerate when .resx changes

### 4. Resource Organization

**Shared Resources:**
```
Resources/
  Resources.resx              # Shared across entire application
  ErrorMessages.resx          # Error messages only
  ValidationMessages.resx     # Validation messages
```

**Feature-Based Resources:**
```
Resources/
  Features/
    Authentication/
      Authentication.resx
      Authentication.es.resx
    Billing/
      Billing.resx
      Billing.es.resx
```

## Anti-Patterns to Avoid

### ❌ Don't: Hardcode Strings

```csharp
// Bad
return "Welcome to our application!";

// Good
return Resources.WelcomeMessage;
```

### ❌ Don't: String Concatenation for Formatting

```csharp
// Bad
string message = Resources.Welcome + " " + userName + "!";

// Good
string message = string.Format(Resources.WelcomeMessageFormat, userName);
```

### ❌ Don't: Ignore Culture Fallback

```csharp
// Bad - requires exact culture match
var rm = new ResourceManager("Resources", assembly);
string value = rm.GetString("Key", new CultureInfo("es-MX"));
if (value == null) throw new Exception("Missing resource");

// Good - uses fallback chain
CultureInfo.CurrentUICulture = new CultureInfo("es-MX");
string value = Resources.Key;  // Automatically falls back if needed
```

### ❌ Don't: Mix Resource Locations

```csharp
// Bad - inconsistent
Resources.WelcomeMessage
ErrorMessages.InvalidInput
StringResources.GetString("Title")

// Good - consistent pattern
Resources.WelcomeMessage
Resources.InvalidInput
Resources.Title
```

## Quick Reference

### Common Operations

**Create new resource file:**
```bash
# Visual Studio: Add New Item → Resources File
# Command line:
dotnet new resxfile -n Resources
```

**Add culture-specific variant:**
```bash
# Copy Resources.resx to Resources.es.resx
# Translate values, keep keys identical
```

**Access in code:**
```csharp
// Strongly-typed (preferred)
string value = Resources.ResourceKey;

// IStringLocalizer (ASP.NET Core)
string value = _localizer["ResourceKey"];

// With parameters
string formatted = _localizer["MessageFormat", param1, param2];
```

**Set culture at runtime:**
```csharp
CultureInfo.CurrentCulture = new CultureInfo("es-MX");
CultureInfo.CurrentUICulture = new CultureInfo("es-MX");
```

### File Structure Example

```
MyApp/
├── Resources/
│   ├── Resources.resx                    # en-US (default)
│   ├── Resources.Designer.cs             # Auto-generated
│   ├── Resources.es.resx                 # Spanish
│   ├── Resources.es-MX.resx              # Mexican Spanish
│   ├── Resources.fr.resx                 # French
│   ├── Views/
│   │   ├── Home/
│   │   │   ├── Index.resx                # View-specific
│   │   │   └── Index.es.resx
│   │   └── Shared/
│   │       └── _Layout.resx
│   └── Controllers/
│       └── HomeController.resx
├── MyApp.csproj
└── Program.cs
```

### Build Configuration

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <!-- Generate satellite assemblies for these cultures -->
    <SatelliteCultures>es;es-MX;fr;fr-CA;de;ja</SatelliteCultures>
  </PropertyGroup>

  <ItemGroup>
    <EmbeddedResource Update="Resources.resx">
      <Generator>ResXFileCodeGenerator</Generator>
      <LastGenOutput>Resources.Designer.cs</LastGenOutput>
    </EmbeddedResource>
  </ItemGroup>
</Project>
```

## Reference Documentation

Comprehensive guides available in `references/`:

1. **resx_fundamentals.md** - Complete .resx file structure and XML schema
2. **resource_manager_guide.md** - ResourceManager and ResourceReader patterns
3. **aspnetcore_localization.md** - ASP.NET Core IStringLocalizer integration
4. **composite_formatting.md** - String formatting patterns and best practices
5. **satellite_assemblies.md** - Building, deploying, and troubleshooting satellites
6. **culture_fallback.md** - Understanding culture resolution and fallback chains
7. **programmatic_manipulation.md** - Creating and editing .resx files programmatically
8. **testing_strategies.md** - Unit testing and integration testing localized apps
9. **build_configuration.md** - MSBuild, csproj, and tooling configuration
10. **migration_guide.md** - Migrating from legacy resource patterns

## Summary

Localized RESX Expert provides comprehensive mastery of .NET resource files and localization:

- **Resource Management**: Create, organize, and maintain .resx files
- **Access Patterns**: ResourceManager, IStringLocalizer, IViewLocalizer
- **Composite Formatting**: Parameterized strings and culture-specific formatting
- **Satellite Assemblies**: Build, deploy, and troubleshoot culture-specific resources
- **ASP.NET Core**: Full integration with middleware and DI
- **Best Practices**: Naming, organization, testing, and anti-patterns
- **Culture Handling**: Fallback chains, culture providers, runtime switching

Use this skill for all localization and internationalization work in .NET applications.
