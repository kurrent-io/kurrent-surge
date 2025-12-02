# Satellite Assemblies

## Overview

Satellite assemblies are .NET assemblies that contain culture-specific resources (localized strings, images, etc.) separate from the main application assembly. They enable efficient deployment and loading of localized resources without modifying the main executable.

## Architecture

### Assembly Structure

```
MyApp/
├── MyApp.exe                      # Main executable
├── MyApp.dll                      # Main assembly (contains default resources)
├── es/                            # Spanish culture folder
│   └── MyApp.resources.dll        # Spanish satellite assembly
├── es-MX/                         # Mexican Spanish culture folder
│   └── MyApp.resources.dll        # Mexican Spanish satellite assembly
├── fr/                            # French culture folder
│   └── MyApp.resources.dll        # French satellite assembly
└── fr-CA/                         # Canadian French culture folder
    └── MyApp.resources.dll        # Canadian French satellite assembly
```

### Satellite Assembly Contents

A satellite assembly contains:
- Compiled `.resources` files for a specific culture
- Assembly metadata (culture, version, public key token)
- No executable code (only resources)

## Building Satellite Assemblies

### Automatic Build with MSBuild

MSBuild automatically creates satellite assemblies when culture-specific `.resx` files exist.

**Project Structure:**
```
MyApp/
└── Resources/
    ├── Resources.resx              # Default (embedded in main assembly)
    ├── Resources.es.resx           # Spanish
    ├── Resources.es-MX.resx        # Mexican Spanish
    └── Resources.fr.resx           # French
```

**Build Output:**
```
bin/Debug/net9.0/
├── MyApp.dll
├── es/
│   └── MyApp.resources.dll
├── es-MX/
│   └── MyApp.resources.dll
└── fr/
    └── MyApp.resources.dll
```

### .csproj Configuration

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <OutputType>Exe</OutputType>

    <!-- Explicitly define satellite cultures (optional but recommended) -->
    <SatelliteCultures>es;es-MX;fr;fr-CA;de;ja</SatelliteCultures>

    <!-- Neutral language (language of default resources) -->
    <NeutralLanguage>en-US</NeutralLanguage>
  </PropertyGroup>

  <ItemGroup>
    <!-- Embedded resources automatically detected -->
    <EmbeddedResource Update="Resources\*.resx">
      <Generator>ResXFileCodeGenerator</Generator>
    </EmbeddedResource>
  </ItemGroup>
</Project>
```

### Manual Build with Assembly Linker (al.exe)

For advanced scenarios, manually create satellite assemblies using `al.exe`:

**Step 1: Compile .resx to .resources**
```bash
resgen Resources.es.resx Resources.es.resources
```

**Step 2: Create Satellite Assembly**
```bash
al /target:lib /embed:Resources.es.resources /culture:es /out:es\MyApp.resources.dll /template:MyApp.dll
```

**Parameters:**
- `/target:lib` - Create a DLL
- `/embed` - Embed the .resources file
- `/culture` - Specify the culture
- `/out` - Output path (must be in culture folder)
- `/template` - Use main assembly as template (copies version, key, etc.)

## Resource Probing

### Probing Algorithm

When ResourceManager requests a resource, it searches in this order:

```
1. GAC (if satellite is signed and installed)
2. ApplicationBase/CultureName/AssemblyName.resources.dll
3. ApplicationBase/CultureName.resources/AssemblyName.resources.dll (legacy)
4. Parent culture folders (if any)
5. Default resources in main assembly
```

**Example for es-MX culture:**
```
1. GAC: Check for MyApp.resources, Version=1.0.0.0, Culture=es-MX
2. C:\MyApp\es-MX\MyApp.resources.dll
3. C:\MyApp\es-MX.resources\MyApp.resources.dll
4. C:\MyApp\es\MyApp.resources.dll (parent culture)
5. C:\MyApp\MyApp.dll (default resources)
```

### Probing Paths

```csharp
// View probing behavior
AppDomain.CurrentDomain.AppendPrivatePath("Resources");

// ResourceManager will now also probe:
// C:\MyApp\Resources\es-MX\MyApp.resources.dll
// C:\MyApp\Resources\es\MyApp.resources.dll
```

### Culture Fallback During Probing

```
Requested Culture: es-MX

Probing Order:
1. es-MX satellite assembly
2. es satellite assembly (neutral Spanish)
3. Default resources (invariant/neutral culture)
```

## Deployment

### xcopy Deployment

```bash
# Copy main application
xcopy /s /y MyApp\bin\Release\net9.0\*.dll C:\Deploy\MyApp\
xcopy /s /y MyApp\bin\Release\net9.0\*.exe C:\Deploy\MyApp\

