using System.Collections.Generic;
using System.Linq;
using Kurrent.Surge.Codecs.Avro.Generators.Model;

namespace Kurrent.Surge.Codecs.Avro.Generators.Emission;

/// <summary>
/// Emits straight-line encode/decode methods for an Avro schema.
/// </summary>
/// <remarks>
/// The emitted code drives <c>AvroBinaryEncoder</c>/<c>AvroBinaryDecoder</c> — our own primitives — and
/// never touches Apache's <c>SpecificDatumReader</c>/<c>SpecificDatumWriter</c>. Those walk the schema at
/// runtime through <c>Avro.Specific.ObjectCreator</c>, which resolves types via
/// <c>Type.GetType(string)</c> and <c>MakeGenericType</c> — IL3050, unavailable under Native AOT. Every
/// type this emitter needs is written into the generated source instead, so nothing is resolved at
/// runtime.
/// </remarks>
sealed class AvroCodecEmitter(AvroSchemaDocument document, AvroGeneratorOptions options) {
    readonly CodeWriter                     _writer   = new();
    readonly AvroTypeMap                    _types    = new(document, options);
    readonly Dictionary<string, AvroRecord> _records  = [];
    readonly HashSet<AvroLogicalKind>       _logicals = [];

    /// <summary>Tracked apart from <see cref="_logicals"/>: a fixed-backed decimal needs its own helpers.</summary>
    bool _decimalFixed;

    /// <summary>Likewise for a fixed-backed duration, which only the NodaTime model can carry.</summary>
    bool _duration;

    /// <summary>Nesting depth, so a collection inside a collection does not reuse a loop variable name.</summary>
    int _depth;

    /// <summary>
    /// Counts map sites across the whole codec, so their local functions do not collide.
    /// </summary>
    /// <remarks>
    /// Depth is not enough. Two sibling maps sit at the same depth and their loop variables are scoped
    /// to their own blocks, but a local function is scoped to the whole method and would redeclare.
    /// </remarks>
    int _mapSites;

    /// <param name="root">The record the caller has already verified as the schema root.</param>
    public string Emit(AvroRecord root) {
        CollectRecords(root);

        _writer.Lines(Templates.FileHeader);
        _writer.Line();

        using (_writer.Block($"internal static class {CodecClassName(root)}")) {
            _writer.Lines(
                $"""
                 public static void Encode({TypeName(root)} value, global::Kurrent.Surge.Codecs.Avro.Binary.AvroBinaryEncoder encoder) =>
                     {EncodeMethodName(root)}(value, encoder);

                 public static {TypeName(root)} Decode(global::Kurrent.Surge.Codecs.Avro.Binary.AvroBinaryDecoder decoder) =>
                     {DecodeMethodName(root)}(decoder);
                 """);

            EmitLogicalTypeSupport(_logicals);

            foreach (var record in _records.Values) {
                _writer.Line();
                EmitRecordEncoder(record);
                _writer.Line();
                EmitRecordDecoder(record);
            }
        }

        return _writer.ToString();
    }

