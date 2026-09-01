using Kurrent.Surge.Codecs.Avro.Generators.Json;
using Kurrent.Surge.Codecs.Avro.Generators.Model;

namespace Kurrent.Surge.Codecs.Avro.Generators.Emission;

/// <summary>
/// Produces the Avro schema text a generated type exposes through <c>ISpecificRecord.Schema</c>.
/// </summary>
/// <remarks>
/// The text is the declaration's own span from the schema file rather than a re-serialisation of the
/// parsed model, so no parser detail — precision, aliases, ordering, custom attributes — can be lost on
/// the way back out. A namespace inherited from an enclosing declaration is injected, because the span
/// on its own would otherwise parse to a different fullname than the type it belongs to.
/// </remarks>
static class AvroSchemaText {
    public static string For(AvroSchemaDocument document, IAvroNamed named) {
        if (named.Declaration is not { } declaration) return "";

        var span = document.Source.Substring(declaration.Start, declaration.End - declaration.Start);

        return NeedsNamespace(declaration, named) ? Inject(span, named.Namespace) : span;
    }

    /// <summary>Writes the text as a C# verbatim string literal.</summary>
    /// <remarks>
    /// Verbatim rather than a raw string literal: generated code compiles against the consumer's
    /// <c>LangVersion</c>, and raw string literals need C# 11.
    /// </remarks>
    public static string ToLiteral(string schemaText) => $"@\"{schemaText.Replace("\"", "\"\"")}\"";

    static bool NeedsNamespace(JsonValue declaration, IAvroNamed named) =>
        named.Namespace.Length > 0
     && declaration["namespace"] is null
     && declaration.TextOf("name")?.Contains(".") != true;

    static string Inject(string span, string @namespace) =>
        $"{{\"namespace\": \"{@namespace}\", {span.Substring(1).TrimStart()}";
}
