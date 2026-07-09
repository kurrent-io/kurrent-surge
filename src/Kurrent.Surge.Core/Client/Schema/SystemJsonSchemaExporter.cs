using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using Kurrent.Surge.Client.Schema.Serialization.Json;
using Kurrent.Surge.Client.Streams;
using Kurrent.Schema;
using NodaTime;

namespace Kurrent.Surge.Client.Schema;

/// <summary>
/// AOT-safe <see cref="ISchemaExporter"/> built on the in-box <see cref="JsonSchemaExporter"/>
/// (System.Text.Json). Generates a JSON Schema for a .NET type using the SDK's serializer
/// contract, so the schema matches the wire format the type serializes to.
/// </summary>
sealed class SystemJsonSchemaExporter : ISchemaExporter {
    /// <summary>
    /// The JSON Schema dialect JsonSchemaExporter targets — draft 2020-12. The runtime's schema
    /// model is written against this spec (see JsonSchema.cs in dotnet/runtime, which cites
    /// json-schema.org/draft/2020-12/json-schema-core section 4.3 normatively).
    /// </summary>
    internal const string Dialect = "https://json-schema.org/draft/2020-12/schema";

    public static ISchemaExporter Instance => new SystemJsonSchemaExporter();

    readonly JsonSerializerOptions _serializerOptions;
	readonly JsonSchemaExporterOptions _exporterOptions;
    readonly bool _canonical;
    readonly bool _declareDialect;

    /// <summary>
    /// AOT-safe <see cref="ISchemaExporter"/> built on the in-box <see cref="JsonSchemaExporter"/>
    /// (System.Text.Json). Generates a JSON Schema for a .NET type using the SDK's serializer
    /// contract, so the schema matches the wire format the type serializes to.
    /// </summary>
    public SystemJsonSchemaExporter(JsonSerializerOptions? serializerOptions = null, bool nullableIsOptional = true, bool canonical = false, bool declareDialect = true, bool enumAsOneOf = false) {
        _serializerOptions = BuildSchemaOptions(serializerOptions ?? JsonSchemaSerializerOptions.DefaultJsonSerializerOptions);
        _exporterOptions   = BuildExporterOptions(nullableIsOptional, enumAsOneOf);
        _canonical         = canonical;
        _declareDialect    = declareDialect;
        
        // The schema must describe the canonical serialized document, not the deserializer's leniency.
        // JsonNumberHandling.AllowReadingFromString (and AllowNamedFloatingPointLiterals) widen numeric
        // properties to {"type":["string","integer"], "pattern": ...}, which would tell the registry a
        // string is a valid value for a number. Those flags are read-side only, so we drop them and keep
        // just the write-shaping bit (WriteAsString) that genuinely changes the serialized form.
        static JsonSerializerOptions BuildSchemaOptions(JsonSerializerOptions source) {
            var numberHandling = source.NumberHandling & JsonNumberHandling.WriteAsString;
            return numberHandling == source.NumberHandling 
                ? source : new(source) { NumberHandling = numberHandling };
        }
        
        // Builds the exporter options. The transform runs for every node in the type graph: it replaces
        // wildcard leaves for custom-converter types, merges format hints and enum constraints, then
        // (optionally) relaxes `required`. The flag is captured here because field initializers can't
        // reference instance members / `this`.
        static JsonSchemaExporterOptions BuildExporterOptions(bool nullableIsOptional, bool enumAsOneOf) => new() {
            // Treat the root and null-oblivious reference types as non-nullable so the schema mirrors
            // the .NET contract instead of emitting `["object","null"]` / `["string","null"]` everywhere.
            TreatNullObliviousAsNonNullable = true,

            TransformSchemaNode = new SchemaNodeTransformer(nullableIsOptional, enumAsOneOf).Transform
        };
    }
    
	public string Export(Type messageType, SchemaDataFormat dataFormat) =>
		ExportAsJsonNode(messageType, dataFormat).ToJsonString();

	// Overrides the interface default: the schema is built as a node, so handing it out
	// directly spares callers the node -> string -> node round-trip the default implies.
	public JsonNode ExportAsJsonNode(Type messageType, SchemaDataFormat dataFormat) {
		if (dataFormat != SchemaDataFormat.Json)
			throw new NotSupportedException($"Unsupported schema data format. Expected {SchemaDataFormat.Json} but got {dataFormat}.");

		// GetTypeInfo resolves through the same TypeInfoResolverChain the serializer uses, so a
		// source-generated context (AOT-safe) is honoured when present and the reflection fallback
		// is used otherwise — identical resolution to the rest of the JSON schema pipeline.
		var schema = _serializerOptions
            .GetTypeInfo(messageType)
            .GetJsonSchemaAsNode(_exporterOptions);

        if (_canonical)
            schema = Canonicalize(schema)!;

        // Declare the dialect explicitly so the server (and humans) never have to guess which
        // spec the schema follows. JsonSchemaExporter targets 2020-12 but emits no declaration.
        // Applied after canonicalization (which strips `$schema` to enable cross-dialect
        // comparison) and only to object roots — a boolean root schema cannot carry keywords.
        if (_declareDialect && schema is JsonObject root)
            root.Insert(0, "$schema", Dialect);

		return schema;
	}