    /// <remarks>
    /// Only the members the schema actually uses are emitted. Values mirror Apache.Avro's
    /// <c>LogicalUnixEpochType</c> and <c>Decimal</c>.
    /// </remarks>
    void EmitLogicalTypeSupport(HashSet<AvroLogicalKind> logicals) {
        if (Needs(
            AvroLogicalKind.TimestampMillis,
            AvroLogicalKind.TimestampMicros,
            AvroLogicalKind.LocalTimestampMillis,
            AvroLogicalKind.LocalTimestampMicros)) {
            _writer.Line();
            _writer.Lines(Templates.UnixEpoch);
        }

        if (Needs(AvroLogicalKind.Date)) {
            _writer.Line();
            _writer.Lines(Templates.UnixEpochDay);
        }

        if (Needs(AvroLogicalKind.TimeMicros, AvroLogicalKind.TimestampMicros, AvroLogicalKind.LocalTimestampMicros)) {
            _writer.Line();
            _writer.Lines(Templates.TicksPerMicrosecond);
        }

        if (Needs(AvroLogicalKind.Decimal)) {
            _writer.Line();
            _writer.Lines(Decimals(Templates.DecimalConverters));
        }

        if (_decimalFixed) {
            _writer.Line();
            _writer.Lines(Decimals(Templates.DecimalFixedConverters));
        }

        if (_duration) {
            _writer.Line();
            _writer.Lines(Templates.DurationConverters);
        }

        if (Needs(AvroLogicalKind.TimestampNanos) && options.EmitsRichNanoseconds && options.UsesNodaTime) {
            _writer.Line();
            _writer.Lines(Templates.NanosecondInstantConverters);
        }

        bool Needs(params AvroLogicalKind[] kinds) => kinds.Any(logicals.Contains);
    }

    /// <summary>
    /// Points the decimal helpers at whichever <c>AvroDecimal</c> the generated properties use.
    /// </summary>
    /// <remarks>
    /// Substituted rather than interpolated: the templates are raw string literals full of braces from
    /// their own interpolated messages, and making them format strings would mean escaping every one.
    /// </remarks>
    string Decimals(string template) =>
        options.EmitsApacheInterop
            ? template
            : template.Replace("global::Avro.AvroDecimal", "global::Kurrent.Surge.Codecs.Avro.AvroDecimal");

    #region ->> Record traversal <<-

    void CollectRecords(AvroSchema schema) {
        switch (document.Resolve(schema)) {
            // Logical kinds are collected in the same walk: the helper members they need are emitted
            // before the record methods, so usage has to be known up front.
            case AvroPrimitive { Logical: not AvroLogicalKind.None } primitive:
                _logicals.Add(primitive.Logical);
                break;

            case AvroFixed { Logical: AvroLogicalKind.Decimal }:
                _decimalFixed = true;
                break;

            case AvroFixed { Logical: AvroLogicalKind.Duration } when options.UsesNodaTime:
                _duration = true;
                break;

            case AvroRecord record when !_records.ContainsKey(record.FullName):
                _records[record.FullName] = record;

                foreach (var field in record.Fields) CollectRecords(field.Type);

                break;

            case AvroArray array: CollectRecords(array.Items); break;
            case AvroMap map:     CollectRecords(map.Values);  break;

            case AvroUnion union:
                foreach (var branch in union.Branches) CollectRecords(branch);

                break;
        }
    }

    #endregion // Record traversal

    #region ->> Encoding <<-

    void EmitRecordEncoder(AvroRecord record) {
        using (_writer.Block($"static void {EncodeMethodName(record)}({TypeName(record)} value, global::Kurrent.Surge.Codecs.Avro.Binary.AvroBinaryEncoder encoder)")) {
            foreach (var field in record.Fields)
                EmitEncode(field.Type, $"value.{AvroNaming.Identifier(field.Name)}", new FieldSite(record, field.Name));
        }
    }

