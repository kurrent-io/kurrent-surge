# Programmatic Manipulation of .resx Files

## Overview

.NET provides APIs for creating, reading, and modifying `.resx` resource files programmatically. This is essential for build tools, translation workflows, resource management utilities, and automation scenarios.

## Required Namespaces

```csharp
using System.Resources;
using System.Collections;
using System.ComponentModel.Design;
using System.Drawing;       // For image resources
using System.IO;
```

## Reading Resources

### ResXResourceReader

Reads resources from `.resx` files:

```csharp
using (var reader = new ResXResourceReader("Resources.resx"))
{
    foreach (DictionaryEntry entry in reader)
    {
        string key = (string)entry.Key;
        object value = entry.Value;

        Console.WriteLine($"{key} = {value}");
    }
}
```

### Reading with Metadata

```csharp
using (var reader = new ResXResourceReader("Resources.resx"))
{
    // Enable reading as ResXDataNode to access metadata
    reader.UseResXDataNodes = true;

    foreach (DictionaryEntry entry in reader)
    {
        ResXDataNode node = (ResXDataNode)entry.Value;

        string key = (string)entry.Key;
        string typeName = node.GetValueTypeName((AssemblyName[])null);
        object value = node.GetValue((AssemblyName[])null);
        string comment = node.Comment;

        Console.WriteLine($"Key: {key}");
        Console.WriteLine($"Type: {typeName}");
        Console.WriteLine($"Value: {value}");
        Console.WriteLine($"Comment: {comment}");
        Console.WriteLine();
    }
}
```

### Reading File References

```csharp
using (var reader = new ResXResourceReader("Resources.resx"))
{
    reader.UseResXDataNodes = true;
    reader.BasePath = Path.GetDirectoryName(Path.GetFullPath("Resources.resx"));

    foreach (DictionaryEntry entry in reader)
    {
        ResXDataNode node = (ResXDataNode)entry.Value;
        ResXFileRef fileRef = node.FileRef;

        if (fileRef != null)
        {
            Console.WriteLine($"File reference:");
            Console.WriteLine($"  Key: {entry.Key}");
            Console.WriteLine($"  File: {fileRef.FileName}");
            Console.WriteLine($"  Type: {fileRef.TypeName}");
            Console.WriteLine($"  Encoding: {fileRef.TextFileEncoding?.EncodingName}");
        }
    }
}
```

## Writing Resources

### ResXResourceWriter

Creates or overwrites `.resx` files:

```csharp
using (var writer = new ResXResourceWriter("NewResources.resx"))
{
    // Add string resources
    writer.AddResource("AppName", "My Application");
    writer.AddResource("Version", "1.0.0");

    // Add with comment
    writer.AddResource(new ResXDataNode("WelcomeMessage", "Welcome!")
    {
        Comment = "Displayed on home page"
    });

    // Generate the file
    writer.Generate();
}
```

### Adding Different Data Types

```csharp
using (var writer = new ResXResourceWriter("Resources.resx"))
{
    // String
    writer.AddResource("Message", "Hello, World!");

    // Integer
    writer.AddResource("MaxRetries", 3);

    // Boolean
    writer.AddResource("IsEnabled", true);

    // Decimal
    writer.AddResource("DefaultPrice", 99.99m);

    // DateTime
    writer.AddResource("ReleaseDate", new DateTime(2025, 1, 1));

    // Image from file
    using (var image = Image.FromFile("logo.png"))
    {
        writer.AddResource("Logo", image);
    }

    // Icon
    using (var icon = new Icon("app.ico"))
    {
        writer.AddResource("AppIcon", icon);
    }

    writer.Generate();
}
```

### Adding File References

```csharp
using (var writer = new ResXResourceWriter("Resources.resx"))
{
    // Image file reference
    var imageRef = new ResXFileRef(
        "Images/logo.png",
        typeof(Bitmap).AssemblyQualifiedName
    );
    writer.AddResource(new ResXDataNode("Logo", imageRef)
    {
        Comment = "Application logo"
    });

    // Audio file reference
    var audioRef = new ResXFileRef(
        "Sounds/notification.wav",
        typeof(byte[]).AssemblyQualifiedName
    );
    writer.AddResource(new ResXDataNode("NotificationSound", audioRef));

    // Text file reference with encoding
    var textRef = new ResXFileRef(
        "Templates/email.html",
        typeof(string).AssemblyQualifiedName,
        System.Text.Encoding.UTF8
    );
    writer.AddResource(new ResXDataNode("EmailTemplate", textRef));

    writer.Generate();
}
```

