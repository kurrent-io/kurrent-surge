using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Kurrent.Surge.Codecs.Avro.Generators.Json;
using Kurrent.Surge.Codecs.Avro.Generators.Model;

namespace Kurrent.Surge.Codecs.Avro.Generators.Emission;

/// <summary>
/// Emits the C# message types a schema declares: records, enums, fixed types, and the interface an
/// all-record union needs so its field has a single CLR type.
/// </summary>
/// <remarks>
/// A record implements <c>ISpecificRecord</c> explicitly, so <c>Schema</c>, <c>Get</c> and <c>Put</c>
/// stay off the public surface while Apache's own readers and writers keep working.
/// <para>
/// The schema text behind <c>ISpecificRecord.Schema</c> is parsed on first read and never at type
/// initialisation. <c>s_schema</c> deliberately has no field initialiser: one would give the type a
/// static constructor, which roots <c>Avro.Schema.Parse</c>, which roots Newtonsoft.Json into every
/// binary whether or not anything ever reads the schema.
/// </para>
/// <para>
/// Naming and emission shape are derived from AvroSourceGenerator (MIT, Copyright (c) 2024 sadicangel).
/// </para>
/// </remarks>
sealed class AvroTypeEmitter(AvroSchemaDocument document) {
    readonly AvroTypeMap                      _types    = new(document);
    readonly List<FieldSite>                  _variants = [];
    readonly Dictionary<string, List<string>> _implements = [];

    public IReadOnlyList<TypeEmission> Emit() {
        CollectVariants();

        var emissions = new List<TypeEmission>();

        foreach (var named in document.NamedTypes.Values) {
            switch (named) {
                case AvroRecord record: emissions.Add(EmitRecord(record)); break;
                case AvroEnum @enum:    emissions.Add(EmitEnum(@enum));    break;

                // A decimal-annotated fixed surfaces as AvroDecimal, so nothing would reference the class.
                case AvroFixed { Logical: AvroLogicalKind.None } @fixed: emissions.Add(EmitFixed(@fixed)); break;
            }
        }

        emissions.AddRange(_variants.Select(EmitVariantInterface));

        return emissions;
    }

    #region ->> Variant discovery <<-

    void CollectVariants() {
        foreach (var record in document.NamedTypes.Values.OfType<AvroRecord>())
        foreach (var field in record.Fields)
            Walk(field.Type, new FieldSite(record, field.Name));

        void Walk(AvroSchema schema, FieldSite site) {
            switch (document.Resolve(schema)) {
                case AvroArray array: Walk(array.Items, site); break;
                case AvroMap map:     Walk(map.Values, site);  break;

                case AvroUnion union: {
                    if (union.IsVariant(document.Resolve)) Register(union, site);

                    foreach (var branch in union.Branches) Walk(branch, site);

                    break;
                }
            }
        }

        void Register(AvroUnion union, FieldSite site) {
            _variants.Add(site);

            var name = AvroNaming.QualifiedVariantInterfaceName(site.Owner, site.Field);

            foreach (var branch in union.Branches.Select(document.Resolve).OfType<AvroRecord>()) {
                if (!_implements.TryGetValue(branch.FullName, out var interfaces))
                    _implements[branch.FullName] = interfaces = [];

                if (!interfaces.Contains(name)) interfaces.Add(name);
            }
        }
    }

    #endregion // Variant discovery

    #region ->> Records <<-

    TypeEmission EmitRecord(AvroRecord record) {
        var writer = Header(record);
        var name   = AvroNaming.TypeName(record);

        var bases = new List<string> { "global::Avro.Specific.ISpecificRecord" };

        if (_implements.TryGetValue(record.FullName, out var interfaces)) bases.AddRange(interfaces);

        Documentation(writer, record.Doc);
        writer.Line(Templates.GeneratedCodeAttribute);

        using (writer.Block($"public sealed partial record {name} : {string.Join(", ", bases)}")) {
            foreach (var field in record.Fields) EmitProperty(writer, record, field);

            writer.Line();
            EmitEquality(writer, record);
            writer.Line();
            EmitSchema(writer, record);
            writer.Line();
            EmitGet(writer, record);
            writer.Line();
            EmitPut(writer, record);
        }

        return new TypeEmission(record.FullName, writer.ToString());
    }

