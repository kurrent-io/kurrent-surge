# ASP.NET Core Localization

## Overview

ASP.NET Core provides a comprehensive localization framework built on top of standard .NET resource management, offering dependency injection-based localization services, middleware for culture negotiation, and specialized localizers for different scenarios.

## Core Interfaces

### IStringLocalizer

The primary interface for accessing localized strings in ASP.NET Core.

```csharp
public interface IStringLocalizer
{
    LocalizedString this[string name] { get; }
    LocalizedString this[string name, params object[] arguments] { get; }
    IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures);
}
```

### IStringLocalizer&lt;T&gt;

Generic version tied to a specific type for scoped resources.

```csharp
public interface IStringLocalizer<out T> : IStringLocalizer
{
}
```

## Service Registration

### Basic Configuration

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add localization services
builder.Services.AddLocalization(options =>
{
    // Specify where resource files are located
    options.ResourcesPath = "Resources";
});

// Add MVC with localization support
builder.Services.AddControllersWithViews()
    .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
    .AddDataAnnotationsLocalization();

var app = builder.Build();

// Configure request localization middleware
var supportedCultures = new[] { "en-US", "es-MX", "fr-CA" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture(supportedCultures[0])
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);

app.UseRequestLocalization(localizationOptions);

app.MapControllers();
app.Run();
```

### Advanced Configuration

```csharp
builder.Services.AddLocalization(options =>
{
    options.ResourcesPath = "Resources";
});

// Configure supported cultures
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[]
    {
        new CultureInfo("en-US"),
        new CultureInfo("en-GB"),
        new CultureInfo("es-MX"),
        new CultureInfo("es-ES"),
        new CultureInfo("fr-CA"),
        new CultureInfo("fr-FR"),
        new CultureInfo("de-DE"),
        new CultureInfo("ja-JP")
    };

    options.DefaultRequestCulture = new RequestCulture("en-US");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;

    // Optionally set fallback to invariant culture
    options.FallBackToParentUICultures = true;
    options.FallBackToParentCultures = true;

    // Configure culture providers (order matters!)
    options.RequestCultureProviders = new List<IRequestCultureProvider>
    {
        new QueryStringRequestCultureProvider(),        // ?culture=es-MX
        new CookieRequestCultureProvider(),             // Cookie
        new AcceptLanguageHeaderRequestCultureProvider() // HTTP header
    };
});
```

## IStringLocalizer Usage

### Constructor Injection

```csharp
public class HomeController : Controller
{
    private readonly IStringLocalizer<HomeController> _localizer;
    private readonly ILogger<HomeController> _logger;

    public HomeController(
        IStringLocalizer<HomeController> localizer,
        ILogger<HomeController> logger)
    {
        _localizer = localizer;
        _logger = logger;
    }

    public IActionResult Index()
    {
        // Simple string access
        ViewData["Message"] = _localizer["Welcome"];

        // Parameterized string
        ViewData["Greeting"] = _localizer["WelcomeUser", User.Identity.Name];

        // Check if resource exists
        LocalizedString localizedString = _localizer["OptionalMessage"];
        if (!localizedString.ResourceNotFound)
        {
            ViewData["OptionalMessage"] = localizedString.Value;
        }

        return View();
    }
}
```

### Shared Resources Pattern

```csharp
// Shared resource class (no implementation needed)
public class SharedResources
{
}

// Register shared resources
builder.Services.AddLocalization();

// Use shared resources in controller
public class ProductController : Controller
{
    private readonly IStringLocalizer<SharedResources> _sharedLocalizer;

    public ProductController(IStringLocalizer<SharedResources> sharedLocalizer)
    {
        _sharedLocalizer = sharedLocalizer;
    }

    public IActionResult Details(int id)
    {
        ViewData["Title"] = _sharedLocalizer["ProductDetails"];
        return View();
    }
}
```

### Resource File Structure for Controllers

```
Resources/
├── Controllers/
│   ├── HomeController.resx             # Default culture
│   ├── HomeController.es-MX.resx       # Mexican Spanish
│   ├── HomeController.fr-CA.resx       # Canadian French
│   ├── ProductController.resx
│   └── ProductController.es-MX.resx
└── SharedResources.resx                # Shared across controllers
    └── SharedResources.es-MX.resx
