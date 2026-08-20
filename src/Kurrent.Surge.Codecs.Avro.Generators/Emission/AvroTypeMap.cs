using System.Linq;
using Kurrent.Surge.Codecs.Avro.Generators.Model;

namespace Kurrent.Surge.Codecs.Avro.Generators.Emission;

/// <summary>Identifies the field a schema node belongs to, so a union can be named.</summary>
readonly record struct FieldSite(AvroRecord Owner, string Field);

/// <summary>
/// The CLR type each Avro schema node maps to.
/// </summary>
/// <remarks>
/// Shared by the type emitter and the codec emitter on purpose. They declare and consume the same
/// members, so a disagreement between them is a compile error in the consumer's build rather than
/// anything either generator could detect.
/// </remarks>
sealed class AvroTypeMap(AvroSchemaDocument document) {
    public AvroSchema Resolve(AvroSchema schema) => document.Resolve(schema);

    public string TypeOf(AvroSchema schema, FieldSite site) =>
        document.Resolve(schema) switch {
            AvroPrimitive primitive => PrimitiveType(primitive),
            AvroRecord record       => AvroNaming.QualifiedTypeName(record),
            AvroEnum @enum          => AvroNaming.QualifiedTypeName(@enum),

            // A logical-annotated fixed surfaces as its logical type; the fixed is only its storage.
            AvroFixed { Logical: AvroLogicalKind.Decimal } => "global::Avro.AvroDecimal",
            AvroFixed { Logical: AvroLogicalKind.Uuid }    => "global::System.Guid",
            AvroFixed @fixed                              => AvroNaming.QualifiedTypeName(@fixed),
            AvroArray array         => $"global::System.Collections.Generic.IReadOnlyList<{TypeOf(array.Items, site)}>",
            AvroMap map             => $"global::System.Collections.Generic.IReadOnlyDictionary<string, {TypeOf(map.Values, site)}>",
            AvroUnion union         => UnionType(union, site),
            _                       => "object?"
        };

    /// <summary>The type the decoder builds before handing it to the property.</summary>
    /// <remarks>
    /// A collection reaches the public surface as a read-only interface, but the decoder needs the
    /// concrete type to size it from the block count and add to it.
    /// </remarks>
    public string ConcreteTypeOf(AvroSchema schema, FieldSite site) =>
        document.Resolve(schema) switch {
            AvroArray array => $"global::System.Collections.Generic.List<{TypeOf(array.Items, site)}>",
            AvroMap map     => $"global::System.Collections.Generic.Dictionary<string, {TypeOf(map.Values, site)}>",
            _               => TypeOf(schema, site)
        };

    /// <summary>
    /// The type a union's value is cast to once its null branch is excluded.
    /// </summary>
    /// <remarks>
    /// <c>Put</c> receives a boxed value and needs the non-nullable form: casting a boxed <c>int</c>
    /// straight to <c>int?</c> is not a valid unboxing conversion.
    /// </remarks>
    public string UnderlyingTypeOf(AvroSchema schema, FieldSite site) =>
        document.Resolve(schema) is AvroUnion { IsNullable: true } union
            ? TypeOf(union.NullableBranch!, site)
            : TypeOf(schema, site);

    public bool IsNullable(AvroSchema schema) =>
        document.Resolve(schema) is AvroUnion union
     && (union.IsNullable || union.Branches.Any(branch => document.Resolve(branch) is AvroPrimitive { Kind: AvroPrimitiveKind.Null }));

    public bool IsValueType(AvroSchema schema) =>
        document.Resolve(schema) switch {
            // Every logical type maps to a struct — Guid, AvroDecimal, DateTime, TimeSpan — even when its
            // base primitive is a reference type such as string or bytes.
            AvroPrimitive { Logical: not AvroLogicalKind.None } => true,
            AvroPrimitive { Kind: AvroPrimitiveKind.Bytes or AvroPrimitiveKind.String or AvroPrimitiveKind.Null } => false,
            AvroPrimitive => true,
            AvroEnum      => true,
            AvroFixed { Logical: not AvroLogicalKind.None } => true,
            _             => false
        };

    string UnionType(AvroUnion union, FieldSite site) {
        if (union.IsNullable) return $"{TypeOf(union.NullableBranch!, site)}?";

        if (union.IsVariant(document.Resolve))
            return AvroNaming.QualifiedVariantInterfaceName(site.Owner, site.Field);

        return union.Branches.Any(branch => document.Resolve(branch) is AvroPrimitive { Kind: AvroPrimitiveKind.Null })
            ? "object?"
            : "object";
    }

    static string PrimitiveType(AvroPrimitive primitive) => primitive.Logical switch {
        AvroLogicalKind.Uuid                 => "global::System.Guid",
        AvroLogicalKind.Decimal              => "global::Avro.AvroDecimal",
        AvroLogicalKind.Date                 => "global::System.DateTime",
        AvroLogicalKind.TimestampMillis      => "global::System.DateTime",
        AvroLogicalKind.TimestampMicros      => "global::System.DateTime",
        AvroLogicalKind.LocalTimestampMillis => "global::System.DateTime",
        AvroLogicalKind.LocalTimestampMicros => "global::System.DateTime",
        AvroLogicalKind.TimeMillis           => "global::System.TimeSpan",
        AvroLogicalKind.TimeMicros           => "global::System.TimeSpan",
        AvroLogicalKind.TimestampNanos       => "global::Kurrent.Surge.Codecs.Avro.NanoInstant",

        _ => primitive.Kind switch {
            AvroPrimitiveKind.Boolean => "bool",
            AvroPrimitiveKind.Int     => "int",
            AvroPrimitiveKind.Long    => "long",
            AvroPrimitiveKind.Float   => "float",
            AvroPrimitiveKind.Double  => "double",
            AvroPrimitiveKind.Bytes   => "byte[]",
            AvroPrimitiveKind.String  => "string",
            _                         => "object?"
        }
    };
}
