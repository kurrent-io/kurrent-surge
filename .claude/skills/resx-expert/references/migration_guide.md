# Migration Guide

## Overview

This guide covers migrating from various localization approaches to `.resx`-based localization, including hardcoded strings, legacy resource formats, database-driven systems, and third-party libraries.

## Migrating from Hardcoded Strings

### Identifying Hardcoded Strings

**Step 1: Find Hardcoded Strings**
```bash
# Search for string literals (PowerShell)
Get-ChildItem -Recurse -Filter *.cs | Select-String '"[A-Za-z ]+"' | Where-Object { $_.Line -notmatch '^\s*//' }

# Grep (Linux/Mac)
grep -rn --include="*.cs" '"[A-Za-z ]*"' src/
```

**Step 2: Analyze and Categorize**
- User-facing messages (high priority)
- Log messages (medium priority - consider leaving)
- Internal constants (low priority - may not need localization)

### Extraction Process

**Manual Extraction:**
```csharp
// Before
public string GetWelcomeMessage()
{
    return "Welcome to our application!";
}

// After
public string GetWelcomeMessage()
{
    return Resources.WelcomeMessage;
}
```

**Add to Resources.resx:**
```xml
<data name="WelcomeMessage" xml:space="preserve">
  <value>Welcome to our application!</value>
  <comment>Displayed on home page when user logs in</comment>
</data>
```

### Automated Extraction Tool

```csharp
public class StringExtractor
{
    public void ExtractStringsFromFile(string filePath, string outputResxPath)
    {
        var code = File.ReadAllText(filePath);
        var regex = new Regex(@"""([A-Za-z][A-Za-z0-9\s.,!?'-]{10,})""");
        var matches = regex.Matches(code);

        var resources = new Dictionary<string, string>();

        foreach (Match match in matches)
        {
            string value = match.Groups[1].Value;
            string key = GenerateKey(value);

            if (!resources.ContainsKey(key))
            {
                resources[key] = value;
            }
        }

        // Write to .resx
        using (var writer = new ResXResourceWriter(outputResxPath))
        {
            foreach (var kvp in resources)
            {
                writer.AddResource(new ResXDataNode(kvp.Key, kvp.Value)
                {
                    Comment = "[TODO: Review and categorize]"
                });
            }

            writer.Generate();
        }
    }

    private string GenerateKey(string value)
    {
        // Simple key generation: first 3 words, PascalCase
        var words = value.Split(' ').Take(3);
        return string.Join("", words.Select(w =>
            char.ToUpper(w[0]) + w.Substring(1).ToLower()));
    }
}
```

## Migrating from Legacy .resources Files

### Binary .resources to .resx

```csharp
public static void ConvertResourcesToResx(string resourcesPath, string resxPath)
{
    using (var reader = new ResourceReader(resourcesPath))
    using (var writer = new ResXResourceWriter(resxPath))
    {
        foreach (DictionaryEntry entry in reader)
        {
            string key = (string)entry.Key;
            object value = entry.Value;

            writer.AddResource(new ResXDataNode(key, value)
            {
                Comment = "[Migrated from legacy .resources file]"
            });
        }

        writer.Generate();
    }
}
```

### .txt Resource Files to .resx

```csharp
public static void ConvertTextResourcesToResx(string txtPath, string resxPath)
{
    var lines = File.ReadAllLines(txtPath);

    using (var writer = new ResXResourceWriter(resxPath))
    {
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                continue;

            var parts = line.Split('=', 2);
            if (parts.Length == 2)
            {
                string key = parts[0].Trim();
                string value = parts[1].Trim();

                writer.AddResource(key, value);
            }
        }

        writer.Generate();
    }
}
```

## Migrating from Database-Driven Localization

### Export Database Resources to .resx

```csharp
public async Task ExportDatabaseResourcesToResx(
    string connectionString,
    string culture,
    string outputPath)
{
    using (var connection = new SqlConnection(connectionString))
    using (var writer = new ResXResourceWriter(outputPath))
    {
        await connection.OpenAsync();

        var query = @"
            SELECT ResourceKey, ResourceValue, Category, Comment
            FROM LocalizedResources
            WHERE Culture = @Culture OR Culture IS NULL
            ORDER BY ResourceKey";

        using (var command = new SqlCommand(query, connection))
        {
            command.Parameters.AddWithValue("@Culture", culture);

            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    string key = reader.GetString(0);
                    string value = reader.GetString(1);
                    string comment = reader.IsDBNull(3) ? null : reader.GetString(3);

                    writer.AddResource(new ResXDataNode(key, value)
                    {
                        Comment = comment
                    });
                }
            }
        }

        writer.Generate();
    }
}
```

### Hybrid Approach (Database + .resx)