    void EmitEncode(AvroSchema schema, string expression, FieldSite site) {
        switch (document.Resolve(schema)) {
            case AvroPrimitive primitive:
                _writer.Line(PrimitiveWrite(primitive, expression));
                break;

            case AvroRecord record:
                _writer.Line($"{EncodeMethodName(record)}({expression}, encoder);");
                break;

            case AvroEnum:
                _writer.Line($"encoder.WriteEnum((int){expression});");
                break;

            case AvroFixed { Logical: AvroLogicalKind.Decimal } @decimal:
                _writer.Line($"WriteDecimalFixed({expression}, encoder, {@decimal.Scale}, {@decimal.Size});");
                break;

            case AvroFixed { Logical: AvroLogicalKind.Duration } when options.UsesNodaTime:
                _writer.Line($"WriteDuration({expression}, encoder);");
                break;

            case AvroFixed { Logical: AvroLogicalKind.Uuid }:
                _writer.Line($"encoder.WriteUuid({expression});");
                break;

            case AvroFixed:
                _writer.Line($"encoder.WriteFixed({expression}.Value);");
                break;

            case AvroArray array: {
                // Indexed rather than enumerated: foreach over IReadOnlyList boxes the list's struct
                // enumerator on every encode. The depth suffix keeps nested arrays from colliding.
                var index = $"i{_depth}";
                var item  = $"item{_depth}";

                _depth++;

                _writer.Line("encoder.WriteArrayStart();");
                _writer.Line($"encoder.SetItemCount({expression}.Count);");

                using (_writer.Block($"for (var {index} = 0; {index} < {expression}.Count; {index}++)")) {
                    _writer.Line("encoder.StartItem();");
                    _writer.Line($"var {item} = {expression}[{index}];");
                    EmitEncode(array.Items, item, site);
                }

                _depth--;

                _writer.Line("encoder.WriteArrayEnd();");
                break;
            }

            case AvroMap map: {
                var index = _mapSites++;
                var pair  = $"pair{index}";
                var entry = $"entry{index}";
                var typed = $"map{index}";
                var write = $"WriteMapItem{index}";

                var keyValue = $"global::System.Collections.Generic.KeyValuePair<string, {_types.TypeOf(map.Values, site)}>";

                _depth++;

                // The item body is a local function so the two loops below share it. Inlining it into
                // both would square the emitted code with every level of map nesting. A local function
                // that is never converted to a delegate captures by ref struct, so this allocates nothing.
                using (_writer.Block($"void {write}({keyValue} {pair})")) {
                    _writer.Line("encoder.StartItem();");
                    _writer.Line($"encoder.WriteString({pair}.Key);");
                    EmitEncode(map.Values, $"{pair}.Value", site);
                }

                _depth--;

                _writer.Line();
                _writer.Line("encoder.WriteMapStart();");
                _writer.Line($"encoder.SetItemCount({expression}.Count);");

                // foreach over IReadOnlyDictionary reaches the interface enumerator, which boxes
                // Dictionary's struct one on every encode. The decoder builds a Dictionary and a
                // collection initialiser produces one, so this test is the path that is actually taken.
                using (_writer.Block($"if ({expression} is {_types.ConcreteTypeOf(map, site)} {typed})")) {
                    _writer.Line($"foreach (var {entry} in {typed}) {write}({entry});");
                }

                using (_writer.Block("else")) {
                    _writer.Line($"foreach (var {entry} in {expression}) {write}({entry});");
                }

                _writer.Line("encoder.WriteMapEnd();");
                break;
            }

            case AvroUnion union when union.IsNullable:
                EmitNullableEncode(union, expression, site);
                break;

            case AvroUnion union:
                EmitUnionEncode(union, expression, site);
                break;
        }
    }

    void EmitNullableEncode(AvroUnion union, string expression, FieldSite site) {
        var underlying = union.NullableBranch!;
        var nullIndex  = union.NullBranchIndex;
        var valueIndex = nullIndex == 0 ? 1 : 0;

        using (_writer.Block($"if ({expression} is null)")) {
            _writer.Line($"encoder.WriteUnionIndex({nullIndex});");
            _writer.Line("encoder.WriteNull();");
        }

        using (_writer.Block("else")) {
            _writer.Line($"encoder.WriteUnionIndex({valueIndex});");

            // A nullable value type is Nullable<T>; the branch encoder needs the unwrapped value.
            EmitEncode(underlying, IsValueType(underlying) ? $"{expression}.Value" : expression, site);
        }
    }