```

## IViewLocalizer

Localizer specifically for Razor views.

### Usage in Views

```csharp
@using Microsoft.AspNetCore.Mvc.Localization
@inject IViewLocalizer Localizer

<h1>@Localizer["PageTitle"]</h1>
<p>@Localizer["Description"]</p>

<!-- With parameters -->
<p>@Localizer["WelcomeMessage", Model.UserName]</p>

<!-- HTML content (auto-escaped) -->
<div>@Localizer["RichContent"]</div>
```

### Resource File Structure for Views

```
Resources/
└── Views/
    ├── Home/
    │   ├── Index.resx                  # Default
    │   ├── Index.es-MX.resx            # Spanish
    │   ├── About.resx
    │   └── About.es-MX.resx
    ├── Shared/
    │   ├── _Layout.resx
    │   └── _Layout.es-MX.resx
    └── Product/
        ├── Details.resx
        └── Details.es-MX.resx
```

### LanguageViewLocationExpanderFormat

Controls how view localization resolves resource files:

```csharp
builder.Services.AddControllersWithViews()
    .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix);
```

**Options:**
- `Suffix` (recommended): `Views.Home.Index.es-MX.resx`
- `SubFolder`: `es-MX/Views.Home.Index.resx`

## IHtmlLocalizer

Localizer for HTML content with safe rendering.

### Usage

```csharp
@inject IHtmlLocalizer<IndexModel> HtmlLocalizer

<!-- HTML content rendered safely -->
<div>
    @HtmlLocalizer["RichContent"]
</div>

<!-- In .resx -->
<!-- <value><strong>Bold text</strong> and <em>italic text</em></value> -->
<!-- Rendered: <strong>Bold text</strong> and <em>italic text</em> -->
```

### IHtmlLocalizer vs IViewLocalizer

```csharp
// IViewLocalizer - HTML escaped
@inject IViewLocalizer ViewLocalizer
<div>@ViewLocalizer["Content"]</div>
<!-- Output: &lt;strong&gt;Text&lt;/strong&gt; -->

// IHtmlLocalizer - HTML rendered
@inject IHtmlLocalizer<MyModel> HtmlLocalizer
<div>@HtmlLocalizer["Content"]</div>
<!-- Output: <strong>Text</strong> -->
```

## IStringLocalizerFactory

Factory for creating localizers dynamically.

### Usage

```csharp
public class DynamicLocalizationService
{
    private readonly IStringLocalizerFactory _factory;

    public DynamicLocalizationService(IStringLocalizerFactory factory)
    {
        _factory = factory;
    }

    public string GetLocalizedString(string resourceType, string key)
    {
        // Create localizer for specific type
        Type type = Type.GetType(resourceType);
        if (type != null)
        {
            var localizer = _factory.Create(type);
            return localizer[key].Value;
        }

        return key;
    }

    public string GetLocalizedStringFromBaseName(string baseName, string location, string key)
    {
        // Create localizer with base name and location
        var localizer = _factory.Create(baseName, location);
        return localizer[key].Value;
    }
}
```

## RequestLocalizationMiddleware

### Culture Providers

The middleware determines culture using configured providers in order:

#### 1. QueryStringRequestCultureProvider

```csharp
// Reads culture from query string
// URL: https://example.com?culture=es-MX&ui-culture=es-MX

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.RequestCultureProviders.Insert(0, new QueryStringRequestCultureProvider
    {
        QueryStringKey = "culture",        // Default: "culture"
        UIQueryStringKey = "ui-culture"    // Default: "ui-culture"
    });
});
```

#### 2. CookieRequestCultureProvider

```csharp
// Reads culture from cookie
// Cookie: .AspNetCore.Culture=c=es-MX|uic=es-MX

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.RequestCultureProviders.Insert(0, new CookieRequestCultureProvider
    {
        CookieName = ".AspNetCore.Culture"  // Default
    });
});