# Copy satellite assemblies (preserves folder structure)
xcopy /s /y MyApp\bin\Release\net9.0\es\*.dll C:\Deploy\MyApp\es\
xcopy /s /y MyApp\bin\Release\net9.0\es-MX\*.dll C:\Deploy\MyApp\es-MX\
xcopy /s /y MyApp\bin\Release\net9.0\fr\*.dll C:\Deploy\MyApp\fr\
```

### NuGet Package Deployment

**Package Structure:**
```
MyPackage.nupkg
├── lib/
│   └── net9.0/
│       ├── MyApp.dll
│       ├── es/
│       │   └── MyApp.resources.dll
│       ├── es-MX/
│       │   └── MyApp.resources.dll
│       └── fr/
│           └── MyApp.resources.dll
```

**.nuspec File:**
```xml
<?xml version="1.0"?>
<package>
  <metadata>
    <id>MyApp</id>
    <version>1.0.0</version>
    <authors>Your Company</authors>
    <description>Localized application</description>
  </metadata>
  <files>
    <!-- Main assembly -->
    <file src="bin\Release\net9.0\MyApp.dll" target="lib\net9.0" />

    <!-- Satellite assemblies -->
    <file src="bin\Release\net9.0\es\MyApp.resources.dll" target="lib\net9.0\es" />
    <file src="bin\Release\net9.0\es-MX\MyApp.resources.dll" target="lib\net9.0\es-MX" />
    <file src="bin\Release\net9.0\fr\MyApp.resources.dll" target="lib\net9.0\fr" />
  </files>
</package>
```

### Global Assembly Cache (GAC) Deployment

**For strong-named assemblies only:**

```bash
# Install main assembly to GAC
gacutil /i MyApp.dll

# Install satellite assemblies
gacutil /i es\MyApp.resources.dll
gacutil /i es-MX\MyApp.resources.dll
gacutil /i fr\MyApp.resources.dll
```

**Notes:**
- GAC deployment is rare in modern .NET
- Requires strong-name signing
- Primarily used for shared libraries
- .NET Core/.NET 5+ prefer local deployment

## Performance Implications

### Loading Performance

```csharp
// First access loads satellite assembly from disk
CultureInfo.CurrentUICulture = new CultureInfo("es-MX");
string value1 = rm.GetString("Message1"); // Load es-MX satellite (~5-10ms)

// Subsequent access uses cached assembly
string value2 = rm.GetString("Message2"); // Cache hit (~< 1ms)
string value3 = rm.GetString("Message3"); // Cache hit (~< 1ms)
```

### Memory Impact

- **Per Culture**: ~10-50 KB for small resource files, more for large resources
- **Caching**: ResourceManager caches loaded ResourceSets
- **Release**: Call `ResourceManager.ReleaseAllResources()` to free memory

### Lazy Loading

Satellite assemblies are loaded on-demand:

```csharp
var rm = new ResourceManager(typeof(Resources)); // No assembly loaded yet

CultureInfo.CurrentUICulture = new CultureInfo("es");
string value = rm.GetString("Message"); // Loads es satellite now

CultureInfo.CurrentUICulture = new CultureInfo("fr");
value = rm.GetString("Message"); // Loads fr satellite now
```

## Versioning and Strong Naming

### Version Matching

Satellite assemblies must match the main assembly version:

```
Main Assembly:      MyApp.dll, Version=1.2.3.0
Satellite Assembly: MyApp.resources.dll, Version=1.2.3.0, Culture=es
```

**Mismatch causes runtime errors:**
```
System.Resources.MissingManifestResourceException:
Could not find any resources appropriate for the specified culture or the neutral culture.
```

### Strong Naming

**Generate Key Pair:**
```bash
sn -k MyKey.snk
```

**.csproj:**
```xml
<PropertyGroup>
  <AssemblyOriginatorKeyFile>MyKey.snk</AssemblyOriginatorKeyFile>
  <SignAssembly>true</SignAssembly>
</PropertyGroup>
```

**Build Output:**
```
Main Assembly: MyApp, Version=1.0.0.0, Culture=neutral, PublicKeyToken=abc123def456
Satellite: MyApp.resources, Version=1.0.0.0, Culture=es, PublicKeyToken=abc123def456
```

## Troubleshooting

### Issue: Satellite Assembly Not Found

**Symptoms:**
```
System.Resources.MissingManifestResourceException
```

**Causes:**
1. Culture folder missing
2. Satellite assembly file missing
3. Incorrect culture name in folder

**Solutions:**
```bash
# Verify culture folder structure
dir bin\Debug\net9.0\es
dir bin\Debug\net9.0\es-MX

# Verify satellite assembly exists
dir bin\Debug\net9.0\es\MyApp.resources.dll

# Check build output for warnings
dotnet build --verbosity detailed
```

### Issue: Wrong Culture Resources Loaded

**Cause:** Incorrect CurrentUICulture setting

**Solution:**
```csharp
// Verify current culture
Console.WriteLine($"CurrentUICulture: {CultureInfo.CurrentUICulture.Name}");

// Set explicitly
CultureInfo.CurrentUICulture = new CultureInfo("es-MX");

