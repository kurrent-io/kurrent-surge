# Testing Strategies for Localized Applications

## Overview

Testing localized applications ensures resources are correctly loaded, formatted appropriately for different cultures, and that the application gracefully handles culture switching and fallback scenarios.

## Unit Testing Resources

### Basic Resource Loading Test

```csharp
[Test]
public void resource_can_be_loaded()
{
    // Arrange
    CultureInfo.CurrentUICulture = new CultureInfo("en-US");

    // Act
    string value = Resources.WelcomeMessage;

    // Assert
    value.ShouldNotBeNullOrEmpty();
    value.ShouldBe("Welcome");
}
```

### Culture-Specific Resource Test

```csharp
[Test]
[Arguments("en-US", "Welcome")]
[Arguments("es-MX", "¡Bienvenido!")]
[Arguments("fr-CA", "Bienvenue")]
public void resource_returns_correct_culture_value(string cultureName, string expected)
{
    // Arrange
    CultureInfo.CurrentUICulture = new CultureInfo(cultureName);

    // Act
    string actual = Resources.WelcomeMessage;

    // Assert
    actual.ShouldBe(expected);
}
```

### Resource Fallback Test

```csharp
[Test]
public void unsupported_culture_falls_back_to_parent()
{
    // Arrange - Request specific culture without specific resources
    CultureInfo.CurrentUICulture = new CultureInfo("es-ES");  // Spanish (Spain)

    // Act - Should fall back to "es" (neutral Spanish)
    string value = Resources.WelcomeMessage;

    // Assert
    value.ShouldBe("Bienvenido");  // From Resources.es.resx
}
```

### Composite Formatting Test

```csharp
[Test]
public void composite_formatted_resource_works()
{
    // Arrange
    CultureInfo.CurrentUICulture = new CultureInfo("en-US");

    // Act
    string message = string.Format(Resources.UserGreeting, "Alice", 5);

    // Assert
    message.ShouldBe("Hello, Alice! You have 5 new messages.");
}
```

### Culture-Specific Formatting Test

```csharp
[Test]
[Arguments("en-US", "$1,234.56")]
[Arguments("es-MX", "$1,234.56")]
[Arguments("de-DE", "1.234,56 €")]
public void currency_formats_correctly_per_culture(string cultureName, string expected)
{
    // Arrange
    CultureInfo.CurrentCulture = new CultureInfo(cultureName);
    CultureInfo.CurrentUICulture = new CultureInfo(cultureName);
    decimal price = 1234.56m;

    // Act
    string formatted = string.Format(Resources.PriceFormat, price);

    // Assert
    formatted.ShouldContain(expected.Substring(1)); // Check number format
}
```

## Mocking IStringLocalizer

### Mock Setup for Unit Tests

```csharp
public class HomeControllerTests
{
    private readonly Mock<IStringLocalizer<HomeController>> _mockLocalizer;
    private readonly HomeController _controller;

    public HomeControllerTests()
    {
        _mockLocalizer = new Mock<IStringLocalizer<HomeController>>();
        _controller = new HomeController(_mockLocalizer.Object);
    }

    [Test]
    public void index_returns_localized_message()
    {
        // Arrange
        var localizedString = new LocalizedString("WelcomeMessage", "Welcome!");
        _mockLocalizer
            .Setup(l => l["WelcomeMessage"])
            .Returns(localizedString);

        // Act
        var result = _controller.Index() as ViewResult;

        // Assert
        result.ViewData["Message"].ShouldBe("Welcome!");
    }

    [Test]
    public void index_uses_parameterized_resource()
    {
        // Arrange
        _mockLocalizer
            .Setup(l => l["UserGreeting", "Alice"])
            .Returns(new LocalizedString("UserGreeting", "Hello, Alice!"));

        // Act
        var result = _controller.Greet("Alice") as ViewResult;

        // Assert
        result.ViewData["Greeting"].ShouldBe("Hello, Alice!");
    }
}
```