    void EmitUnionEncode(AvroUnion union, string expression, FieldSite site) {
        // The declared CLR type is either a variant interface or object, so the branch is recovered by a
        // runtime type test. Order follows the schema, because the index written is the schema position.
        using (_writer.Block($"switch ({expression})")) {
            for (var index = 0; index < union.Branches.Count; index++) {
                var branch = document.Resolve(union.Branches[index]);

                if (branch is AvroPrimitive { Kind: AvroPrimitiveKind.Null }) {
                    _writer.Line($"case null:");
                    _writer.Line($"    encoder.WriteUnionIndex({index});");
                    _writer.Line($"    encoder.WriteNull();");
                    _writer.Line($"    break;");

                    continue;
                }

                var local = $"branch{index}";

                using (_writer.Block($"case {CSharpType(branch, site)} {local}:")) {
                    _writer.Line($"encoder.WriteUnionIndex({index});");
                    EmitEncode(branch, local, site);
                    _writer.Line("break;");
                }
            }

            _writer.Line("default:");
            _writer.Line($"    throw new global::System.ArgumentOutOfRangeException(nameof(value), $\"value of type {{{expression}?.GetType()}} does not match any branch of the union for field '{site.Field}'.\");");
        }
    }

    /// <remarks>
    /// Logical-type conversions mirror Apache.Avro's own <c>ConvertToBaseValue</c> implementations
    /// (<c>Util/Date.cs</c>, <c>Uuid.cs</c>, <c>Timestamp*.cs</c>, <c>Decimal.cs</c>) so the bytes match
    /// what <c>SpecificDatumWriter</c> would have produced.
    /// </remarks>
    /// <summary>
    /// Rewrites a NodaTime-typed expression as the BCL type the wire arithmetic is written against.
    /// </summary>
    /// <remarks>
    /// Both models therefore run byte-identical conversion code, which is what makes the time model a
    /// choice of surface type rather than a second encoding to keep in step with Apache.
    /// </remarks>
    string ToBcl(AvroLogicalKind logical, string expression) => !options.UsesNodaTime ? expression : logical switch {
        AvroLogicalKind.Date => $"{expression}.ToDateOnly()",
        AvroLogicalKind.TimeMillis or AvroLogicalKind.TimeMicros => $"{expression}.ToTimeOnly()",
        AvroLogicalKind.TimestampMillis or AvroLogicalKind.TimestampMicros => $"{expression}.ToDateTimeOffset()",
        AvroLogicalKind.LocalTimestampMillis or AvroLogicalKind.LocalTimestampMicros =>
            $"{expression}.ToDateTimeUnspecified()",
        _ => expression
    };

    string FromBcl(AvroLogicalKind logical, string read) => !options.UsesNodaTime ? read : logical switch {
        AvroLogicalKind.Date => $"global::NodaTime.LocalDate.FromDateOnly({read})",
        AvroLogicalKind.TimeMillis or AvroLogicalKind.TimeMicros => $"global::NodaTime.LocalTime.FromTimeOnly({read})",
        AvroLogicalKind.TimestampMillis or AvroLogicalKind.TimestampMicros =>
            $"global::NodaTime.Instant.FromDateTimeOffset({read})",
        AvroLogicalKind.LocalTimestampMillis or AvroLogicalKind.LocalTimestampMicros =>
            $"global::NodaTime.LocalDateTime.FromDateTime({read})",
        _ => read
    };

    string PrimitiveWrite(AvroPrimitive primitive, string raw) => Write(primitive, ToBcl(primitive.Logical, raw));

