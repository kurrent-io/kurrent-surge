namespace Kurrent.Surge.Schema;

/// <summary>
/// The schema text a converter is given, and which type in it is meant.
/// </summary>
/// <remarks>
/// Schemas arrive as strings, never as files. A format that resolves references between its own
/// documents does so inside its implementation, so nothing here carries a file name or a path.
/// <para>
/// Each schema must be self-contained apart from whatever its format treats as built in — for
/// protobuf, the well-known types. A reference that cannot be resolved from this set fails loudly
/// rather than producing a partial schema.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class SchemaSource {
    /// <summary>The schema documents, as text.</summary>
    public IReadOnlyList<string> Schemas { get; init; } = [];

    /// <summary>
    /// The fully qualified message the caller means, for a format whose document declares more than one.
    /// </summary>
    /// <remarks>Null when the document has a single root, as an Avro schema does.</remarks>
    public string? MessageTypeName { get; init; }

    /// <summary>A single schema document.</summary>
    public static SchemaSource Text(string schema, string? messageTypeName = null) =>
        new() { Schemas = [schema], MessageTypeName = messageTypeName };

    /// <summary>Several schema documents that together declare the message.</summary>
    public static SchemaSource From(string? messageTypeName, params IReadOnlyList<string> schemas) =>
        new() { Schemas = schemas, MessageTypeName = messageTypeName };
}