### Localizer Test Helper

```csharp
public static class LocalizerTestHelper
{
    public static IStringLocalizer<T> CreateMockLocalizer<T>(
        Dictionary<string, string> resources)
    {
        var mock = new Mock<IStringLocalizer<T>>();

        foreach (var kvp in resources)
        {
            var localizedString = new LocalizedString(kvp.Key, kvp.Value);
            mock.Setup(l => l[kvp.Key]).Returns(localizedString);
        }

        // Setup GetAllStrings
        mock.Setup(l => l.GetAllStrings(It.IsAny<bool>()))
            .Returns(resources.Select(kvp =>
                new LocalizedString(kvp.Key, kvp.Value)));

        return mock.Object;
    }
}

// Usage
[Test]
public void test_with_mock_localizer()
{
    var resources = new Dictionary<string, string>
    {
        { "Welcome", "Welcome!" },
        { "Goodbye", "Goodbye!" }
    };

    var localizer = LocalizerTestHelper.CreateMockLocalizer<MyClass>(resources);

    var service = new MyService(localizer);
    var result = service.GetWelcomeMessage();

    result.ShouldBe("Welcome!");
}
```

## Integration Testing with TestServer

### ASP.NET Core Integration Test

```csharp
public class LocalizationIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public LocalizationIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Test]
    [Arguments("en-US", "Welcome")]
    [Arguments("es-MX", "Bienvenido")]
    [Arguments("fr-CA", "Bienvenue")]
    public async Task home_page_displays_correct_culture(string culture, string expected)
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.Add(
            new StringWithQualityHeaderValue(culture));

        // Act
        var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.EnsureSuccessStatusCode();
        content.ShouldContain(expected);
    }
}
```

### Cookie-Based Culture Switching Test

```csharp
[Test]
public async Task culture_cookie_changes_language()
{
    // Arrange
    var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    // Act - Set culture to Spanish
    var setCultureResponse = await client.PostAsync(
        "/Culture/SetCulture?culture=es-MX&returnUrl=/",
        null);

    // Extract cookie
    var cookies = setCultureResponse.Headers.GetValues("Set-Cookie");
    client.DefaultRequestHeaders.Add("Cookie", cookies);

    // Request home page
    var response = await client.GetAsync("/");
    var content = await response.Content.ReadAsStringAsync();

    // Assert
    content.ShouldContain("Bienvenido");
}
```

## Resource Completeness Validation

### Check All Cultures Have Required Keys

```csharp
[Test]
public void all_cultures_have_complete_resources()
{
    // Arrange
    var baseKeys = GetResourceKeys("Resources.resx");
    var cultures = new[] { "es", "es-MX", "fr", "fr-CA" };

    foreach (var culture in cultures)
    {
        // Act
        var cultureKeys = GetResourceKeys($"Resources.{culture}.resx");
        var missingKeys = baseKeys.Except(cultureKeys).ToList();

        // Assert
        missingKeys.ShouldBeEmpty(
            $"Culture '{culture}' is missing keys: {string.Join(", ", missingKeys)}");
    }
}

private HashSet<string> GetResourceKeys(string resxPath)
{
    var keys = new HashSet<string>();

    using (var reader = new ResXResourceReader(resxPath))
    {
        foreach (DictionaryEntry entry in reader)
        {
            keys.Add((string)entry.Key);
        }
    }

    return keys;
}
```

### Validate No Untranslated Placeholders