    void EmitProperty(CodeWriter writer, AvroRecord record, AvroField field) {
        var site    = new FieldSite(record, field.Name);
        var type    = _types.TypeOf(field.Type, site);
        var initial = field.Default is null ? null : Literal(field.Default, field.Type, site);

        Documentation(writer, field.Doc);

        // Avro requires a value for every field that declares no default, so the C# type says the same.
        // A nullable field is exempt: null is already a value it can hold.
        var required = initial is null && !_types.IsNullable(field.Type) ? "required " : "";
        var suffix   = initial is null ? "" : $" = {initial};";

        writer.Line($"public {required}{type} {AvroNaming.Identifier(field.Name)} {{ get; set; }}{suffix}");
    }

    /// <summary>Value equality that means what it says.</summary>
    /// <remarks>
    /// Declaring both members suppresses the pair a record would synthesize, which compares a
    /// <c>byte[]</c>, a list or a dictionary by reference and so reports two identical messages as
    /// different. The compiler still routes <c>==</c> through this.
    /// <para>
    /// Collections take part in the hash, so a record whose collection is mutated after it goes into a
    /// dictionary will not be found again. That hazard already existed — every property has a setter, so
    /// changing any field moves the hash.
    /// </para>
    /// </remarks>
    void EmitEquality(CodeWriter writer, AvroRecord record) {
        var name     = AvroNaming.TypeName(record);
        var equality = new AvroEqualityEmitter(document, _types);

        using (writer.Block($"public bool Equals({name}? other)")) {
            writer.Line("if (ReferenceEquals(this, other)) return true;");
            writer.Line("if (other is null) return false;");

            if (record.Fields.Count == 0) {
                writer.Line();
                writer.Line("return true;");
            }
            else {
                writer.Line();

                for (var index = 0; index < record.Fields.Count; index++) {
                    var field = record.Fields[index];
                    var site  = new FieldSite(record, field.Name);
                    var self  = AvroNaming.Identifier(field.Name);

                    var lead = index == 0 ? "return " : "    && ";
                    var tail = index == record.Fields.Count - 1 ? ";" : "";

                    writer.Line($"{lead}{equality.Comparison(field.Type, self, $"other.{self}", site)}{tail}");
                }
            }
        }

        writer.Line();

        using (writer.Block("public override int GetHashCode()")) {
            writer.Line("var hash = new global::System.HashCode();");
            writer.Line();

            foreach (var field in record.Fields) {
                var site = new FieldSite(record, field.Name);
                var self = AvroNaming.Identifier(field.Name);

                writer.Line($"hash.Add({equality.Hash(field.Type, self, site)});");
            }

            writer.Line();
            writer.Line("return hash.ToHashCode();");
        }
    }

    /// <remarks>
    /// The double-checked-free lazy read can parse twice under a race. Both results are equal and
    /// immutable, so the cost is one wasted parse rather than a correctness problem, and avoiding it
    /// would need a lock on a path that almost never runs.
    /// </remarks>
    void EmitSchema(CodeWriter writer, IAvroNamed named) {
        writer.Line($"const string SchemaJson = {AvroSchemaText.ToLiteral(AvroSchemaText.For(document, named))};");
        writer.Line();
        writer.Line("static global::Avro.Schema? s_schema;");
        writer.Line();

        var accessor = named is AvroFixed
            ? "public override global::Avro.Schema Schema"
            : "global::Avro.Schema global::Avro.Specific.ISpecificRecord.Schema";

        writer.Line($"{accessor} => s_schema ??= global::Avro.Schema.Parse(SchemaJson);");
    }

    void EmitGet(CodeWriter writer, AvroRecord record) {
        using (writer.Block("object? global::Avro.Specific.ISpecificRecord.Get(int fieldPos) => fieldPos switch", "};")) {
            for (var index = 0; index < record.Fields.Count; index++)
                writer.Line($"{index} => {InteropGet(record.Fields[index])},");

            writer.Line("_ => throw new global::Avro.AvroRuntimeException($\"Bad index {fieldPos} in Get()\")");
        }
    }

