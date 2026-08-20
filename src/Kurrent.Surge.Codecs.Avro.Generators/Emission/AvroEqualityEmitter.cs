using Kurrent.Surge.Codecs.Avro.Generators.Model;

namespace Kurrent.Surge.Codecs.Avro.Generators.Emission;

/// <summary>
/// Builds the comparison and hash expressions a generated record uses for value equality.
/// </summary>
/// <remarks>
/// A record's synthesized equality compares a <c>byte[]</c>, a list or a dictionary member by reference,
/// so two decoded messages holding identical data come out unequal. Declaring <c>Equals</c> and
/// <c>GetHashCode</c> suppresses the synthesized pair, and the compiler still routes <c>==</c> through
/// them.
/// <para>
/// Everything else already compares structurally: strings, enums, <c>Guid</c>, <c>DateTime</c>,
/// <c>AvroDecimal</c>, <c>NanoInstant</c>, a generated record, and Apache's <c>SpecificFixed</c>. Those
/// go through <c>EqualityComparer&lt;T&gt;.Default</c>, which picks the right one and tolerates null.
/// </para>
/// </remarks>
sealed class AvroEqualityEmitter(AvroSchemaDocument document, AvroTypeMap types) {
    const string Helper = "global::Kurrent.Surge.Codecs.Avro.AvroEquality";

    /// <summary>An expression comparing two values of the field's type.</summary>
    public string Comparison(AvroSchema schema, string left, string right, FieldSite site) {
        switch (Shape(schema)) {
            case AvroPrimitive { Logical: AvroLogicalKind.None, Kind: AvroPrimitiveKind.Bytes }:
                return $"{Helper}.BytesEqual({left}, {right})";

            case AvroArray array: {
                var element = types.TypeOf(array.Items, site);

                return $"{Helper}.ListEqual<{element}>({left}, {right}, static (x, y) => {Comparison(array.Items, "x", "y", site)})";
            }

            case AvroMap map: {
                var value = types.TypeOf(map.Values, site);

                return $"{Helper}.MapEqual<{value}>({left}, {right}, static (x, y) => {Comparison(map.Values, "x", "y", site)})";
            }

            default:
                return $"global::System.Collections.Generic.EqualityComparer<{types.TypeOf(schema, site)}>.Default.Equals({left}, {right})";
        }
    }

    /// <summary>An expression producing a hash for one value of the field's type.</summary>
    public string Hash(AvroSchema schema, string value, FieldSite site) {
        switch (Shape(schema)) {
            case AvroPrimitive { Logical: AvroLogicalKind.None, Kind: AvroPrimitiveKind.Bytes }:
                return $"{Helper}.BytesHash({value})";

            case AvroArray array: {
                var element = types.TypeOf(array.Items, site);

                return $"{Helper}.ListHash<{element}>({value}, static x => {Hash(array.Items, "x", site)})";
            }

            case AvroMap map: {
                var element = types.TypeOf(map.Values, site);

                return $"{Helper}.MapHash<{element}>({value}, static x => {Hash(map.Values, "x", site)})";
            }

            default:
                return $"global::System.Collections.Generic.EqualityComparer<{types.TypeOf(schema, site)}>.Default.GetHashCode({value}!)";
        }
    }

    /// <summary>
    /// The schema that decides how a field compares.
    /// </summary>
    /// <remarks>
    /// A null union delegates to its other branch, because a nullable list still needs element-wise
    /// comparison and the helpers already treat null as a value. Every other union is typed as
    /// <c>object</c> or as a variant interface, and its runtime type carries the right equality.
    /// </remarks>
    AvroSchema Shape(AvroSchema schema) =>
        document.Resolve(schema) is AvroUnion { IsNullable: true } union
            ? document.Resolve(union.NullableBranch!)
            : document.Resolve(schema);
}