    /// <summary>
    /// Rewrites a schema into its canonical form: object keys sorted ordinally, the `required` and
    /// `type` arrays sorted (their order carries no semantic meaning), and the `$schema` dialect
    /// marker dropped. Two semantically identical schemas always canonicalize to the same string,
    /// making the output deterministic and directly comparable (e.g. against NJsonSchema output).
    /// </summary>
    // Nodes are MOVED into the rebuilt tree (detached via Remove/RemoveAt) instead of recreated:
    // JsonArray.Add<T>/JsonValue.Create<T> carry [RequiresUnreferencedCode]/[RequiresDynamicCode],
    // while moving existing nodes and the non-generic Add(JsonNode?) overload are AOT-safe.
    internal static JsonNode? Canonicalize(JsonNode? node) {
        switch (node) {
            case JsonObject obj: {
                var result = new JsonObject();
                var keys = obj.Select(property => property.Key)
                    .Where(key => key != "$schema")
                    .OrderBy(key => key, StringComparer.Ordinal)
                    .ToList();

                foreach (var key in keys) {
                    var child = obj[key];
                    obj.Remove(key); // detach so the node can be re-parented

                    result[key] = key is "required" or "type" && child is JsonArray array
                        ? SortStringArray(array)
                        : Canonicalize(child);
                }

                return result;
            }

            case JsonArray arr: {
                var result = new JsonArray();
                while (arr.Count > 0) {
                    var item = arr[0];
                    arr.RemoveAt(0); // detach so the node can be re-parented
                    result.Add(Canonicalize(item));
                }

                return result;
            }

            default:
                return node;
        }
    }

    static JsonArray SortStringArray(JsonArray array) {
        var items = new List<JsonNode?>(array.Count);
        while (array.Count > 0) {
            var item = array[0];
            array.RemoveAt(0); // detach so the node can be re-parented
            items.Add(item);
        }

        items.Sort(static (left, right) => string.CompareOrdinal((string?)left, (string?)right));

        var result = new JsonArray();
        foreach (var item in items)
            result.Add(item);

        return result;
    }
    
    class SchemaNodeTransformer(bool nullableIsOptional, bool enumAsOneOf) {
        // Types serialized by custom converters (NodaTime's, and the SDK's TimeSpanIso8601Converter)
        // cannot be introspected by JsonSchemaExporter, which emits the wildcard `true` (matches any
        // value) for them. NJsonSchema maps them to string + format from its built-in table in
        // ReflectionServiceBase.GetDescription (verified: identical with or without the converters
        // configured). We replicate that table so the schema is meaningful; the node is fully replaced.
        static readonly FrozenDictionary<Type, string> StringFormatTypes = new Dictionary<Type, string> {
            [typeof(Instant)]        = "date-time",
            [typeof(LocalDateTime)]  = "date-time",
            [typeof(OffsetDateTime)] = "date-time",
            [typeof(ZonedDateTime)]  = "date-time",
            [typeof(LocalDate)]      = "date",
            [typeof(LocalTime)]      = "time",
            [typeof(Duration)]       = "duration",
            [typeof(TimeSpan)]       = "duration"
        }.ToFrozenDictionary();

        // OpenAPI-style format hints NJsonSchema adds to numeric and binary types. JsonSchemaExporter
        // already emits the correct `type` for these, so the hint is merged into the existing node.
        // Validators ignore unknown formats per the JSON Schema spec — these exist purely for parity.
        static readonly FrozenDictionary<Type, string> FormatHints = new Dictionary<Type, string> {
            [typeof(int)]     = "int32",
            [typeof(long)]    = "int64",
            [typeof(ulong)]   = "ulong",
            [typeof(float)]   = "float",
            [typeof(double)]  = "double",
            [typeof(decimal)] = "decimal",
            [typeof(byte)]    = "byte",
            [typeof(byte[])]  = "byte"
        }.ToFrozenDictionary();
        