```csharp
public class HybridResourceManager : ResourceManager
{
    private readonly IDbConnection _dbConnection;
    private readonly Dictionary<string, string> _cache = new();

    public HybridResourceManager(
        string baseName,
        Assembly assembly,
        IDbConnection dbConnection) : base(baseName, assembly)
    {
        _dbConnection = dbConnection;
    }

    public override string GetString(string name, CultureInfo culture)
    {
        culture ??= CultureInfo.CurrentUICulture;
        string cacheKey = $"{culture.Name}:{name}";

        // Check cache
        if (_cache.TryGetValue(cacheKey, out string cached))
            return cached;

        // Try database first (dynamic overrides)
        string dbValue = GetStringFromDatabase(name, culture);
        if (dbValue != null)
        {
            _cache[cacheKey] = dbValue;
            return dbValue;
        }

        // Fall back to .resx files
        string resxValue = base.GetString(name, culture);
        if (resxValue != null)
        {
            _cache[cacheKey] = resxValue;
        }

        return resxValue;
    }

    private string GetStringFromDatabase(string name, CultureInfo culture)
    {
        using (var command = _dbConnection.CreateCommand())
        {
            command.CommandText = @"
                SELECT ResourceValue
                FROM LocalizedResources
                WHERE ResourceKey = @Key AND Culture = @Culture";

            var keyParam = command.CreateParameter();
            keyParam.ParameterName = "@Key";
            keyParam.Value = name;
            command.Parameters.Add(keyParam);

            var cultureParam = command.CreateParameter();
            cultureParam.ParameterName = "@Culture";
            cultureParam.Value = culture.Name;
            command.Parameters.Add(cultureParam);

            return command.ExecuteScalar() as string;
        }
    }
}
```

## Migrating from JSON Resource Files

### JSON to .resx Converter

```csharp
public static void ConvertJsonToResx(string jsonPath, string resxPath)
{
    string json = File.ReadAllText(jsonPath);
    var resources = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

    using (var writer = new ResXResourceWriter(resxPath))
    {
        ConvertJsonObject(resources, writer, "");
        writer.Generate();
    }
}

private static void ConvertJsonObject(
    Dictionary<string, JsonElement> obj,
    ResXResourceWriter writer,
    string prefix)
{
    foreach (var kvp in obj)
    {
        string key = string.IsNullOrEmpty(prefix) ? kvp.Key : $"{prefix}.{kvp.Key}";

        if (kvp.Value.ValueKind == JsonValueKind.Object)
        {
            // Nested object - recurse
            var nested = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                kvp.Value.GetRawText());
            ConvertJsonObject(nested, writer, key);
        }
        else if (kvp.Value.ValueKind == JsonValueKind.String)
        {
            // String value - add to resources
            writer.AddResource(key, kvp.Value.GetString());
        }
    }
}
```

**Example:**
```json
{
  "common": {
    "welcome": "Welcome",
    "goodbye": "Goodbye"
  },
  "errors": {
    "notFound": "Not found",
    "serverError": "Server error"
  }
}
```

**Converts to:**
```xml
<data name="common.welcome"><value>Welcome</value></data>
<data name="common.goodbye"><value>Goodbye</value></data>
<data name="errors.notFound"><value>Not found</value></data>
<data name="errors.serverError"><value>Server error</value></data>
```

## Migrating from Third-Party Libraries

### From GetText (.po files)

```csharp
public class PoFileConverter
{
    public void ConvertPoToResx(string poPath, string resxPath)
    {
        var lines = File.ReadAllLines(poPath);
        using (var writer = new ResXResourceWriter(resxPath))
        {
            string currentMsgId = null;
            string currentMsgStr = null;
            string currentComment = null;

            foreach (var line in lines)
            {
                if (line.StartsWith("#"))
                {
                    currentComment = line.Substring(1).Trim();
                }
                else if (line.StartsWith("msgid "))
                {
                    currentMsgId = ExtractQuotedString(line.Substring(6));
                }
                else if (line.StartsWith("msgstr "))
                {
                    currentMsgStr = ExtractQuotedString(line.Substring(7));

                    // Add to resources
                    if (!string.IsNullOrEmpty(currentMsgId) &&
                        !string.IsNullOrEmpty(currentMsgStr))
                    {
                        string key = GenerateKeyFromMsgId(currentMsgId);
                        writer.AddResource(new ResXDataNode(key, currentMsgStr)
                        {
                            Comment = currentComment
                        });
                    }

                    // Reset
                    currentMsgId = null;
                    currentMsgStr = null;
                    currentComment = null;
                }
            }

            writer.Generate();
        }
    }

    private string ExtractQuotedString(string input)
    {
        var match = Regex.Match(input, @"""(.*)""");
        return match.Success ? match.Groups[1].Value : input;
    }

    private string GenerateKeyFromMsgId(string msgId)
    {
        // Convert message ID to PascalCase key
        return Regex.Replace(msgId, @"\b\w", m => m.Value.ToUpper())
                    .Replace(" ", "");
    }
}
```

### From i18next (JavaScript)