```csharp
[Test]
public void translated_resources_have_no_placeholders()
{
    // Arrange
    var cultures = new[] { "es", "es-MX", "fr", "fr-CA" };
    var placeholderPattern = new Regex(@"\[TODO|TRANSLATE|XXX\]", RegexOptions.IgnoreCase);

    foreach (var culture in cultures)
    {
        // Act
        var resources = GetAllResourceValues($"Resources.{culture}.resx");
        var untranslated = resources.Where(kvp =>
            kvp.Value != null && placeholderPattern.IsMatch(kvp.Value)).ToList();

        // Assert
        untranslated.ShouldBeEmpty(
            $"Culture '{culture}' has untranslated resources: {string.Join(", ", untranslated.Select(kvp => kvp.Key))}");
    }
}

private Dictionary<string, string> GetAllResourceValues(string resxPath)
{
    var resources = new Dictionary<string, string>();

    using (var reader = new ResXResourceReader(resxPath))
    {
        foreach (DictionaryEntry entry in reader)
        {
            resources[(string)entry.Key] = entry.Value?.ToString();
        }
    }

    return resources;
}
```

## Testing Culture-Specific Behavior

### Date Formatting Test

```csharp
[Test]
[Arguments("en-US", "12/25/2025")]
[Arguments("es-MX", "25/12/2025")]
[Arguments("de-DE", "25.12.2025")]
public void dates_format_correctly_per_culture(string cultureName, string expected)
{
    // Arrange
    CultureInfo.CurrentCulture = new CultureInfo(cultureName);
    var date = new DateTime(2025, 12, 25);

    // Act
    string formatted = date.ToString("d");  // Short date format

    // Assert
    formatted.ShouldBe(expected);
}
```

### Number Formatting Test

```csharp
[Test]
[Arguments("en-US", "1,234.56")]
[Arguments("de-DE", "1.234,56")]
[Arguments("fr-FR", "1 234,56")]
public void numbers_format_correctly_per_culture(string cultureName, string expected)
{
    // Arrange
    CultureInfo.CurrentCulture = new CultureInfo(cultureName);
    double number = 1234.56;

    // Act
    string formatted = number.ToString("N2");

    // Assert
    formatted.ShouldBe(expected);
}
```

## Pseudo-Localization Testing

### Pseudo-Localizer for Testing

```csharp
public static class PseudoLocalizer
{
    public static string Pseudolocalize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Add padding to detect truncation issues
        var padded = $"[!!! {text} !!!]";

        // Replace characters with accented versions
        var sb = new StringBuilder(padded);
        var replacements = new Dictionary<char, char>
        {
            { 'a', 'á' }, { 'A', 'Á' },
            { 'e', 'é' }, { 'E', 'É' },
            { 'i', 'í' }, { 'I', 'Í' },
            { 'o', 'ó' }, { 'O', 'Ó' },
            { 'u', 'ú' }, { 'U', 'Ú' }
        };

        for (int i = 0; i < sb.Length; i++)
        {
            if (replacements.TryGetValue(sb[i], out char replacement))
            {
                sb[i] = replacement;
            }
        }

        return sb.ToString();
    }
}

// Generate pseudo-localized resource file
[Test]
public void generate_pseudo_localized_resources()
{
    using (var reader = new ResXResourceReader("Resources.resx"))
    using (var writer = new ResXResourceWriter("Resources.qps-ploc.resx"))
    {
        foreach (DictionaryEntry entry in reader)
        {
            string key = (string)entry.Key;
            string value = entry.Value?.ToString();

            if (value != null)
            {
                value = PseudoLocalizer.Pseudolocalize(value);
            }

            writer.AddResource(key, value);
        }

        writer.Generate();
    }
}
```

## Performance Testing

### Measure Resource Load Time

```csharp
[Test]
public void resource_loading_performance_is_acceptable()
{
    // Arrange
    const int iterations = 1000;
    var stopwatch = Stopwatch.StartNew();

    // Act
    for (int i = 0; i < iterations; i++)
    {
        _ = Resources.WelcomeMessage;
    }

    stopwatch.Stop();

    // Assert - should be fast after caching
    var averageMs = stopwatch.ElapsedMilliseconds / (double)iterations;
    averageMs.ShouldBeLessThan(0.1, "Resource access should be cached and fast");
}
```

### Measure Culture Switching Performance