    string Write(AvroPrimitive primitive, string expression) => primitive.Logical switch {
        AvroLogicalKind.Uuid            => $"encoder.WriteString({expression}.ToString());",
        AvroLogicalKind.Date            => $"encoder.WriteInt({expression}.DayNumber - UnixEpochDay);",
        AvroLogicalKind.TimeMillis      => $"encoder.WriteInt((int){expression}.ToTimeSpan().TotalMilliseconds);",
        AvroLogicalKind.TimeMicros      => $"encoder.WriteLong({expression}.Ticks / TicksPerMicrosecond);",
        AvroLogicalKind.Decimal         => $"WriteDecimal({expression}, encoder, {primitive.Scale});",

        AvroLogicalKind.TimestampNanos when options.EmitsRichNanoseconds && options.UsesNodaTime =>
            $"encoder.WriteLong(ToUnixNanoseconds({expression}));",

        AvroLogicalKind.TimestampNanos when options.EmitsRichNanoseconds =>
            $"encoder.WriteLong({expression}.TotalNanoseconds);",

        AvroLogicalKind.TimestampNanos => $"encoder.WriteLong({expression});",

        // UtcDateTime rather than ToUnixTimeMilliseconds: the two disagree by one millisecond at
        // DateTime.MaxValue, and the subtraction is Apache's own arithmetic.
        AvroLogicalKind.TimestampMillis =>
            $"encoder.WriteLong((long)({expression}.UtcDateTime - UnixEpoch).TotalMilliseconds);",

        AvroLogicalKind.TimestampMicros =>
            $"encoder.WriteLong(({expression}.UtcDateTime - UnixEpoch).Ticks / TicksPerMicrosecond);",

        // The local variants write what the absolute ones write; only the read differs. They stay
        // DateTime, so ToUniversalTime is still the conversion Apache applies.
        AvroLogicalKind.LocalTimestampMillis =>
            $"encoder.WriteLong((long)({expression}.ToUniversalTime() - UnixEpoch).TotalMilliseconds);",

        AvroLogicalKind.LocalTimestampMicros =>
            $"encoder.WriteLong(({expression}.ToUniversalTime() - UnixEpoch).Ticks / TicksPerMicrosecond);",

        _ => primitive.Kind switch {
            AvroPrimitiveKind.Null    => "encoder.WriteNull();",
            AvroPrimitiveKind.Boolean => $"encoder.WriteBoolean({expression});",
            AvroPrimitiveKind.Int     => $"encoder.WriteInt({expression});",
            AvroPrimitiveKind.Long    => $"encoder.WriteLong({expression});",
            AvroPrimitiveKind.Float   => $"encoder.WriteFloat({expression});",
            AvroPrimitiveKind.Double  => $"encoder.WriteDouble({expression});",
            AvroPrimitiveKind.Bytes   => $"encoder.WriteBytes({expression});",
            _                         => $"encoder.WriteString({expression});"
        }
    };

    #endregion // Encoding

    #region ->> Decoding <<-

    void EmitRecordDecoder(AvroRecord record) {
        using (_writer.Block($"static {TypeName(record)} {DecodeMethodName(record)}(global::Kurrent.Surge.Codecs.Avro.Binary.AvroBinaryDecoder decoder)")) {
            var locals = new List<string>();

            foreach (var field in record.Fields) {
                var local = $"_{field.Name}";
                EmitDecodeInto(field.Type, local, new FieldSite(record, field.Name));
                locals.Add($"{AvroNaming.Identifier(field.Name)} = {local}");
            }

            _writer.Line();
            _writer.Line($"return new {TypeName(record)} {{ {string.Join(", ", locals)} }};");
        }
    }