// Setting the cookie
Response.Cookies.Append(
    CookieRequestCultureProvider.DefaultCookieName,
    CookieRequestCultureProvider.MakeCookieValue(new RequestCulture("es-MX")),
    new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1) }
);
```

#### 3. AcceptLanguageHeaderRequestCultureProvider

```csharp
// Reads from HTTP Accept-Language header
// Header: Accept-Language: es-MX,es;q=0.9,en;q=0.8

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.RequestCultureProviders.Add(new AcceptLanguageHeaderRequestCultureProvider
    {
        MaximumAcceptLanguageHeaderValuesToTry = 3  // Try top 3 languages
    });
});
```

#### 4. Custom Culture Provider

```csharp
public class UserProfileCultureProvider : RequestCultureProvider
{
    public override async Task<ProviderCultureResult> DetermineProviderCultureResult(HttpContext httpContext)
    {
        // Get culture from user profile in database
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userService = httpContext.RequestServices.GetRequiredService<IUserService>();
            var userCulture = await userService.GetUserCultureAsync(userId);

            if (!string.IsNullOrEmpty(userCulture))
            {
                return new ProviderCultureResult(userCulture);
            }
        }

        return await NullProviderCultureResult;
    }
}

// Register custom provider
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.RequestCultureProviders.Insert(0, new UserProfileCultureProvider());
});
```

### Culture Switching

#### Controller Action for Culture Switching

```csharp
public class CultureController : Controller
{
    [HttpPost]
    public IActionResult SetCulture(string culture, string returnUrl)
    {
        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                SameSite = SameSiteMode.Lax
            }
        );

        return LocalRedirect(returnUrl);
    }
}
```

#### View Component for Culture Selector

```csharp
public class CultureSelectorViewComponent : ViewComponent
{
    private readonly IOptions<RequestLocalizationOptions> _localizationOptions;

    public CultureSelectorViewComponent(IOptions<RequestLocalizationOptions> localizationOptions)
    {
        _localizationOptions = localizationOptions;
    }

    public IViewComponentResult Invoke()
    {
        var cultureFeature = HttpContext.Features.Get<IRequestCultureFeature>();
        var currentCulture = cultureFeature?.RequestCulture.Culture ?? CultureInfo.CurrentCulture;

        var model = new CultureSelectorViewModel
        {
            SupportedCultures = _localizationOptions.Value.SupportedUICultures.ToList(),
            CurrentCulture = currentCulture
        };

        return View(model);
    }
}
```

#### Razor View for Culture Selector

```html
@model CultureSelectorViewModel

<form asp-controller="Culture" asp-action="SetCulture" method="post">
    <input type="hidden" name="returnUrl" value="@Context.Request.Path" />
    <select name="culture" onchange="this.form.submit()">
        @foreach (var culture in Model.SupportedCultures)
        {
            <option value="@culture.Name" selected="@(culture.Name == Model.CurrentCulture.Name)">
                @culture.NativeName
            </option>
        }
    </select>
</form>
```

## DataAnnotations Localization

### Enabling DataAnnotations Localization

```csharp
builder.Services.AddControllersWithViews()
    .AddDataAnnotationsLocalization(options =>
    {
        // Use shared resources for DataAnnotations
        options.DataAnnotationLocalizerProvider = (type, factory) =>
            factory.Create(typeof(SharedResources));
    });
```

### Localizing Validation Messages

**Model with DataAnnotations:**
```csharp
public class LoginViewModel
{
    [Required(ErrorMessage = "EmailRequired")]
    [EmailAddress(ErrorMessage = "EmailInvalid")]
    public string Email { get; set; }

    [Required(ErrorMessage = "PasswordRequired")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "PasswordLength")]
    public string Password { get; set; }
}
```

**SharedResources.resx:**
```xml
<data name="EmailRequired" xml:space="preserve">
  <value>Email is required.</value>
</data>
<data name="EmailInvalid" xml:space="preserve">
  <value>Email format is invalid.</value>
</data>
<data name="PasswordRequired" xml:space="preserve">
  <value>Password is required.</value>
</data>
<data name="PasswordLength" xml:space="preserve">
  <value>Password must be between {2} and {1} characters.</value>
  <comment>{2} = MinimumLength, {1} = MaximumLength</comment>