```csharp
[Test]
public void culture_switching_performance()
{
    // Arrange
    var cultures = new[] { "en-US", "es-MX", "fr-CA", "de-DE" };
    var stopwatch = Stopwatch.StartNew();

    // Act
    for (int i = 0; i < 100; i++)
    {
        foreach (var cultureName in cultures)
        {
            CultureInfo.CurrentUICulture = new CultureInfo(cultureName);
            _ = Resources.WelcomeMessage;
        }
    }

    stopwatch.Stop();

    // Assert
    stopwatch.ElapsedMilliseconds.ShouldBeLessThan(1000,
        "Culture switching with resource access should be fast");
}
```

## Test Helpers and Utilities

### Culture Test Fixture

```csharp
public class CultureTestFixture : IDisposable
{
    private readonly CultureInfo _originalCulture;
    private readonly CultureInfo _originalUICulture;

    public CultureTestFixture(string cultureName)
    {
        _originalCulture = CultureInfo.CurrentCulture;
        _originalUICulture = CultureInfo.CurrentUICulture;

        var culture = new CultureInfo(cultureName);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUICulture;
    }
}

// Usage
[Test]
public void test_in_spanish_culture()
{
    using (new CultureTestFixture("es-MX"))
    {
        // Test code runs in es-MX culture
        string value = Resources.WelcomeMessage;
        value.ShouldBe("¡Bienvenido!");
    }
    // Culture automatically restored
}
```

## Best Practices

### 1. Always Restore Culture After Tests

```csharp
// ✓ Good - using fixture or IDisposable
using (new CultureTestFixture("es"))
{
    // Test code
}

// ✓ Good - manual restore
var originalCulture = CultureInfo.CurrentUICulture;
try
{
    CultureInfo.CurrentUICulture = new CultureInfo("es");
    // Test code
}
finally
{
    CultureInfo.CurrentUICulture = originalCulture;
}
```

### 2. Test Both Exact and Fallback Cultures

```csharp
[Test]
[Arguments("es-MX", "¡Bienvenido!")]  // Exact match
[Arguments("es-ES", "Bienvenido")]    // Falls back to 'es'
[Arguments("es-AR", "Bienvenido")]    // Falls back to 'es'
public void resource_fallback_works(string culture, string expected)
{
    CultureInfo.CurrentUICulture = new CultureInfo(culture);
    Resources.WelcomeMessage.ShouldBe(expected);
}
```

### 3. Validate Resource Completeness in CI

```csharp
[Test]
[Category("CI")]
public void all_resource_files_are_complete()
{
    var baseKeys = GetResourceKeys("Resources.resx");
    var cultures = Directory.GetFiles("Resources", "Resources.*.resx");

    foreach (var cultureFile in cultures)
    {
        var keys = GetResourceKeys(cultureFile);
        var missing = baseKeys.Except(keys).ToList();

        missing.ShouldBeEmpty($"File '{Path.GetFileName(cultureFile)}' is missing keys");
    }
}
```

### 4. Use Parameterized Tests for Multiple Cultures

```csharp
[Test]
[Arguments("en-US")]
[Arguments("es-MX")]
[Arguments("fr-CA")]
[Arguments("de-DE")]
public void resource_exists_for_all_supported_cultures(string cultureName)
{
    CultureInfo.CurrentUICulture = new CultureInfo(cultureName);
    string value = Resources.WelcomeMessage;
    value.ShouldNotBeNullOrEmpty();
}
```

## Summary

- **Unit Tests**: Verify resource loading and culture-specific values
- **Mocking**: Use Mock<IStringLocalizer<T>> for isolated unit tests
- **Integration Tests**: Test full request pipeline with TestServer
- **Completeness**: Validate all cultures have required resources
- **Pseudo-Localization**: Detect UI truncation and encoding issues
- **Performance**: Ensure resource access is fast after caching
- **Best Practices**: Restore culture, test fallback, automate validation

Comprehensive testing ensures localized applications work correctly across all supported cultures and gracefully handle edge cases.
