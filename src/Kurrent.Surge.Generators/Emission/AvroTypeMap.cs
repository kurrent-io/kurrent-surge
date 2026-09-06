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
sealed class AvroTypeMap(AvroSchemaDocument document, AvroGeneratorOptions options) {
    public AvroSchema Resolve(AvroSchema schema) => document.Resolve(schema);

    public string TypeOf(AvroSchema schema, FieldSite site) =>
        document.Resolve(schema) switch {
            AvroPrimitive primitive => PrimitiveType(primitive, options),
            AvroRecord record       => AvroNaming.QualifiedTypeName(record),
            AvroEnum @enum          => AvroNaming.QualifiedTypeName(@enum),

            // A logical-annotated fixed surfaces as its logical type; the fixed is only its storage.
            AvroFixed { Logical: AvroLogicalKind.Decimal } => DecimalType(options),
            AvroFixed { Logical: AvroLogicalKind.Uuid }    => "global::System.Guid",

            // Only Period carries months, days and milliseconds together. Without it the annotation is
            // erased and the field stays the fixed itself, which is what the specification asks for.
            AvroFixed { Logical: AvroLogicalKind.Duration } when options.UsesNodaTime => "global::NodaTime.Period",

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
            // Every logical type maps to a struct — Guid, AvroDecimal, DateOnly, TimeOnly,
            // DateTimeOffset — even when its base primitive is a reference type such as string or bytes.
            AvroPrimitive { Logical: not AvroLogicalKind.None } => true,
            AvroPrimitive { Kind: AvroPrimitiveKind.Bytes or AvroPrimitiveKind.String or AvroPrimitiveKind.Null } => false,
            AvroPrimitive => true,
            AvroEnum      => true,

            // Period is a class, unlike every other logical type's CLR form.
            AvroFixed { Logical: AvroLogicalKind.Duration } when options.UsesNodaTime => false,

            AvroFixed { Logical: not AvroLogicalKind.None } => true,
            _             => false
        };

    string UnionType(AvroUnion union, FieldSite site) {
        if (union.IsNullable) return $"{TypeOf(union.NullableBranch!, site)}?";

        // A null branch drops `required` from the property, so the type has to admit null or the
        // property is left non-nullable, unassigned and uninitialised. IsNullable below reads the same
        // branches, and the two answers have to agree.
        var suffix = IsNullable(union) ? "?" : "";

        return union.IsVariant(document.Resolve)
            ? $"{AvroNaming.QualifiedVariantInterfaceName(site.Owner, site.Field)}{suffix}"
            : $"object{suffix}";
    }

    /// <remarks>
    /// Three of these are narrower than the type Apache exposes, and each converts back at the
    /// <c>ISpecificRecord</c> boundary so Apache's own readers and writers are unaffected.
    /// <para>
    /// <c>date</c> carries no time and no <see cref="System.DateTimeKind"/>, and <c>time-*</c> is a
    /// reading on a clock rather than a duration — <c>TimeSpan</c> can hold a negative value and one past
    /// 24 hours, neither of which the format can represent.
    /// </para>
    /// <para>
    /// An absolute <c>timestamp</c> is <c>DateTimeOffset</c> because a <c>DateTime</c> with
    /// <c>Kind=Unspecified</c> encodes differently on two machines: Apache applies
    /// <c>ToUniversalTime()</c>, which silently reads the local timezone. The offset itself is not on the
    /// wire, so a value round-trips to the same instant at offset zero rather than to its original
    /// offset. The <c>local-timestamp</c> variants stay <c>DateTime</c> — they are deliberately wall-clock
    /// readings and not instants at all.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// Apache's <c>AvroDecimal</c> only when its readers and writers need to see one. Ours otherwise —
    /// same unscaled-value-and-scale shape, and its comparison does not overflow past 96 bits.
    /// </remarks>
    static string DecimalType(AvroGeneratorOptions options) =>
        options.EmitsApacheInterop ? "global::Avro.AvroDecimal" : "global::Kurrent.Surge.Codecs.Avro.AvroDecimal";

    static string PrimitiveType(AvroPrimitive primitive, AvroGeneratorOptions options) => primitive.Logical switch {
        AvroLogicalKind.Uuid    => "global::System.Guid",
        AvroLogicalKind.Decimal => DecimalType(options),

        AvroLogicalKind.Date when options.UsesNodaTime => "global::NodaTime.LocalDate",
        AvroLogicalKind.Date                          => "global::System.DateOnly",

        AvroLogicalKind.TimeMillis or AvroLogicalKind.TimeMicros when options.UsesNodaTime => "global::NodaTime.LocalTime",
        AvroLogicalKind.TimeMillis or AvroLogicalKind.TimeMicros                          => "global::System.TimeOnly",

        AvroLogicalKind.TimestampMillis or AvroLogicalKind.TimestampMicros when options.UsesNodaTime => "global::NodaTime.Instant",
        AvroLogicalKind.TimestampMillis or AvroLogicalKind.TimestampMicros                          => "global::System.DateTimeOffset",

        AvroLogicalKind.LocalTimestampMillis or AvroLogicalKind.LocalTimestampMicros when options.UsesNodaTime => "global::NodaTime.LocalDateTime",
        AvroLogicalKind.LocalTimestampMillis or AvroLogicalKind.LocalTimestampMicros                          => "global::System.DateTime",

        AvroLogicalKind.TimestampNanos when options.EmitsRichNanoseconds && options.UsesNodaTime => "global::NodaTime.Instant",
        AvroLogicalKind.TimestampNanos when options.EmitsRichNanoseconds                        => "global::Kurrent.Surge.Codecs.Avro.NanoInstant",
        AvroLogicalKind.TimestampNanos                                                          => "long",

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