```typescript
// TypeScript migration script
import * as fs from 'fs';

interface ResourceTree {
    [key: string]: string | ResourceTree;
}

function convertI18nextToResxFormat(i18nextJson: ResourceTree, prefix: string = ''): Map<string, string> {
    const resources = new Map<string, string>();

    for (const [key, value] of Object.entries(i18nextJson)) {
        const fullKey = prefix ? `${prefix}.${key}` : key;

        if (typeof value === 'string') {
            resources.set(fullKey, value);
        } else {
            // Recursive for nested objects
            const nested = convertI18nextToResxFormat(value, fullKey);
            nested.forEach((v, k) => resources.set(k, v));
        }
    }

    return resources;
}

// Generate PowerShell script to create .resx
function generateResxCreationScript(resources: Map<string, string>, outputPath: string) {
    let script = `
$writer = New-Object System.Resources.ResXResourceWriter("${outputPath}")

`;

    for (const [key, value] of resources) {
        const escapedValue = value.replace(/"/g, '""');
        script += `$writer.AddResource("${key}", "${escapedValue}")\n`;
    }

    script += `
$writer.Generate()
$writer.Dispose()
`;

    return script;
}
```

## Breaking Changes Across .NET Versions

### .NET Framework to .NET Core/.NET 5+

**Changes:**
1. **Namespace**: `System.Resources.Tools` removed, use MSBuild tasks
2. **Strong Naming**: No longer required for satellite assemblies
3. **GAC**: Not supported in .NET Core/.NET 5+

**Migration:**
```xml
<!-- .NET Framework -->
<PropertyGroup>
  <SignAssembly>true</SignAssembly>
  <AssemblyOriginatorKeyFile>Key.snk</AssemblyOriginatorKeyFile>
</PropertyGroup>

<!-- .NET 5+ -->
<PropertyGroup>
  <!-- Strong naming optional -->
  <!-- GAC deployment not supported -->
  <!-- Use local deployment instead -->
</PropertyGroup>
```

### ASP.NET MVC to ASP.NET Core

**Before (MVC):**
```csharp
public class HomeController : Controller
{
    public ActionResult Index()
    {
        ViewBag.Message = Resources.WelcomeMessage;
        return View();
    }
}
```

**After (ASP.NET Core):**
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
        return View();
    }
}
```

## Migration Checklist

### Pre-Migration

- [ ] Audit existing localization approach
- [ ] Identify all localizable strings
- [ ] Document supported cultures
- [ ] Plan resource file organization
- [ ] Set up version control for .resx files

### During Migration

- [ ] Create default .resx files (Resources.resx)
- [ ] Extract hardcoded strings
- [ ] Convert legacy resources
- [ ] Generate culture-specific files
- [ ] Update code to use ResourceManager or IStringLocalizer
- [ ] Configure build system (.csproj, MSBuild)
- [ ] Set up CI/CD validation

### Post-Migration

- [ ] Test all cultures
- [ ] Verify fallback behavior
- [ ] Validate satellite assembly generation
- [ ] Update documentation
- [ ] Train team on new workflow
- [ ] Monitor for missing resources in production

## Gradual Migration Strategy

### Hybrid Resource Provider

```csharp
public class HybridStringLocalizer : IStringLocalizer
{
    private readonly IStringLocalizer _resxLocalizer;
    private readonly ILegacyResourceProvider _legacyProvider;

    public HybridStringLocalizer(
        IStringLocalizer resxLocalizer,
        ILegacyResourceProvider legacyProvider)
    {
        _resxLocalizer = resxLocalizer;
        _legacyProvider = legacyProvider;
    }

    public LocalizedString this[string name]
    {
        get
        {
            // Try new .resx first
            var resxValue = _resxLocalizer[name];
            if (!resxValue.ResourceNotFound)
                return resxValue;

            // Fall back to legacy system
            var legacyValue = _legacyProvider.GetString(name);
            if (legacyValue != null)
                return new LocalizedString(name, legacyValue);

            return resxValue; // Return not found
        }
    }

    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var template = this[name];
            if (!template.ResourceNotFound)
            {
                return new LocalizedString(
                    name,
                    string.Format(template.Value, arguments));
            }

            return template;
        }
    }

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        return _resxLocalizer.GetAllStrings(includeParentCultures);
    }
}
```

## Best Practices

### 1. Migrate Incrementally

Start with high-priority user-facing strings, then internal messages, then logs.

### 2. Preserve Context

Add comments to .resx files explaining where strings are used.

### 3. Maintain Backward Compatibility

Use hybrid approaches during migration to avoid breaking existing code.

### 4. Test Thoroughly

Test all cultures after migration to ensure nothing was lost.

### 5. Document Changes

Update team documentation and provide training on new workflow.

## Summary

- **Hardcoded Strings**: Extract to .resx with automated tools
- **Legacy Formats**: Convert binary .resources, .txt, .po files
- **Database**: Export to .resx, or use hybrid approach
- **JSON**: Convert nested structure to flat keys
- **Third-Party**: Convert GetText, i18next to .resx format
- **.NET Migration**: Update from Framework to Core patterns
- **Strategy**: Use hybrid providers for gradual migration
- **Checklist**: Follow pre-, during, and post-migration steps

Migrating to .resx-based localization provides a standard, maintainable, and tooling-friendly approach to multi-language applications.
