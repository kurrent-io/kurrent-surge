using Kurrent.Client.Streams;
using Kurrent.Schema;
using Kurrent.Validation;

namespace Kurrent.Client.Registry;

[PublicAPI]
public record CreateSchemaOptions : SelfValidating<CreateSchemaOptions, CreateSchemaOptions.Validator> {
    /// <summary>Unique name of the schema to create.</summary>
    public SchemaName SchemaName { get; set; } = SchemaName.Unset;

    /// <summary>Schema definition in string format.</summary>
    public string SchemaDefinition { get; set; } = "";

    /// <summary>Format of the schema, such as Json, Protobuf, Avro, or Bytes.</summary>
    public SchemaDataFormat DataFormat { get; set; } = SchemaDataFormat.Unspecified;

    /// <summary>Compatibility mode for the schema.</summary>
    public CompatibilityMode CompatibilityMode { get; set; } = CompatibilityMode.None;

    /// <summary>Human-readable description of the schema.</summary>
    public string Description { get; set; } = "";

    /// <summary>Key-value pairs that provide additional metadata or categorization for the schema.</summary>
    public IDictionary<string, string> Tags {
        get;
        set => field = value ?? new Dictionary<string, string>();
    } = new Dictionary<string, string>();

    /// <summary>Adds or overwrites a tag in <see cref="Tags"/>.</summary>
    public void AddTag(string key, string value) =>
        Tags[key] = value;

    public class Validator : Validator<Validator, CreateSchemaOptions> {
        public Validator() {
            RuleFor(r => r.SchemaName)
                .Must(name => !name.IsUnset, "Schema name must be specified.");

            RuleFor(r => r.SchemaDefinition)
                .NotEmpty()
                .WithMessage("Schema definition cannot be empty or whitespace.");

            RuleFor(r => r.DataFormat)
                .IsInEnum()
                .Must(f => f != SchemaDataFormat.Unspecified, "Data format must be specified.");

            RuleFor(r => r.CompatibilityMode)
                .IsInEnum();
        }
    }
}
