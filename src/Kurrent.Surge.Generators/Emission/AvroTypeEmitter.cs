using System.Globalization;
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
/// </remarks>
sealed class AvroTypeEmitter(AvroSchemaDocument document, AvroGeneratorOptions options) {
    readonly AvroTypeMap                      _types      = new(document, options);
    readonly List<FieldSite>                  _variants   = [];
    readonly Dictionary<string, List<string>> _implements = [];

    public IReadOnlyList<TypeEmission> Emit() {
        CollectVariants();

        var emissions = new List<TypeEmission>();

        foreach (var named in document.NamedTypes.Values) {
            switch (named) {
                case AvroRecord record: emissions.Add(EmitRecord(record)); break;

                case AvroEnum @enum: emissions.Add(EmitEnum(@enum)); break;

                case AvroFixed @fixed when NeedsFixedClass(@fixed): emissions.Add(EmitFixed(@fixed)); break;
            }
        }

        emissions.AddRange(_variants.Select(EmitVariantInterface));

        return emissions;
    }

    /// <summary>Whether a field of this fixed is typed as the fixed class itself.</summary>
    /// <remarks>
    /// A <c>decimal</c> or <c>uuid</c> annotation replaces the class with its logical type, so nothing
    /// would reference it. A <c>duration</c> replaces it only under a time model that can carry one —
    /// otherwise the annotation is erased and the class is still the field's type.
    /// </remarks>
    bool NeedsFixedClass(AvroFixed @fixed) =>
        @fixed.Logical switch {
            AvroLogicalKind.None     => true,
            AvroLogicalKind.Duration => !options.UsesNodaTime,
            _                        => false
        };

    #region ->> Variant discovery <<-