### Adding Metadata

```csharp
using (var writer = new ResXResourceWriter("Resources.resx"))
{
    // Add regular resources
    writer.AddResource("AppName", "MyApp");

    // Add metadata (design-time information, not compiled)
    writer.AddMetadata("Author", "Development Team");
    writer.AddMetadata("LastModified", DateTime.Now.ToString("O"));
    writer.AddMetadata("Version", "1.0.0");

    writer.Generate();
}
```

## Modifying Existing Resources

### Reading and Writing Pattern

```csharp
// Read existing resources into memory
var resources = new Dictionary<string, ResXDataNode>();

using (var reader = new ResXResourceReader("Resources.resx"))
{
    reader.UseResXDataNodes = true;

    foreach (DictionaryEntry entry in reader)
    {
        resources[(string)entry.Key] = (ResXDataNode)entry.Value;
    }
}

// Modify resources
if (resources.ContainsKey("ExistingKey"))
{
    // Update existing resource
    resources["ExistingKey"] = new ResXDataNode("ExistingKey", "New Value")
    {
        Comment = "Updated comment"
    };
}

// Add new resource
resources["NewKey"] = new ResXDataNode("NewKey", "New Value")
{
    Comment = "Newly added resource"
};

// Remove resource
resources.Remove("ObsoleteKey");

// Write back to file
using (var writer = new ResXResourceWriter("Resources.resx"))
{
    foreach (var kvp in resources)
    {
        writer.AddResource(kvp.Value);
    }

    writer.Generate();
}
```

### Utility Class for Modifications

```csharp
public static class ResXFileHelper
{
    public static void AddOrUpdateResource(
        string resxPath,
        string key,
        object value,
        string comment = null)
    {
        var resources = LoadResources(resxPath);

        resources[key] = new ResXDataNode(key, value)
        {
            Comment = comment
        };

        SaveResources(resxPath, resources);
    }

    public static void RemoveResource(string resxPath, string key)
    {
        var resources = LoadResources(resxPath);
        resources.Remove(key);
        SaveResources(resxPath, resources);
    }

    public static bool ResourceExists(string resxPath, string key)
    {
        var resources = LoadResources(resxPath);
        return resources.ContainsKey(key);
    }

    public static string GetResourceValue(string resxPath, string key)
    {
        var resources = LoadResources(resxPath);
        if (resources.TryGetValue(key, out ResXDataNode node))
        {
            return node.GetValue((AssemblyName[])null)?.ToString();
        }
        return null;
    }

    private static Dictionary<string, ResXDataNode> LoadResources(string resxPath)
    {
        var resources = new Dictionary<string, ResXDataNode>();

        if (!File.Exists(resxPath))
            return resources;

        using (var reader = new ResXResourceReader(resxPath))
        {
            reader.UseResXDataNodes = true;

            foreach (DictionaryEntry entry in reader)
            {
                resources[(string)entry.Key] = (ResXDataNode)entry.Value;
            }
        }

        return resources;
    }

    private static void SaveResources(
        string resxPath,
        Dictionary<string, ResXDataNode> resources)
    {
        using (var writer = new ResXResourceWriter(resxPath))
        {
            foreach (var kvp in resources)
            {
                writer.AddResource(kvp.Value);
            }

            writer.Generate();
        }
    }
}
```

## Merging Resource Files

### Merge Two .resx Files

```csharp
public static void MergeResXFiles(
    string sourceFile1,
    string sourceFile2,
    string outputFile,
    ConflictResolution conflictResolution = ConflictResolution.UseSource1)
{
    var mergedResources = new Dictionary<string, ResXDataNode>();

    // Load first file
    using (var reader = new ResXResourceReader(sourceFile1))
    {
        reader.UseResXDataNodes = true;

        foreach (DictionaryEntry entry in reader)
        {
            mergedResources[(string)entry.Key] = (ResXDataNode)entry.Value;
        }
    }

    // Load and merge second file
    using (var reader = new ResXResourceReader(sourceFile2))
    {
        reader.UseResXDataNodes = true;

        foreach (DictionaryEntry entry in reader)
        {
            string key = (string)entry.Key;
            ResXDataNode node = (ResXDataNode)entry.Value;

            if (mergedResources.ContainsKey(key))
            {
                // Handle conflict
                switch (conflictResolution)
                {
                    case ConflictResolution.UseSource1:
                        // Keep existing (from source1)
                        break;
                    case ConflictResolution.UseSource2:
                        // Overwrite with source2
                        mergedResources[key] = node;
                        break;
                    case ConflictResolution.ThrowException:
                        throw new InvalidOperationException($"Duplicate key: {key}");
                }
            }
            else
            {
                mergedResources[key] = node;
            }
        }
    }

    // Write merged resources
    using (var writer = new ResXResourceWriter(outputFile))
    {
        foreach (var kvp in mergedResources)
        {
            writer.AddResource(kvp.Value);
        }

        writer.Generate();
    }
}

public enum ConflictResolution
{
    UseSource1,
    UseSource2,
    ThrowException
}
```