    void EmitDecodeInto(AvroSchema schema, string local, FieldSite site) {
        switch (document.Resolve(schema)) {
            case AvroPrimitive primitive:
                _writer.Line($"var {local} = {PrimitiveRead(primitive)};");
                break;

            case AvroRecord record:
                _writer.Line($"var {local} = {DecodeMethodName(record)}(decoder);");
                break;

            case AvroEnum @enum:
                _writer.Line($"var {local} = ({TypeName(@enum)})decoder.ReadEnum();");
                break;

            case AvroFixed { Logical: AvroLogicalKind.Decimal } @decimal:
                _writer.Line($"var {local} = ReadDecimalFixed(decoder, {@decimal.Scale}, {@decimal.Size});");
                break;

            case AvroFixed { Logical: AvroLogicalKind.Duration } when options.UsesNodaTime:
                _writer.Line($"var {local} = ReadDuration(decoder);");
                break;

            case AvroFixed { Logical: AvroLogicalKind.Uuid }:
                _writer.Line($"var {local} = decoder.ReadUuid();");
                break;

            case AvroFixed @fixed:
                _writer.Line($"var {local} = new {TypeName(@fixed)}();");
                _writer.Line($"decoder.ReadFixed({local}.Value);");
                break;

            case AvroArray array: {
                var itemLocal = $"{local}_item";

                _writer.Line($"var {local} = new {_types.ConcreteTypeOf(array, site)}();");

                using (_writer.Block($"for (var {local}_n = decoder.ReadArrayStart(); {local}_n != 0; {local}_n = decoder.ReadArrayNext())")) {
                    // Avro sends the element count per block, so the list is sized once instead of
                    // doubling its way up from zero.
                    _writer.Line($"{local}.EnsureCapacity({local}.Count + (int){local}_n);");

                    using (_writer.Block($"for (var {local}_i = 0L; {local}_i < {local}_n; {local}_i++)")) {
                        EmitDecodeInto(array.Items, itemLocal, site);
                        _writer.Line($"{local}.Add({itemLocal});");
                    }
                }

                break;
            }

            case AvroMap map: {
                var valueLocal = $"{local}_value";

                _writer.Line($"var {local} = new {_types.ConcreteTypeOf(map, site)}();");

                using (_writer.Block($"for (var {local}_n = decoder.ReadMapStart(); {local}_n != 0; {local}_n = decoder.ReadMapNext())")) {
                    _writer.Line($"{local}.EnsureCapacity({local}.Count + (int){local}_n);");

                    using (_writer.Block($"for (var {local}_i = 0L; {local}_i < {local}_n; {local}_i++)")) {
                        _writer.Line($"var {local}_key = decoder.ReadString();");
                        EmitDecodeInto(map.Values, valueLocal, site);
                        _writer.Line($"{local}[{local}_key] = {valueLocal};");
                    }
                }

                break;
            }

            case AvroUnion union when union.IsNullable: {
                var underlying  = union.NullableBranch!;
                var nullIndex   = union.NullBranchIndex;
                var branchLocal = $"{local}_branch";

                _writer.Line($"{CSharpType(union, site)} {local} = default;");

                using (_writer.Block($"if (decoder.ReadUnionIndex() == {nullIndex})")) {
                    _writer.Line("decoder.ReadNull();");
                }

                using (_writer.Block("else")) {
                    EmitDecodeInto(underlying, branchLocal, site);
                    _writer.Line($"{local} = {branchLocal};");
                }

                break;
            }

            case AvroUnion union:
                EmitUnionDecode(union, local, site);
                break;
        }
    }

    void EmitUnionDecode(AvroUnion union, string local, FieldSite site) {
        _writer.Line($"{CSharpType(union, site)} {local};");

        using (_writer.Block("switch (decoder.ReadUnionIndex())")) {
            for (var index = 0; index < union.Branches.Count; index++) {
                var branch = document.Resolve(union.Branches[index]);

                if (branch is AvroPrimitive { Kind: AvroPrimitiveKind.Null }) {
                    _writer.Line($"case {index}:");
                    _writer.Line($"    decoder.ReadNull();");
                    _writer.Line($"    {local} = null;");
                    _writer.Line($"    break;");

                    continue;
                }

                var branchLocal = $"{local}_b{index}";

                using (_writer.Block($"case {index}:")) {
                    EmitDecodeInto(branch, branchLocal, site);
                    _writer.Line($"{local} = {branchLocal};");
                    _writer.Line("break;");
                }
            }

            _writer.Line("default:");
            _writer.Line($"    throw new global::Avro.AvroException(\"Union index out of range for field '{site.Field}'.\");");
        }
    }

