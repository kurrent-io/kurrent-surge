using System.Text;
using Kurrent.Surge.Codecs.Avro.Generators.Model;

namespace Kurrent.Surge.Codecs.Avro.Generators.Emission;

/// <summary>
/// Maps Avro names onto C# identifiers.
/// </summary>
/// <remarks>
/// Escaping a reserved word is the only transformation applied. The Avro specification constrains a name
/// to <c>[A-Za-z_][A-Za-z0-9_]*</c>, a subset of what C# accepts, so a conforming schema needs nothing
/// more. A schema that breaks that rule is not rejected here and reaches the compiler as an invalid
/// identifier.
/// <para>
/// Derived from AvroSourceGenerator (MIT, Copyright (c) 2024 sadicangel),
/// <c>Registry/Extensions/StringAvroSchemaExtensions.cs</c> at v0.6.0.
/// </para>
/// </remarks>
static class AvroNaming {
    static readonly HashSet<string> Reserved = new() {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
        "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
        "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if",
        "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new",
        "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
        "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static",
        "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong",
        "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
        "__arglist", "__makeref", "__reftype", "__refvalue"
    };

    /// <summary>Escapes a single Avro name so it can be used as a C# identifier.</summary>
    public static string Identifier(string name) => Reserved.Contains(name) ? $"@{name}" : name;

    /// <summary>Escapes each dot-separated segment of an Avro namespace.</summary>
    public static string Namespace(string @namespace) {
        if (@namespace.Length == 0) return "";

        var segments = @namespace.Split('.');

        for (var index = 0; index < segments.Length; index++) segments[index] = Identifier(segments[index]);

        return string.Join(".", segments);
    }

    public static string TypeName(IAvroNamed named) => Identifier(named.Name);

    public static string QualifiedTypeName(IAvroNamed named) => Qualified(named.Namespace, Identifier(named.Name));

    /// <summary>
    /// The interface every branch of an all-record union implements, so the field has one CLR type.
    /// </summary>
    /// <remarks>
    /// Named from the declaring record and field rather than from the branches, because the same pair of
    /// records can appear in two different unions and each needs its own interface. The raw Avro field
    /// name is used: escaping it first would put an <c>@</c> in the middle of the composed name.
    /// </remarks>
    public static string VariantInterfaceName(AvroRecord owner, string fieldName) =>
        $"I{owner.Name}{Capitalise(fieldName)}Variant";

    public static string QualifiedVariantInterfaceName(AvroRecord owner, string fieldName) =>
        Qualified(owner.Namespace, VariantInterfaceName(owner, fieldName));

    /// <summary>A unique, filename-safe key for one generated source file.</summary>
    public static string HintName(string fullName) {
        var builder = new StringBuilder(fullName.Length);

        foreach (var character in fullName) builder.Append(char.IsLetterOrDigit(character) ? character : '_');

        return builder.ToString();
    }

    static string Qualified(string @namespace, string name) {
        var mangled = Namespace(@namespace);

        return mangled.Length == 0 ? $"global::{name}" : $"global::{mangled}.{name}";
    }

    static string Capitalise(string name) =>
        name.Length == 0 ? name : $"{char.ToUpperInvariant(name[0])}{name.Substring(1)}";
}