## Comparing Resource Files

### Find Missing Keys

```csharp
public static class ResXComparer
{
    public static List<string> FindMissingKeys(
        string baseFile,
        string translatedFile)
    {
        var baseKeys = GetResourceKeys(baseFile);
        var translatedKeys = GetResourceKeys(translatedFile);

        return baseKeys.Except(translatedKeys).ToList();
    }

    public static List<string> FindExtraKeys(
        string baseFile,
        string translatedFile)
    {
        var baseKeys = GetResourceKeys(baseFile);
        var translatedKeys = GetResourceKeys(translatedFile);

        return translatedKeys.Except(baseKeys).ToList();
    }

    public static Dictionary<string, (string baseValue, string translatedValue)> FindDifferences(
        string baseFile,
        string translatedFile)
    {
        var baseResources = GetAllResources(baseFile);
        var translatedResources = GetAllResources(translatedFile);

        var differences = new Dictionary<string, (string, string)>();

        foreach (var key in baseResources.Keys.Intersect(translatedResources.Keys))
        {
            string baseValue = baseResources[key];
            string translatedValue = translatedResources[key];

            if (baseValue != translatedValue)
            {
                differences[key] = (baseValue, translatedValue);
            }
        }

        return differences;
    }

    private static HashSet<string> GetResourceKeys(string resxPath)
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

    private static Dictionary<string, string> GetAllResources(string resxPath)
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
}
```

## Export and Import

### Export to JSON

```csharp
public static void ExportToJson(string resxPath, string jsonPath)
{
    var resources = new Dictionary<string, object>();

    using (var reader = new ResXResourceReader(resxPath))
    {
        reader.UseResXDataNodes = true;

        foreach (DictionaryEntry entry in reader)
        {
            ResXDataNode node = (ResXDataNode)entry.Value;
            string key = (string)entry.Key;

            // Only export strings (skip binary resources)
            if (node.GetValueTypeName((AssemblyName[])null).StartsWith("System.String"))
            {
                resources[key] = new
                {
                    value = node.GetValue((AssemblyName[])null),
                    comment = node.Comment
                };
            }
        }
    }

    string json = JsonSerializer.Serialize(resources, new JsonSerializerOptions
    {
        WriteIndented = true
    });

    File.WriteAllText(jsonPath, json);
}
```

### Import from JSON

```csharp
public static void ImportFromJson(string jsonPath, string resxPath)
{
    string json = File.ReadAllText(jsonPath);
    var resources = JsonSerializer.Deserialize<Dictionary<string, JsonResourceEntry>>(json);

    using (var writer = new ResXResourceWriter(resxPath))
    {
        foreach (var kvp in resources)
        {
            writer.AddResource(new ResXDataNode(kvp.Key, kvp.Value.Value)
            {
                Comment = kvp.Value.Comment
            });
        }

        writer.Generate();
    }
}

public class JsonResourceEntry
{
    public string Value { get; set; }
    public string Comment { get; set; }
}
```

### Export to CSV for Translation

```csharp
public static void ExportToCsv(string resxPath, string csvPath)
{
    using (var writer = new StreamWriter(csvPath))
    {
        // Write header
        writer.WriteLine("Key,Value,Comment");

        using (var reader = new ResXResourceReader(resxPath))
        {
            reader.UseResXDataNodes = true;

            foreach (DictionaryEntry entry in reader)
            {
                ResXDataNode node = (ResXDataNode)entry.Value;
                string key = (string)entry.Key;
                string value = node.GetValue((AssemblyName[])null)?.ToString();
                string comment = node.Comment;

                // Escape quotes and commas
                value = EscapeCsvField(value);
                comment = EscapeCsvField(comment);

                writer.WriteLine($"{key},{value},{comment}");
            }
        }
    }
}

private static string EscapeCsvField(string field)
{
    if (string.IsNullOrEmpty(field))
        return string.Empty;

    if (field.Contains(",") || field.Contains("\"") || field.Contains("\n"))
    {
        return $"\"{field.Replace("\"", "\"\"")}\"";
    }

    return field;
}
```