    /// <remarks>
    /// <c>ToLocalTime</c> on the local variants is Apache's behaviour, not ours
    /// (<c>LocalTimestampMillisecond.ConvertToLogicalValue</c>). Dropping it would make our decoder
    /// disagree with <c>SpecificDatumReader</c> on the same bytes.
    /// </remarks>
    string PrimitiveRead(AvroPrimitive primitive) => FromBcl(primitive.Logical, Read(primitive));

    string Read(AvroPrimitive primitive) => primitive.Logical switch {
        AvroLogicalKind.Uuid                 => "new global::System.Guid(decoder.ReadString())",
        AvroLogicalKind.Date                 => "global::System.DateOnly.FromDayNumber(UnixEpochDay + decoder.ReadInt())",
        AvroLogicalKind.TimeMillis           => "global::System.TimeOnly.FromTimeSpan(global::System.TimeSpan.FromMilliseconds(decoder.ReadInt()))",
        AvroLogicalKind.TimeMicros           => "global::System.TimeOnly.FromTimeSpan(global::System.TimeSpan.FromTicks(decoder.ReadLong() * TicksPerMicrosecond))",
        AvroLogicalKind.LocalTimestampMillis => "UnixEpoch.AddMilliseconds(decoder.ReadLong()).ToLocalTime()",
        AvroLogicalKind.LocalTimestampMicros => "UnixEpoch.AddTicks(decoder.ReadLong() * TicksPerMicrosecond).ToLocalTime()",
        AvroLogicalKind.Decimal              => $"ReadDecimal(decoder, {primitive.Scale})",

        // UnixEpoch is Kind=Utc, so the DateTimeOffset lands at offset zero. The wire carries no offset,
        // so a value encoded at +05:00 decodes to the same instant expressed at zero.
        AvroLogicalKind.TimestampMillis =>
            "new global::System.DateTimeOffset(UnixEpoch.AddMilliseconds(decoder.ReadLong()))",

        AvroLogicalKind.TimestampMicros =>
            "new global::System.DateTimeOffset(UnixEpoch.AddTicks(decoder.ReadLong() * TicksPerMicrosecond))",

        AvroLogicalKind.TimestampNanos when options.EmitsRichNanoseconds && options.UsesNodaTime =>
            "FromUnixNanoseconds(decoder.ReadLong())",

        AvroLogicalKind.TimestampNanos when options.EmitsRichNanoseconds =>
            "new global::Kurrent.Surge.Codecs.Avro.NanoInstant(decoder.ReadLong())",

        AvroLogicalKind.TimestampNanos => "decoder.ReadLong()",

        _ => primitive.Kind switch {
            AvroPrimitiveKind.Null    => "default(object?)",
            AvroPrimitiveKind.Boolean => "decoder.ReadBoolean()",
            AvroPrimitiveKind.Int     => "decoder.ReadInt()",
            AvroPrimitiveKind.Long    => "decoder.ReadLong()",
            AvroPrimitiveKind.Float   => "decoder.ReadFloat()",
            AvroPrimitiveKind.Double  => "decoder.ReadDouble()",
            AvroPrimitiveKind.Bytes   => "decoder.ReadBytes()",
            _                         => "decoder.ReadString()"
        }
    };

    #endregion // Decoding

    #region ->> Naming <<-

    string CSharpType(AvroSchema schema, FieldSite site) => _types.TypeOf(schema, site);

    bool IsValueType(AvroSchema schema) => _types.IsValueType(schema);

    static string TypeName(IAvroNamed named) => AvroNaming.QualifiedTypeName(named);

    static string EncodeMethodName(AvroRecord record) => $"Encode_{AvroNaming.HintName(record.FullName)}";
    static string DecodeMethodName(AvroRecord record) => $"Decode_{AvroNaming.HintName(record.FullName)}";

    public static string CodecClassName(AvroRecord record) => $"{AvroNaming.HintName(record.FullName)}AvroCodec";

    #endregion // Naming
}