        public JsonNode Transform(JsonSchemaExporterContext context, JsonNode schema) {
            var type       = context.TypeInfo.Type;
            var underlying = Nullable.GetUnderlyingType(type) ?? type;

            // Custom-converter types exported as the wildcard `true` get a full replacement node.
            // Built by parsing a constant so no AOT-unsafe JsonArray.Add<T>/JsonValue.Create<T> is hit.
            if (StringFormatTypes.TryGetValue(underlying, out var format)) {
                // A Nullable<T> member permits null, so widen the type accordingly.
                var typeToken = underlying != type ? """["string","null"]""" : "\"string\"";
                return JsonNode.Parse($$"""{"type":{{typeToken}},"format":"{{format}}"}""")!;
            }

            if (schema is JsonObject node) {
                ApplyFormatHint(node, underlying);
                ApplyEnumValues(node, underlying, enumAsOneOf);

                // JsonSchemaExporter ties `required` to constructor-parameter optionality (does it
                // have a default?), NOT to nullability. The registry's model treats nullable members
                // as optional, so by default we strip null-permitting properties from each object's
                // `required` array. Pass nullableIsOptional: false to keep STJ's stricter
                // "present-but-nullable" semantics.
                if (nullableIsOptional)
                    DropNullablesFromRequired(node);
            }

            return schema;
        }
        
	    // Adds NJsonSchema's OpenAPI-style format hint to a numeric/binary node when absent.
	    static void ApplyFormatHint(JsonObject node, Type type) {
		    if (FormatHints.TryGetValue(type, out var format) && !node.ContainsKey("format"))
			    node["format"] = format;
	    }

	    // Constrains integer-serialized enums to their declared values. Two representations:
	    //   - default: `enum` + `x-enumNames`, mirroring NJsonSchema's output (inlined here instead
	    //     of a $ref — semantically identical). x-enumNames is NJsonSchema's vendor extension.
	    //   - enumAsOneOf: the spec-idiomatic labelled form — one `oneOf` branch per member pairing
	    //     `const` with a `title` annotation. The `type` keyword is kept in both forms: it stays
	    //     valid alongside the constraint and the registry's type-based compatibility checks key
	    //     off it.
	    // String-serialized enums already carry an `enum` keyword emitted by JsonSchemaExporter.
	    static void ApplyEnumValues(JsonObject node, Type type, bool enumAsOneOf) {
		    if (!type.IsEnum || node.ContainsKey("enum") || node.ContainsKey("oneOf"))
			    return;

		    // Enum.GetValuesAsUnderlyingType exists precisely because Enum.GetValues(Type) carries
		    // [RequiresDynamicCode]; it returns the raw numeric values with no dynamic array creation.
		    var values = Enum.GetValuesAsUnderlyingType(type);
		    var names  = Enum.GetNames(type);

		    // A nullable enum member also permits the null literal.
		    var permitsNull = node["type"] is JsonArray types && types.Any(t => (string?)t == "null");

		    if (enumAsOneOf) {
			    var branches = new List<string>(names.Length + 1);
			    for (var i = 0; i < names.Length; i++)
				    branches.Add($"{{\"const\":{Convert.ToString(values.GetValue(i), CultureInfo.InvariantCulture)},\"title\":\"{names[i]}\"}}");

			    if (permitsNull)
				    branches.Add("""{"const":null}""");

			    node["oneOf"] = JsonNode.Parse($"[{string.Join(",", branches)}]");
			    return;
		    }

		    var literals = new List<string>(names.Length + 1);
		    foreach (var value in values)
			    literals.Add(Convert.ToString(value, CultureInfo.InvariantCulture)!);

		    if (permitsNull)
			    literals.Add("null");

		    node["enum"]        = JsonNode.Parse($"[{string.Join(",", literals)}]");
		    node["x-enumNames"] = JsonNode.Parse($"[\"{string.Join("\",\"", names)}\"]");
	    }

	    // Removes every property whose schema permits null (its `type` is an array containing "null")
	    // from the object's `required` array, making nullable members optional. Non-object nodes and
	    // objects without properties/required are left untouched.
	    //
	    // We mutate the existing array in place rather than building a new one: JsonArray.Add<T> and
	    // JsonValue.Create<T> carry [RequiresUnreferencedCode]/[RequiresDynamicCode], so creating new
	    // nodes would break AOT/trim. RemoveAt and the explicit JsonNode->string operator do not.
	    static void DropNullablesFromRequired(JsonObject node) {
		    if (node["properties"] is not JsonObject properties
		        || node["required"] is not JsonArray required)
			    return;

		    // Walk backwards so RemoveAt doesn't shift the indices we haven't visited yet.
		    for (var i = required.Count - 1; i >= 0; i--) {
			    var name = (string?)required[i];
			    if (name is not null && PermitsNull(properties, name))
				    required.RemoveAt(i);
		    }

		    // Omit an empty `required` rather than emitting `"required": []`.
		    if (required.Count == 0)
			    node.Remove("required");

            // A property permits null when its `type` keyword is an array containing the "null" entry,
            // e.g. {"type": ["string", "null"]}.
            static bool PermitsNull(JsonObject properties, string name) =>
                properties[name] is JsonObject property
             && property["type"] is JsonArray types
             && types.Any(type => (string?)type == "null");
	    }
    }
}