## Generating Placeholder Resources

### Create Placeholder Translations

```csharp
public static void GeneratePlaceholderTranslation(
    string baseFile,
    string outputFile,
    string cultureName)
{
    using (var reader = new ResXResourceReader(baseFile))
    using (var writer = new ResXResourceWriter(outputFile))
    {
        reader.UseResXDataNodes = true;

        foreach (DictionaryEntry entry in reader)
        {
            ResXDataNode node = (ResXDataNode)entry.Value;
            string key = (string)entry.Key;

            // Create placeholder with [TRANSLATE] marker
            object value = node.GetValue((AssemblyName[])null);
            if (value is string stringValue)
            {
                value = $"[{cultureName.ToUpper()}] {stringValue}";
            }

            writer.AddResource(new ResXDataNode(key, value)
            {
                Comment = $"[TODO: Translate to {cultureName}] {node.Comment}"
            });
        }

        writer.Generate();
    }
}
```

## Validation

### Validate Resource File

```csharp
public static class ResXValidator
{
    public static ValidationResult Validate(string resxPath)
    {
        var result = new ValidationResult();

        if (!File.Exists(resxPath))
        {
            result.Errors.Add($"File not found: {resxPath}");
            return result;
        }

        try
        {
            using (var reader = new ResXResourceReader(resxPath))
            {
                reader.UseResXDataNodes = true;

                foreach (DictionaryEntry entry in reader)
                {
                    string key = (string)entry.Key;
                    ResXDataNode node = (ResXDataNode)entry.Value;

                    // Validate key naming
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        result.Errors.Add("Empty or whitespace resource key found");
                        continue;
                    }

                    // Validate value
                    try
                    {
                        object value = node.GetValue((AssemblyName[])null);
                        if (value == null)
                        {
                            result.Warnings.Add($"Resource '{key}' has null value");
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Failed to get value for key '{key}': {ex.Message}");
                    }

                    // Check for file references
                    if (node.FileRef != null)
                    {
                        string basePath = Path.GetDirectoryName(Path.GetFullPath(resxPath));
                        string filePath = Path.Combine(basePath, node.FileRef.FileName);

                        if (!File.Exists(filePath))
                        {
                            result.Errors.Add($"File reference not found: {node.FileRef.FileName} (key: {key})");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Failed to read file: {ex.Message}");
        }

        return result;
    }
}

public class ValidationResult
{
    public List<string> Errors { get; } = new List<string>();
    public List<string> Warnings { get; } = new List<string>();
    public bool IsValid => Errors.Count == 0;
}
```

## Best Practices

### 1. Always Use `using` Statements

```csharp
// ✓ Good - ensures proper disposal
using (var reader = new ResXResourceReader("file.resx"))
{
    // Read resources
}

// ❌ Bad - resource leak
var reader = new ResXResourceReader("file.resx");
// ... (missing Dispose)
```

### 2. Use ResXDataNode for Full Control

```csharp
// ✓ Good - preserves all metadata
reader.UseResXDataNodes = true;
foreach (DictionaryEntry entry in reader)
{
    ResXDataNode node = (ResXDataNode)entry.Value;
    // Access comment, type, file references
}

// ❌ Limited - loses metadata
foreach (DictionaryEntry entry in reader)
{
    object value = entry.Value;  // Just the value, no metadata
}
```

### 3. Set BasePath for File References

```csharp
// ✓ Good - resolves file references correctly
reader.BasePath = Path.GetDirectoryName(Path.GetFullPath(resxPath));

// ❌ Bad - file references may fail to resolve
// (no BasePath set)
```

### 4. Handle Exceptions Gracefully

```csharp
try
{
    using (var reader = new ResXResourceReader(resxPath))
    {
        // Process resources
    }
}
catch (ArgumentException ex)
{
    Console.WriteLine($"Invalid .resx file format: {ex.Message}");
}
catch (IOException ex)
{
    Console.WriteLine($"File access error: {ex.Message}");
}
```

## Summary

- **ResXResourceReader**: Read `.resx` files programmatically
- **ResXResourceWriter**: Create and modify `.resx` files
- **ResXDataNode**: Access complete resource metadata (value, type, comment, file references)
- **Merging**: Combine multiple resource files with conflict resolution
- **Comparison**: Find missing, extra, or changed resources
- **Export/Import**: Convert to/from JSON, CSV for translation workflows
- **Validation**: Check resource file integrity and completeness

Programmatic manipulation enables automation of localization workflows, build tools, and resource management utilities.
