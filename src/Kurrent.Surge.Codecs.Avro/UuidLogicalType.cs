using Avro;
using Avro.Generic;
using Avro.Util;

namespace Kurrent.Surge.Codecs.Avro;

/// <summary>
/// Widens Apache's <c>uuid</c> logical type to accept a <c>fixed(16)</c> base.
/// </summary>
/// <remarks>
/// The Avro specification has allowed <c>uuid</c> on <c>fixed(16)</c> since 1.12, and Java supports it.
/// Apache.Avro's C# implementation rejects it in <c>Uuid.ValidateSchema</c>, and that check runs before
/// the unknown-logical-type fallback can catch it — so <c>Schema.Parse</c> throws and a generated type's
/// <c>ISpecificRecord.Schema</c> throws with it.
/// <para>
/// Registering this replaces the built-in for the whole process, which is why the generator emits the
/// registration only into assemblies that actually declare such a schema. It only widens: a
/// <c>string</c> base behaves exactly as before.
/// </para>
/// <para>
/// Registration is not needed for the codec itself. Generated encode and decode write the sixteen bytes
/// directly; this exists so the schema can be parsed and so Apache's own readers and writers work.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class UuidLogicalType() : LogicalType(LogicalTypeName) {
    public const string LogicalTypeName = "uuid";

    const int UuidBytes = 16;

    /// <summary>Registers this in place of Apache's built-in <c>uuid</c>, once per process.</summary>
    public static void Enable() => LogicalTypeFactory.Instance.Register(new UuidLogicalType());

    /// <inheritdoc />
    public override void ValidateSchema(LogicalSchema schema) {
        if (schema.BaseSchema.Tag is global::Avro.Schema.Type.String) return;

        if (schema.BaseSchema.Tag is global::Avro.Schema.Type.Fixed && ((FixedSchema)schema.BaseSchema).Size == UuidBytes) return;

        throw new AvroTypeException($"'{LogicalTypeName}' can only be used with an underlying string or fixed({UuidBytes}) type");
    }

    /// <inheritdoc />
    /// <remarks>
    /// A fixed carries the sixteen bytes in RFC 4122 order. <see cref="Guid.ToByteArray()"/> without an
    /// argument is little-endian across the first three fields and would byte-swap every value.
    /// </remarks>
    public override object ConvertToBaseValue(object logicalValue, LogicalSchema schema) {
        var value = (Guid)logicalValue;

        return schema.BaseSchema.Tag is global::Avro.Schema.Type.String
            ? value.ToString()
            : new GenericFixed((FixedSchema)schema.BaseSchema, value.ToByteArray(bigEndian: true));
    }

    /// <inheritdoc />
    public override object ConvertToLogicalValue(object baseValue, LogicalSchema schema) =>
        baseValue is string text ? new Guid(text) : new Guid(((GenericFixed)baseValue).Value, bigEndian: true);

    /// <inheritdoc />
    public override Type GetCSharpType(bool nullible) => nullible ? typeof(Guid?) : typeof(Guid);

    /// <inheritdoc />
    public override bool IsInstanceOfLogicalType(object logicalValue) => logicalValue is Guid;
}