    /// <summary>The value handed to Apache across <c>ISpecificRecord</c>.</summary>
    /// <remarks>
    /// Apache does not model <c>timestamp-nanos</c>. It resolves the field to <c>UnknownLogicalType</c>
    /// over a <c>long</c> and rejects anything that is not an <c>Int64</c>, so the richer property type
    /// converts here rather than leaking across the interface. Everything else crosses unchanged.
    /// </remarks>
    string InteropGet(AvroField field) {
        var name = AvroNaming.Identifier(field.Name);

        return NanosecondBranch(field.Type) switch {
            NanosecondShape.Direct   => $"{name}.TotalNanoseconds",
            NanosecondShape.Nullable => $"{name}?.TotalNanoseconds",
            _                        => name
        };
    }

    string InteropPut(AvroField field, FieldSite site) {
        const string Nano = "global::Kurrent.Surge.Codecs.Avro.NanoInstant";

        switch (NanosecondBranch(field.Type)) {
            case NanosecondShape.Direct:   return $"new {Nano}((long)fieldValue!)";
            case NanosecondShape.Nullable: return $"fieldValue is null ? null : new {Nano}((long)fieldValue)";
        }

        // A boxed value cannot be unboxed straight to Nullable<T>, so a nullable field tests for null
        // first and casts to the branch type.
        return _types.IsNullable(field.Type)
            ? $"fieldValue is null ? null : ({_types.UnderlyingTypeOf(field.Type, site)})fieldValue"
            : $"({_types.TypeOf(field.Type, site)})fieldValue!";
    }

    enum NanosecondShape { None, Direct, Nullable }

    NanosecondShape NanosecondBranch(AvroSchema schema) {
        if (document.Resolve(schema) is AvroPrimitive { Logical: AvroLogicalKind.TimestampNanos })
            return NanosecondShape.Direct;

        return document.Resolve(schema) is AvroUnion { IsNullable: true } union
            && document.Resolve(union.NullableBranch!) is AvroPrimitive { Logical: AvroLogicalKind.TimestampNanos }
                ? NanosecondShape.Nullable
                : NanosecondShape.None;
    }

    void EmitPut(CodeWriter writer, AvroRecord record) {
        using (writer.Block("void global::Avro.Specific.ISpecificRecord.Put(int fieldPos, object? fieldValue)"))
        using (writer.Block("switch (fieldPos)")) {
            for (var index = 0; index < record.Fields.Count; index++) {
                var field = record.Fields[index];
                var name  = AvroNaming.Identifier(field.Name);

                writer.Line($"case {index}: {name} = {InteropPut(field, new FieldSite(record, field.Name))}; break;");
            }

            writer.Line("default: throw new global::Avro.AvroRuntimeException($\"Bad index {fieldPos} in Put()\");");
        }
    }

    #endregion // Records

    #region ->> Enums, fixed and variants <<-

    TypeEmission EmitEnum(AvroEnum @enum) {
        var writer = Header(@enum);

        Documentation(writer, @enum.Doc);
        writer.Line(Templates.GeneratedCodeAttribute);

        using (writer.Block($"public enum {AvroNaming.TypeName(@enum)}")) {
            foreach (var symbol in @enum.Symbols) writer.Line($"{AvroNaming.Identifier(symbol)},");
        }

        return new TypeEmission(@enum.FullName, writer.ToString());
    }

    TypeEmission EmitFixed(AvroFixed @fixed) {
        var writer = Header(@fixed);
        var name   = AvroNaming.TypeName(@fixed);

        Documentation(writer, @fixed.Doc);
        writer.Line(Templates.GeneratedCodeAttribute);

        using (writer.Block($"public sealed partial class {name} : global::Avro.Specific.SpecificFixed")) {
            writer.Line($"public {name}() : base({@fixed.Size}) {{ }}");
            writer.Line();
            writer.Line($"public int Size => {@fixed.Size};");
            writer.Line();
            EmitSchema(writer, @fixed);
        }

        return new TypeEmission(@fixed.FullName, writer.ToString());
    }

    TypeEmission EmitVariantInterface(FieldSite site) {
        var writer = new CodeWriter();
        var name   = AvroNaming.VariantInterfaceName(site.Owner, site.Field);

        writer.Lines(Templates.TypeFileHeader);
        writer.Line();

        var @namespace = AvroNaming.Namespace(site.Owner.Namespace);

        if (@namespace.Length > 0) {
            writer.Line($"namespace {@namespace};");
            writer.Line();
        }

        writer.Line("/// <summary>");
        writer.Line($"/// One branch of the union declared by '{site.Field}' on '{site.Owner.FullName}'.");
        writer.Line("/// </summary>");
        writer.Line(Templates.GeneratedCodeAttribute);
        writer.Line($"public partial interface {name};");

        var fullName = site.Owner.Namespace.Length == 0 ? name : $"{site.Owner.Namespace}.{name}";

        return new TypeEmission(fullName, writer.ToString());
    }

