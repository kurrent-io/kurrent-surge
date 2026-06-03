using System.Security.Cryptography;
using System.Text;

namespace Kurrent.CodeAnalysis.Emission;

/// <summary>
/// Deterministic, collision-resistant file naming for source-generated files. Takes
/// <see cref="TypeSymbol"/> so generators don't need to hold Roslyn symbols past extraction.
/// </summary>
/// <remarks>
/// Hash input is prefixed with <c>global::</c> on each full name to stay byte-identical with the
/// V1 helper (which used <c>SymbolDisplayFormat.FullyQualifiedFormat</c>). Callers migrating from
/// V1 get the same filenames and no git-rename churn on generated output.
/// </remarks>
public static class SourceGeneratorFileNaming {
    public static string GetHintName(TypeSymbol type, FileNamingOptions? options = null) =>
        GetHintName(type, options, explicitHashTypeFullNames: null);

    /// <summary>
    /// Overload for generators that hash a set of types distinct from the target type's own
    /// <see cref="TypeSymbol.TypeArguments"/> — e.g. Variant, where the hash is over the case
    /// types carried on the implemented <c>IVariant&lt;...&gt;</c> interface.
    /// </summary>
    public static string GetHintName(
        TypeSymbol type,
        FileNamingOptions? options,
        IEnumerable<string>? explicitHashTypeFullNames) =>
        GetHintName(
            typeName:                  type.Name,
            typeNamespace:             type.Namespace,
            typeFullName:              type.FullName,
            typeArgumentFullNames:     type.TypeArguments.Select(t => t.FullName).ToArray(),
            options:                   options,
            explicitHashTypeFullNames: explicitHashTypeFullNames);

    /// <summary>
    /// String-based overload for generators that have already projected a target type down to
    /// value strings (no live <see cref="TypeSymbol"/> in hand). Useful in the
    /// <c>RegisterSources</c> callback where the pipeline element is a user DTO.
    /// </summary>
    public static string GetHintName(
        string typeName,
        string typeNamespace,
        string typeFullName,
        string[]? typeArgumentFullNames = null,
        FileNamingOptions? options = null,
        IEnumerable<string>? explicitHashTypeFullNames = null) {
        options ??= FileNamingOptions.Default;

        var hashInput      = GenerateHashInput(typeFullName, typeArgumentFullNames, explicitHashTypeFullNames);
        var hash           = ComputeStableHash(hashInput);
        var fileName       = BuildFileName(typeName, hash, options);
        var directoryParts = BuildDirectoryParts(typeNamespace, options);

        return CombinePathParts(directoryParts, fileName, options);
    }

    static string GenerateHashInput(
        string fullName,
        string[]? typeArgumentFullNames,
        IEnumerable<string>? explicitHashTypeFullNames) {
        if (explicitHashTypeFullNames is not null) {
            var materialized = explicitHashTypeFullNames.ToList();
            if (materialized.Count > 0)
                return string.Join(",", materialized.Select(PrefixGlobal));
        }

        if (typeArgumentFullNames is { Length: > 0 })
            return string.Join(",", typeArgumentFullNames.Select(PrefixGlobal));

        return PrefixGlobal(fullName);
    }

    static string PrefixGlobal(string fullName) =>
        fullName.StartsWith("global::", StringComparison.Ordinal) ? fullName : "global::" + fullName;

    static string BuildFileName(string typeName, string hash, FileNamingOptions options) {
        var name = options.LowercasePath ? typeName.ToLowerInvariant() : typeName;
        return $"{name}_{hash}.g.cs";
    }

    static List<string> BuildDirectoryParts(string typeNamespace, FileNamingOptions options) {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(options.Prefix) && options.UseFoldersForPrefix)
            parts.Add(ApplyCasing(options.Prefix, options.LowercasePath));

        if (options.UseFoldersForNamespace) {
            var ns = string.IsNullOrEmpty(typeNamespace) ? "Global" : typeNamespace;
            parts.Add(ApplyCasing(ns, options.LowercasePath));
        }

        return parts;
    }

    static string CombinePathParts(List<string> directoryParts, string fileName, FileNamingOptions options) {
        if (directoryParts.Count > 0) {
            var directory = string.Join("/", directoryParts);
            return $"{directory}/{fileName}";
        }

        if (!string.IsNullOrEmpty(options.Prefix) && !options.UseFoldersForPrefix) {
            var prefix = ApplyCasing(options.Prefix, options.LowercasePath);
            return $"{prefix}_{fileName}";
        }

        return fileName;
    }

    static string ApplyCasing(string value, bool lowercase) =>
        lowercase ? value.ToLowerInvariant() : value;

    static string ComputeStableHash(string input) {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(hash).Replace("-", "").Substring(0, 8);
    }
}