    void CollectVariants() {
        foreach (var record in document.NamedTypes.Values.OfType<AvroRecord>())
        foreach (var field in record.Fields)
            Walk(field.Type, new FieldSite(record, field.Name));

        void Walk(AvroSchema schema, FieldSite site) {
            switch (document.Resolve(schema)) {
                case AvroArray array: Walk(array.Items, site); break;

                case AvroMap map: Walk(map.Values, site); break;

                case AvroUnion union: {
                    if (union.IsVariant(document.Resolve))
                        Register(union, site);

                    foreach (var branch in union.Branches)
                        Walk(branch, site);

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

                if (!interfaces.Contains(name))
                    interfaces.Add(name);
            }
        }
    }

    #endregion // Variant discovery

    #region ->> Records <<-

    TypeEmission EmitRecord(AvroRecord record) {
        var writer = Header(record);
        var name   = AvroNaming.TypeName(record);

        var bases = new List<string>();

        if (options.EmitsApacheInterop)
            bases.Add("global::Avro.Specific.ISpecificRecord");

        if (_implements.TryGetValue(record.FullName, out var interfaces))
            bases.AddRange(interfaces);

        Documentation(writer, record.Doc);
        writer.Line(Templates.GeneratedCodeAttribute);

        var declaration = bases.Count == 0
            ? $"{options.AccessModifierKeyword} sealed partial record {name}"
            : $"{options.AccessModifierKeyword} sealed partial record {name} : {string.Join(", ", bases)}";

        using (writer.Block(declaration)) {
            foreach (var field in record.Fields)
                EmitProperty(writer, record, field);

            writer.Line();
            EmitEquality(writer, record);

            // Schema, Get and Put exist only for Apache's readers and writers. Without them the type
            // carries no schema text, so nothing can root Avro.Schema.Parse and pull in Newtonsoft.
            if (options.EmitsApacheInterop) {
                writer.Line();
                EmitSchema(writer, record);
                writer.Line();
                EmitGet(writer, record);
                writer.Line();
                EmitPut(writer, record);
            }
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
            } else {
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
    /// Several logical types surface as a narrower CLR type than the one Apache works in — <c>DateOnly</c>
    /// for <c>date</c>, <c>TimeOnly</c> for <c>time-*</c>, <c>DateTimeOffset</c> for an absolute
    /// <c>timestamp</c>, and <c>NanoInstant</c> for <c>timestamp-nanos</c> when that is switched on.
    /// Apache resolves each to <c>DateTime</c>, <c>TimeSpan</c> or <c>long</c> and rejects anything else,
    /// so the conversion happens here and the richer type never crosses the interface. Everything else
    /// crosses unchanged.
    /// </remarks>
    string InteropGet(AvroField field) {
        var name = AvroNaming.Identifier(field.Name);
        var (shape, logical) = InteropOf(field.Type);

        if (shape is InteropShape.None)
            return name;

        var access = shape is InteropShape.Nullable ? $"{name}?" : name;

        if (options.UsesNodaTime) {
            // Instant to nanoseconds is a subtraction, so it cannot ride the null-conditional the other
            // conversions use; a nullable field spells the null check out.
            if (logical is AvroLogicalKind.TimestampNanos) {
                const string Epoch = "global::NodaTime.NodaConstants.UnixEpoch";

                return shape is InteropShape.Nullable
                    ? $"{name} is null ? (object?)null : ({name}.Value - {Epoch}).ToInt64Nanoseconds()"
                    : $"({name} - {Epoch}).ToInt64Nanoseconds()";
            }

            return logical switch {
                AvroLogicalKind.Date                                               => $"{access}.ToDateOnly().ToDateTime(global::System.TimeOnly.MinValue)",
                AvroLogicalKind.TimeMillis or AvroLogicalKind.TimeMicros           => $"{access}.ToTimeOnly().ToTimeSpan()",
                AvroLogicalKind.TimestampMillis or AvroLogicalKind.TimestampMicros => $"{access}.ToDateTimeUtc()",
                _                                                                  => $"{access}.ToDateTimeUnspecified()"
            };
        }

        return logical switch {
            AvroLogicalKind.Date                                               => $"{access}.ToDateTime(global::System.TimeOnly.MinValue)",
            AvroLogicalKind.TimeMillis or AvroLogicalKind.TimeMicros           => $"{access}.ToTimeSpan()",
            AvroLogicalKind.TimestampMillis or AvroLogicalKind.TimestampMicros => $"{access}.UtcDateTime",
            _                                                                  => $"{access}.TotalNanoseconds"
        };
    }

    string InteropPut(AvroField field, FieldSite site) {
        var (shape, logical) = InteropOf(field.Type);

        // A boxed value cannot be unboxed straight to Nullable<T>, so a nullable field tests for null
        // first and casts to the branch type.
        if (shape is InteropShape.None)
            return _types.IsNullable(field.Type)
                ? $"fieldValue is null ? null : ({_types.UnderlyingTypeOf(field.Type, site)})fieldValue"
                : $"({_types.TypeOf(field.Type, site)})fieldValue!";

        var suffix = shape is InteropShape.Nullable ? "" : "!";

        var convert = options.UsesNodaTime
            ? logical switch {
                AvroLogicalKind.Date =>
                    $"global::NodaTime.LocalDate.FromDateOnly(global::System.DateOnly.FromDateTime((global::System.DateTime)fieldValue{suffix}))",
                AvroLogicalKind.TimeMillis or AvroLogicalKind.TimeMicros =>
                    $"global::NodaTime.LocalTime.FromTimeOnly(global::System.TimeOnly.FromTimeSpan((global::System.TimeSpan)fieldValue{suffix}))",
                AvroLogicalKind.TimestampMillis or AvroLogicalKind.TimestampMicros =>
                    $"global::NodaTime.Instant.FromDateTimeUtc((global::System.DateTime)fieldValue{suffix})",
                AvroLogicalKind.LocalTimestampMillis or AvroLogicalKind.LocalTimestampMicros =>
                    $"global::NodaTime.LocalDateTime.FromDateTime((global::System.DateTime)fieldValue{suffix})",
                _ =>
                    $"global::NodaTime.NodaConstants.UnixEpoch + global::NodaTime.Duration.FromNanoseconds((long)fieldValue{suffix})"
            }
            : logical switch {
                AvroLogicalKind.Date =>
                    $"global::System.DateOnly.FromDateTime((global::System.DateTime)fieldValue{suffix})",
                AvroLogicalKind.TimeMillis or AvroLogicalKind.TimeMicros =>
                    $"global::System.TimeOnly.FromTimeSpan((global::System.TimeSpan)fieldValue{suffix})",
                AvroLogicalKind.TimestampMillis or AvroLogicalKind.TimestampMicros =>
                    $"new global::System.DateTimeOffset((global::System.DateTime)fieldValue{suffix})",
                _ =>
                    $"new global::Kurrent.Surge.Codecs.Avro.NanoInstant((long)fieldValue{suffix})"
            };

        return shape is InteropShape.Nullable ? $"fieldValue is null ? null : {convert}" : convert;
    }

    enum InteropShape {
        None,
        Direct,
        Nullable
    }

    (InteropShape Shape, AvroLogicalKind Logical) InteropOf(AvroSchema schema) {
        var resolved = document.Resolve(schema);

        if (resolved is AvroPrimitive { Logical: var direct } && NeedsInterop(direct))
            return (InteropShape.Direct, direct);

        if (resolved is AvroUnion { IsNullable                                : true } union
         && document.Resolve(union.NullableBranch!) is AvroPrimitive { Logical: var branch }
         && NeedsInterop(branch))
            return (InteropShape.Nullable, branch);

        return (InteropShape.None, AvroLogicalKind.None);
    }

    bool NeedsInterop(AvroLogicalKind kind) =>
        kind switch {
            AvroLogicalKind.Date
             or AvroLogicalKind.TimeMillis or AvroLogicalKind.TimeMicros
             or AvroLogicalKind.TimestampMillis or AvroLogicalKind.TimestampMicros => true,

            // DateTime is already what Apache expects; LocalDateTime is not.
            AvroLogicalKind.LocalTimestampMillis or AvroLogicalKind.LocalTimestampMicros => options.UsesNodaTime,

            // Erased to long, so the property already is what Apache expects.
            AvroLogicalKind.TimestampNanos => options.EmitsRichNanoseconds,

            _ => false
        };

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

        using (writer.Block($"{options.AccessModifierKeyword} enum {AvroNaming.TypeName(@enum)}")) {
            foreach (var symbol in @enum.Symbols)
                writer.Line($"{AvroNaming.Identifier(symbol)},");
        }

        return new TypeEmission(@enum.FullName, writer.ToString());
    }

    TypeEmission EmitFixed(AvroFixed @fixed) {
        var writer = Header(@fixed);
        var name   = AvroNaming.TypeName(@fixed);

        Documentation(writer, @fixed.Doc);
        writer.Line(Templates.GeneratedCodeAttribute);

        var @base = options.EmitsApacheInterop
            ? "global::Avro.Specific.SpecificFixed"
            : "global::Kurrent.Surge.Codecs.Avro.AvroFixedValue";

        using (writer.Block($"{options.AccessModifierKeyword} sealed partial class {name} : {@base}")) {
            writer.Line($"public {name}() : base({@fixed.Size}) {{ }}");

            if (options.EmitsApacheInterop) {
                writer.Line();
                writer.Line($"public int Size => {@fixed.Size};");
                writer.Line();
                EmitSchema(writer, @fixed);
            }
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

        if (options.EmitsXmlDocs) {
            writer.Line("/// <summary>");
            writer.Line($"/// One branch of the union declared by '{site.Field}' on '{site.Owner.FullName}'.");
            writer.Line("/// </summary>");
        }

        writer.Line(Templates.GeneratedCodeAttribute);
        writer.Line($"{options.AccessModifierKeyword} partial interface {name};");

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

    void Documentation(CodeWriter writer, string? doc) {
        if (!options.EmitsXmlDocs || string.IsNullOrWhiteSpace(doc))
            return;

        writer.Line("/// <summary>");

        foreach (var line in doc!.Split('\n'))
            writer.Line($"/// {Escape(line.Trim('\r', ' '))}");

        writer.Line("/// </summary>");

        static string Escape(string text) =>
            text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
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
    static bool Accepts(AvroSchema schema, JsonValue value) =>
        schema switch {
            AvroPrimitive { Kind: AvroPrimitiveKind.Null }    => value.Kind is JsonKind.Null,
            AvroPrimitive { Kind: AvroPrimitiveKind.Boolean } => value.Kind is JsonKind.Boolean,

            AvroPrimitive {
                Kind: AvroPrimitiveKind.Int or AvroPrimitiveKind.Long
                                            or AvroPrimitiveKind.Float or AvroPrimitiveKind.Double
            } => value.Kind is JsonKind.Number,

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
        if (primitive.Logical is not AvroLogicalKind.None)
            return null;

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
        if (value.Kind is not JsonKind.Array)
            return null;

        var items = value.Items.Select(item => Literal(item, array.Items, site)).ToList();

        return items.Any(item => item is null)
            ? null
            : $"new {_types.ConcreteTypeOf(array, site)} {{ {string.Join(", ", items)} }}";
    }

    string? MapLiteral(JsonValue value, AvroMap map, FieldSite site) {
        if (value.Kind is not JsonKind.Object)
            return null;

        var entries = value
            .Members
            .Select(member => (member.Key, Value: Literal(member.Value, map.Values, site)))
            .ToList();

        return entries.Any(entry => entry.Value is null)
            ? null
            : $"new {_types.ConcreteTypeOf(map, site)} {{ {string.Join(", ", entries.Select(entry => $"[{Quote(entry.Key)}] = {entry.Value}"))} }}";
    }

    string? RecordLiteral(JsonValue value, AvroRecord record, FieldSite site) {
        if (value.Kind is not JsonKind.Object)
            return null;

        var assignments = new List<string>();

        foreach (var field in record.Fields) {
            if (value[field.Name] is not { } member)
                return null;

            var literal = Literal(member, field.Type, new FieldSite(record, field.Name));

            if (literal is null)
                return null;

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
                case '"': builder.Append("\\\""); break;

                case '\\': builder.Append(@"\\"); break;

                case '\n': builder.Append("\\n"); break;

                case '\r': builder.Append("\\r"); break;

                case '\t': builder.Append("\\t"); break;

                default:
                    if (character < ' ')
                        builder.Append($"\\u{(int)character:X4}");
                    else
                        builder.Append(character);

                break;
            }
        }

        return builder.Append('"').ToString();
    }

    #endregion // Default values
}