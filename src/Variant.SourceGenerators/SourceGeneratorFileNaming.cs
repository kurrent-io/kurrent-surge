using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Kurrent.Variant.SourceGenerators;

/// <summary>
/// Provides deterministic, collision-resistant file naming for source generators.
/// </summary>
public static class SourceGeneratorFileNaming {
    /// <summary>
    /// Generates a deterministic hint name for source-generated files.
    /// The method automatically extracts generic type arguments from the type symbol if available,
    /// or uses the type's fully qualified name for hash computation.
    /// </summary>
    /// <param name="typeSymbol">The type symbol being generated.</param>
    /// <param name="options">Optional configuration for file naming.</param>
    /// <param name="typeArgs">Optional explicit type arguments. If null, automatically extracted from typeSymbol.</param>
    /// <returns>A hint name suitable for AddSource() that includes a deterministic hash.</returns>
    public static string GetHintName(
        INamedTypeSymbol typeSymbol,
        FileNamingOptions? options = null,
        IEnumerable<ITypeSymbol>? typeArgs = null
    ) {
        options ??= FileNamingOptions.Default;

        var hashInput      = GenerateHashInput(typeSymbol, typeArgs?.ToList() ?? []);
        var hash           = ComputeStableHash(hashInput);
        var fileName       = BuildFileName(typeSymbol.Name, hash, options);
        var directoryParts = BuildDirectoryParts(typeSymbol, options);

        return CombinePathParts(directoryParts, fileName, options);
    }

    static string GenerateHashInput(INamedTypeSymbol typeSymbol, List<ITypeSymbol> typeArgs) {
        string s;
        if (typeArgs.Count > 0)
            // Use fully qualified type arguments for hash
            s = string.Join(",", typeArgs.Select(t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        else if (typeSymbol is { IsGenericType: true, TypeArguments.Length: > 0 })
            // Auto-extract from generic type
            s = string.Join(",", typeSymbol.TypeArguments.Select(t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        else
            // Use fully qualified type name as fallback
            s = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return s;
    }

    static string BuildFileName(string typeName, string hash, FileNamingOptions options) {
        var name = options.LowercasePath ? typeName.ToLowerInvariant() : typeName;
        return $"{name}_{hash}.g.cs";
    }

    static List<string> BuildDirectoryParts(INamedTypeSymbol typeSymbol, FileNamingOptions options) {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(options.Prefix) && options.UseFoldersForPrefix) {
            var prefix = ApplyCasing(options.Prefix, options.LowercasePath);
            parts.Add(prefix);
        }

        if (options.UseFoldersForNamespace) {
            var namespaceName  = GetNamespaceName(typeSymbol);
            var casedNamespace = ApplyCasing(namespaceName, options.LowercasePath);
            parts.Add(casedNamespace);
        }

        return parts;
    }

    static string CombinePathParts(List<string> directoryParts, string fileName, FileNamingOptions options) {
        if (directoryParts.Count > 0) {
            var directory = string.Join("/", directoryParts);
            return $"{directory}/{fileName}";
        }

        if (!string.IsNullOrEmpty(options.Prefix) && !options.UseFoldersForPrefix) {
            var prefix = ApplyCasing(options.Prefix!, options.LowercasePath);
            return $"{prefix}_{fileName}";
        }

        return fileName;
    }

    static string GetNamespaceName(INamedTypeSymbol typeSymbol) =>
        typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? "Global"
            : typeSymbol.ContainingNamespace.ToDisplayString();

    static string ApplyCasing(string value, bool lowercase) => lowercase ? value.ToLowerInvariant() : value;

    static string ComputeStableHash(string input) {
        using var md5  = MD5.Create();

        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));

        return BitConverter.ToString(hash).Replace("-", "").Substring(0, 8);
    }
}

/// <summary>
/// Configuration options for source generator file naming.
/// </summary>
public sealed class FileNamingOptions {
    /// <summary>
    /// Gets the default file naming options.
    /// </summary>
    public static FileNamingOptions Default { get; } = new() {
        UseFoldersForNamespace = true,
        UseFoldersForPrefix    = true,
        LowercasePath          = false,
        Prefix                 = ""
    };

    /// <summary>
    /// When true, creates a folder hierarchy for the namespace (e.g., "Kurrent/Client/").
    /// When false, namespace is not included in the path.
    /// Default: true
    /// </summary>
    public bool UseFoldersForNamespace { get; set; } = true;

    /// <summary>
    /// When true and Prefix is set, the prefix becomes a folder (e.g., "Variants/...").
    /// When false and Prefix is set, the prefix is prepended to the filename (e.g., "Variants_TypeName_HASH.g.cs").
    /// Default: true
    /// </summary>
    public bool UseFoldersForPrefix { get; set; } = true;

    /// <summary>
    /// When true, converts all path components to lowercase.
    /// Default: false
    /// </summary>
    public bool LowercasePath { get; set; }

    /// <summary>
    /// Optional prefix for the generated file.
    /// Behavior depends on UseFoldersForPrefix.
    /// Default: null
    /// </summary>
    public string Prefix { get; set; } = "";

    /// <summary>
    /// Creates a new instance with flat file naming (no folders).
    /// </summary>
    public static FileNamingOptions Flat => new() {
        UseFoldersForNamespace = false,
        UseFoldersForPrefix    = false
    };

    /// <summary>
    /// Creates a new instance with lowercase paths.
    /// </summary>
    public static FileNamingOptions Lowercase => new() {
        UseFoldersForNamespace = true,
        UseFoldersForPrefix    = true,
        LowercasePath          = true
    };
}