    #endregion // Enums, fixed and variants

    #region ->> Shared emission <<-

    CodeWriter Header(IAvroNamed named) {
        var writer = new CodeWriter();

        writer.Lines(Templates.TypeFileHeader);
        writer.Line();

        var @namespace = AvroNaming.Namespace(named.Namespace);

        if (@namespace.Length > 0) {
            writer.Line($"namespace {@namespace};");
            writer.Line();
        }

        return writer;
    }

    static void Documentation(CodeWriter writer, string? doc) {
        if (string.IsNullOrWhiteSpace(doc)) return;

        writer.Line("/// <summary>");

        foreach (var line in doc!.Split('\n')) writer.Line($"/// {Escape(line.Trim('\r', ' '))}");

        writer.Line("/// </summary>");

        static string Escape(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    #endregion // Shared emission

    #region ->> Default values <<-

    /// <summary>Renders an Avro default as the C# expression that initialises the property.</summary>
    /// <remarks>
    /// A union's default has to match one element of the union, not its first — see the Avro
    /// specification, "Unions". Reading it against the first branch turns a legal
    /// <c>["null","int"]</c> default of <c>0</c> into <c>null</c>. Anything the renderer cannot express
    /// returns null, and the property is emitted without an initialiser rather than with a wrong one.
    /// </remarks>
    string? Literal(JsonValue value, AvroSchema schema, FieldSite site) {
        var resolved = document.Resolve(schema);

        if (resolved is AvroUnion union) {
            var branch = union.Branches.FirstOrDefault(candidate => Accepts(document.Resolve(candidate), value));

            return branch is null ? null : Literal(value, branch, site);
        }

        return resolved switch {
            AvroPrimitive primitive => PrimitiveLiteral(value, primitive),
            AvroEnum @enum          => EnumLiteral(value, @enum),
            AvroFixed @fixed        => FixedLiteral(value, @fixed),
            AvroArray array         => ArrayLiteral(value, array, site),
            AvroMap map             => MapLiteral(value, map, site),
            AvroRecord record       => RecordLiteral(value, record, site),
            _                       => null
        };
    }

    /// <summary>Whether a branch of a union is the one the default value was written against.</summary>
    /// <remarks>
    /// Matched on the JSON shape, which is all the specification's default-encoding table gives us.
    /// Where two branches accept the same shape the earlier one wins, since the specification allows
    /// any matching element and offers no tie-break.
    /// </remarks>
    static bool Accepts(AvroSchema schema, JsonValue value) => schema switch {
        AvroPrimitive { Kind: AvroPrimitiveKind.Null }    => value.Kind is JsonKind.Null,
        AvroPrimitive { Kind: AvroPrimitiveKind.Boolean } => value.Kind is JsonKind.Boolean,

        AvroPrimitive { Kind: AvroPrimitiveKind.Int or AvroPrimitiveKind.Long
                            or AvroPrimitiveKind.Float or AvroPrimitiveKind.Double } => value.Kind is JsonKind.Number,

        AvroPrimitive { Kind: AvroPrimitiveKind.Bytes or AvroPrimitiveKind.String } => value.Kind is JsonKind.String,

        AvroEnum @enum => value.Kind is JsonKind.String && @enum.Symbols.Contains(value.Text),
        AvroFixed      => value.Kind is JsonKind.String,
        AvroRecord     => value.Kind is JsonKind.Object,
        AvroArray      => value.Kind is JsonKind.Array,
        AvroMap        => value.Kind is JsonKind.Object,

        // A union cannot immediately contain another union, so a nested one is malformed either way.
        _ => false
    };

    static string? PrimitiveLiteral(JsonValue value, AvroPrimitive primitive) {
        // A logical type's CLR representation is a struct with no literal form, so the schema default
        // cannot be expressed as an initialiser.
        if (primitive.Logical is not AvroLogicalKind.None) return null;

        return primitive.Kind switch {
            AvroPrimitiveKind.Null    => "null",
            AvroPrimitiveKind.Boolean => value.Boolean ? "true" : "false",
            AvroPrimitiveKind.Int     => ((int)value.Number).ToString(CultureInfo.InvariantCulture),
            AvroPrimitiveKind.Long    => $"{(long)value.Number}L",
            AvroPrimitiveKind.Float   => $"{value.Number.ToString("R", CultureInfo.InvariantCulture)}f",
            AvroPrimitiveKind.Double  => $"{value.Number.ToString("R", CultureInfo.InvariantCulture)}d",
            AvroPrimitiveKind.Bytes   => value.Kind is JsonKind.String ? Bytes(value.Text) : null,
            AvroPrimitiveKind.String  => value.Kind is JsonKind.String ? Quote(value.Text) : null,
            _                         => null
        };
    }

    /// <remarks>
    /// A fixed maps to a <c>SpecificFixed</c> subclass, not to <c>byte[]</c>, so the default has to
    /// construct one. A default whose length disagrees with the declared size is dropped: the
    /// <c>Value</c> setter would throw at construction.
    /// </remarks>
    static string? FixedLiteral(JsonValue value, AvroFixed @fixed) =>
        @fixed.Logical is AvroLogicalKind.None
     && value.Kind is JsonKind.String
     && value.Text.Length == @fixed.Size
     && Bytes(value.Text) is { } bytes
            ? $"new {AvroNaming.QualifiedTypeName(@fixed)} {{ Value = {bytes} }}"
            : null;

    static string? EnumLiteral(JsonValue value, AvroEnum @enum) =>
        value.Kind is JsonKind.String && @enum.Symbols.Contains(value.Text)
            ? $"{AvroNaming.QualifiedTypeName(@enum)}.{AvroNaming.Identifier(value.Text)}"
            : null;

    string? ArrayLiteral(JsonValue value, AvroArray array, FieldSite site) {
        if (value.Kind is not JsonKind.Array) return null;

        var items = value.Items.Select(item => Literal(item, array.Items, site)).ToList();

        return items.Any(item => item is null)
            ? null
            : $"new {_types.ConcreteTypeOf(array, site)} {{ {string.Join(", ", items)} }}";
    }

    string? MapLiteral(JsonValue value, AvroMap map, FieldSite site) {
        if (value.Kind is not JsonKind.Object) return null;

        var entries = value.Members
            .Select(member => (member.Key, Value: Literal(member.Value, map.Values, site)))
            .ToList();

        return entries.Any(entry => entry.Value is null)
            ? null
            : $"new {_types.ConcreteTypeOf(map, site)} {{ {string.Join(", ", entries.Select(entry => $"[{Quote(entry.Key)}] = {entry.Value}"))} }}";
    }

    string? RecordLiteral(JsonValue value, AvroRecord record, FieldSite site) {
        if (value.Kind is not JsonKind.Object) return null;

        var assignments = new List<string>();

        foreach (var field in record.Fields) {
            if (value[field.Name] is not { } member) return null;

            var literal = Literal(member, field.Type, new FieldSite(record, field.Name));

            if (literal is null) return null;

            assignments.Add($"{AvroNaming.Identifier(field.Name)} = {literal}");
        }

        return $"new {AvroNaming.QualifiedTypeName(record)} {{ {string.Join(", ", assignments)} }}";
    }

    // An Avro 'bytes' or 'fixed' default is a JSON string whose characters are code points 0-255, one
    // byte each — "ÿ" is the single byte 0xFF. See the Avro specification, "Schema Record" —
    // default values. A code point above 255 is not representable, so the default is dropped rather
    // than silently truncated to its low byte.
    static string? Bytes(string text) =>
        text.All(character => character <= 0xFF)
            ? $"new byte[] {{ {string.Join(", ", text.Select(character => $"0x{(byte)character:X2}"))} }}"
            : null;

    static string Quote(string text) {
        var builder = new StringBuilder(text.Length + 2).Append('"');

        foreach (var character in text) {
            switch (character) {
                case '"':  builder.Append("\\\""); break;
                case '\\': builder.Append(@"\\");  break;
                case '\n': builder.Append("\\n");  break;
                case '\r': builder.Append("\\r");  break;
                case '\t': builder.Append("\\t");  break;

                default:
                    if (character < ' ') builder.Append($"\\u{(int)character:X4}");
                    else builder.Append(character);

                    break;
            }
        }

        return builder.Append('"').ToString();
    }

    #endregion // Default values
}
