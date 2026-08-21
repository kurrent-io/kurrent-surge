using System;
using System.Collections.Generic;
using System.Linq;
using Kurrent.Surge.Codecs.Avro.Generators.Json;

namespace Kurrent.Surge.Codecs.Avro.Generators.Model;

enum AvroPrimitiveKind { Null, Boolean, Int, Long, Float, Double, Bytes, String }

/// <summary>
/// The Avro logical types this generator encodes. <c>None</c> means the primitive is used as-is.
/// </summary>
/// <remarks>
/// This is every logical type Apache.Avro 1.12.1 implements. The ones it does not — <c>duration</c>,
/// <c>big-decimal</c>, the <c>*-nanos</c> family, and anything on a <c>fixed</c> base — are unreachable
/// through it: <c>Schema.Parse</c> throws on all of them, so modelling them here would buy nothing.
/// </remarks>
enum AvroLogicalKind {
    None,
    Uuid,
    Decimal,
    Date,
    TimeMillis,
    TimeMicros,
    TimestampMillis,
    TimestampMicros,
    LocalTimestampMillis,
    LocalTimestampMicros,

    /// <remarks>
    /// Apache does not model this one — it resolves to <c>UnknownLogicalType</c> over a <c>long</c>, so
    /// the value crossing <c>ISpecificRecord.Get</c> stays a <c>long</c> and only the property is richer.
    /// </remarks>
    TimestampNanos,

    /// <remarks>
    /// Months, days and milliseconds as three little-endian unsigned 32-bit values in a <c>fixed(12)</c>.
    /// Modelled only under the NodaTime time model, where <c>Period</c> can carry all three; the BCL has
    /// no type for it, so it is erased to the fixed otherwise. Apache models it in neither case.
    /// </remarks>
    Duration
}

abstract record AvroSchema;

/// <param name="Scale">Decimal scale, read from the schema at compile time so no runtime lookup is needed.</param>
sealed record AvroPrimitive(AvroPrimitiveKind Kind, AvroLogicalKind Logical = AvroLogicalKind.None, int Scale = 0) : AvroSchema;

/// <param name="Default">
/// The field's declared default, as the raw JSON node. Kept as JSON rather than a converted value
/// because its meaning depends on the field's schema — <c>"ÿ"</c> is a string for a
/// <c>string</c> field and a single byte for a <c>bytes</c> field.
/// </param>
sealed record AvroField(string Name, AvroSchema Type, string? Doc = null, JsonValue? Default = null);

/// <summary>A schema that declares a C# type: a record, an enum or a fixed.</summary>
interface IAvroNamed {
    string     Namespace   { get; }
    string     Name        { get; }
    string?    Doc         { get; }
    string     FullName    { get; }

    /// <summary>
    /// The declaration's own JSON node, used to re-emit the type's Avro schema text verbatim.
    /// </summary>
    JsonValue? Declaration { get; }
}

sealed record AvroRecord(string Namespace, string Name, IReadOnlyList<AvroField> Fields, string? Doc = null)
    : AvroSchema, IAvroNamed {
    public string     FullName    => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}.{Name}";
    public JsonValue? Declaration { get; init; }
}

sealed record AvroEnum(string Namespace, string Name, IReadOnlyList<string> Symbols, string? Doc = null)
    : AvroSchema, IAvroNamed {
    public string     FullName    => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}.{Name}";
    public JsonValue? Declaration { get; init; }
}

/// <param name="Logical">
/// <c>decimal</c> is the only logical type Apache.Avro accepts on a fixed base. When set, the field's
/// CLR type is <c>AvroDecimal</c> and no C# class is emitted for the fixed itself — nothing refers to it.
/// </param>
sealed record AvroFixed(
    string          Namespace,
    string          Name,
    int             Size,
    string?         Doc     = null,
    AvroLogicalKind Logical = AvroLogicalKind.None,
    int             Scale   = 0)
    : AvroSchema, IAvroNamed {
    public string     FullName    => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}.{Name}";
    public JsonValue? Declaration { get; init; }
}

sealed record AvroArray(AvroSchema Items) : AvroSchema;

sealed record AvroMap(AvroSchema Values) : AvroSchema;

sealed record AvroUnion(IReadOnlyList<AvroSchema> Branches) : AvroSchema {
    /// <summary>
    /// True for the `["null", X]` shape, which maps to a nullable CLR type rather than a discriminated
    /// union. Any other union arity is not representable as a single generated property.
    /// </summary>
    public bool IsNullable => Branches.Count == 2 && Branches.Any(b => b is AvroPrimitive { Kind: AvroPrimitiveKind.Null });

    public AvroSchema? NullableBranch =>
        IsNullable ? Branches.First(b => b is not AvroPrimitive { Kind: AvroPrimitiveKind.Null }) : null;

    public int NullBranchIndex =>
        Branches.ToList().FindIndex(b => b is AvroPrimitive { Kind: AvroPrimitiveKind.Null });

    /// <summary>
    /// True when AvroSourceGenerator represents this union as a generated variant interface rather than
    /// <c>object</c>: every branch is a record or null, and at least one is a record.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>UnionSchema.SupportsVariant()</c> in AvroSourceGenerator. The two generators read the
    /// same .avsc and must agree on the CLR type of every field, so this predicate is a deliberate
    /// coupling to that version and is covered by a test.
    /// </remarks>
    public bool IsVariant(Func<AvroSchema, AvroSchema> resolve) {
        if (Branches.Count < 2 || IsNullable) return false;

        var resolved = Branches.Select(resolve).ToList();

        return resolved.All(b => b is AvroRecord or AvroPrimitive { Kind: AvroPrimitiveKind.Null })
            && resolved.Any(b => b is AvroRecord);
    }
}

/// <summary>
/// A reference to a named type defined earlier in the same schema document. Avro allows a named type to
/// be declared once and referenced by name afterwards, so the parser records the reference and the
/// emitter resolves it against the document's named-type table.
/// </summary>
sealed record AvroNamedReference(string FullName) : AvroSchema;