</data>
```

**SharedResources.es-MX.resx:**
```xml
<data name="EmailRequired" xml:space="preserve">
  <value>El correo electrónico es obligatorio.</value>
</data>
<data name="EmailInvalid" xml:space="preserve">
  <value>El formato del correo electrónico no es válido.</value>
</data>
<data name="PasswordRequired" xml:space="preserve">
  <value>La contraseña es obligatoria.</value>
</data>
<data name="PasswordLength" xml:space="preserve">
  <value>La contraseña debe tener entre {2} y {1} caracteres.</value>
</data>
```

### Localizing Display Names

```csharp
public class UserProfile
{
    [Display(Name = "FullName")]
    public string FullName { get; set; }

    [Display(Name = "EmailAddress")]
    public string Email { get; set; }

    [Display(Name = "PhoneNumber")]
    public string Phone { get; set; }
}
```

## Razor Pages Localization

### PageModel with IStringLocalizer

```csharp
public class IndexModel : PageModel
{
    private readonly IStringLocalizer<IndexModel> _localizer;

    public IndexModel(IStringLocalizer<IndexModel> localizer)
    {
        _localizer = localizer;
    }

    public string WelcomeMessage { get; set; }

    public void OnGet()
    {
        WelcomeMessage = _localizer["Welcome"];
    }
}
```

### Razor Page with IViewLocalizer

```cshtml
@page
@model IndexModel
@inject IViewLocalizer Localizer

<h1>@Localizer["PageTitle"]</h1>
<p>@Model.WelcomeMessage</p>
<p>@Localizer["Description"]</p>
```

### Resource File Structure for Razor Pages

```
Resources/
└── Pages/
    ├── Index.resx
    ├── Index.es-MX.resx
    ├── About.resx
    └── About.es-MX.resx
```

## API Localization

### Localizing API Responses

```csharp
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IStringLocalizer<ProductsController> _localizer;

    public ProductsController(IStringLocalizer<ProductsController> localizer)
    {
        _localizer = localizer;
    }

    [HttpGet("{id}")]
    public ActionResult<ProductResponse> Get(int id)
    {
        var product = _productService.GetProduct(id);
        if (product == null)
        {
            return NotFound(new { error = _localizer["ProductNotFound"].Value });
        }

        return Ok(new ProductResponse
        {
            Name = product.Name,
            Description = _localizer[$"Product_{id}_Description"].Value
        });
    }
}
```

## Best Practices

### 1. Use Typed Localizers

```csharp
// ✓ Good - strongly typed
private readonly IStringLocalizer<HomeController> _localizer;

// ❌ Bad - untyped
private readonly IStringLocalizer _localizer;
```

### 2. Shared Resources for Common Strings

```csharp
// Shared resources for common UI strings
public class SharedResources { }

// Register and use
builder.Services.AddLocalization();

public class MyController : Controller
{
    private readonly IStringLocalizer<SharedResources> _sharedLocalizer;
}
```

### 3. Check ResourceNotFound

```csharp
LocalizedString localizedString = _localizer["Key"];
if (localizedString.ResourceNotFound)
{
    _logger.LogWarning($"Missing resource: {localizedString.Name}");
}
string value = localizedString.Value;
```

### 4. Culture Provider Order Matters

```csharp
// Order: Most specific → Least specific
options.RequestCultureProviders = new List<IRequestCultureProvider>
{
    new QueryStringRequestCultureProvider(),        // Override for testing
    new CookieRequestCultureProvider(),             // User preference
    new AcceptLanguageHeaderRequestCultureProvider() // Browser default
};
```

## Summary

- **IStringLocalizer**: Primary interface for localization in ASP.NET Core
- **IViewLocalizer**: Specialized for Razor views
- **IHtmlLocalizer**: For HTML content with safe rendering
- **RequestLocalizationMiddleware**: Automatic culture negotiation
- **Culture Providers**: Query string, cookie, HTTP header, custom
- **DataAnnotations**: Automatic validation message localization
- **Shared Resources**: Common pattern for shared strings across application

ASP.NET Core localization provides a modern, dependency injection-based approach to building multilingual applications.