// Verify resource manager culture
Console.WriteLine($"ResourceManager culture: {Resources.ResourceManager.GetResourceSet(CultureInfo.CurrentUICulture, true, true)}");
```

### Issue: Resources Return Null

**Cause:** Resource key missing in satellite assembly

**Solution:**
```csharp
// Check all resources in satellite
ResourceSet rs = rm.GetResourceSet(new CultureInfo("es"), true, false);
if (rs != null)
{
    foreach (DictionaryEntry entry in rs)
    {
        Console.WriteLine($"{entry.Key} = {entry.Value}");
    }
}
else
{
    Console.WriteLine("Satellite assembly not loaded");
}
```

### Issue: Version Mismatch

**Symptoms:**
```
Could not load file or assembly 'MyApp.resources, Version=1.0.0.0'
```

**Cause:** Main assembly version updated but satellites not rebuilt

**Solution:**
```bash
# Clean and rebuild all
dotnet clean
dotnet build
```

### Issue: Strong Name Verification Failed

**Symptoms:**
```
System.IO.FileLoadException: Could not load file or assembly '...' or one of its dependencies.
Strong name validation failed.
```

**Cause:** Satellite assembly not signed or wrong key

**Solution:**
```bash
# Verify assembly signature
sn -v MyApp.dll
sn -v es\MyApp.resources.dll

# Re-sign if needed
al /target:lib /embed:Resources.es.resources /culture:es /out:es\MyApp.resources.dll /keyfile:MyKey.snk
```

## Advanced Scenarios

### Custom Resource Locations

```csharp
// Modify probing behavior
AppDomain.CurrentDomain.SetData("APP_CONTEXT_BASE_DIRECTORY", customPath);

// Or use custom ResourceManager
public class CustomResourceManager : ResourceManager
{
    protected override string GetResourceFileName(CultureInfo culture)
    {
        // Custom file naming convention
        return $"CustomResources.{culture.Name}.resources";
    }

    protected override ResourceSet InternalGetResourceSet(
        CultureInfo culture,
        bool createIfNotExists,
        bool tryParents)
    {
        // Custom probing logic
        return base.InternalGetResourceSet(culture, createIfNotExists, tryParents);
    }
}
```

### Satellite Assemblies in Subdirectories

```csharp
// Add subdirectory to probing path
AppDomain.CurrentDomain.AppendPrivatePath("Localization");

// Probing will search:
// ApplicationBase/Localization/es-MX/MyApp.resources.dll
```

### Downloading Satellite Assemblies On-Demand

```csharp
public class DownloadableResourceManager : ResourceManager
{
    private readonly HttpClient _httpClient;
    private readonly string _downloadUrl;

    public DownloadableResourceManager(string baseName, Assembly assembly, string downloadUrl)
        : base(baseName, assembly)
    {
        _httpClient = new HttpClient();
        _downloadUrl = downloadUrl;
    }

    protected override ResourceSet InternalGetResourceSet(
        CultureInfo culture,
        bool createIfNotExists,
        bool tryParents)
    {
        // Try local first
        ResourceSet rs = base.InternalGetResourceSet(culture, createIfNotExists, tryParents);
        if (rs != null) return rs;

        // Download if not found
        if (createIfNotExists)
        {
            DownloadSatelliteAssembly(culture).Wait();
            return base.InternalGetResourceSet(culture, createIfNotExists, tryParents);
        }

        return null;
    }

    private async Task DownloadSatelliteAssembly(CultureInfo culture)
    {
        string url = $"{_downloadUrl}/{culture.Name}/MyApp.resources.dll";
        string localPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            culture.Name,
            "MyApp.resources.dll"
        );

        Directory.CreateDirectory(Path.GetDirectoryName(localPath));

        byte[] data = await _httpClient.GetByteArrayAsync(url);
        await File.WriteAllBytesAsync(localPath, data);
    }
}
```

## Best Practices

### 1. Include All Cultures in Deployment

Even if partially translated, include satellite assemblies for all supported cultures (they can be incomplete).

### 2. Version Satellite Assemblies with Main Assembly

Ensure satellite assembly versions match the main assembly to avoid runtime errors.

### 3. Use Neutral Cultures When Possible

```
# Good: Neutral culture covers multiple regions
es\MyApp.resources.dll (Spanish - covers es-ES, es-MX, es-AR, etc.)

# Only use specific cultures when necessary
es-MX\MyApp.resources.dll (Mexican Spanish - only when differs from neutral)
```

### 4. Test Probing in Production Environment

Probing behavior can differ between development and production (file paths, permissions).

### 5. Monitor Satellite Assembly Loading

```csharp
AppDomain.CurrentDomain.AssemblyLoad += (sender, args) =>
{
    if (args.LoadedAssembly.FullName.Contains(".resources"))
    {
        Console.WriteLine($"Loaded satellite: {args.LoadedAssembly.FullName}");
    }
};
```

## Summary

- **Satellite Assemblies**: Culture-specific resource DLLs deployed in culture-named folders
- **Automatic Build**: MSBuild creates satellites from culture-specific .resx files
- **Probing**: ResourceManager searches culture folders with fallback to parent cultures
- **Deployment**: xcopy-friendly, include in NuGet packages, optional GAC for shared libraries
- **Performance**: Lazy-loaded on-demand, cached after first access
- **Versioning**: Must match main assembly version
- **Troubleshooting**: Verify folder structure, assembly presence, culture names, versions

Satellite assemblies enable efficient, modular deployment of localized applications without code changes.
