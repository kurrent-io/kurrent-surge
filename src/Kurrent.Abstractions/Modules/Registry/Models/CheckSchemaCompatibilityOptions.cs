using Kurrent.Client.Streams;
using Kurrent.Schema;
using Kurrent.Validation;

namespace Kurrent.Client.Registry;

[PublicAPI]
public record CheckSchemaCompatibilityOptions : SelfValidating<CheckSchemaCompatibilityOptions, CheckSchemaCompatibilityOptions.Validator> {
    /// <summary>Schema identifier (either a schema name or a schema version id) to check compatibility against.</summary>
    public SchemaIdentifier Identifier { get; set; }

    /// <summary>Schema definition to check for compatibility in string format.</summary>
    public string SchemaDefinition { get; set; } = "";

    /// <summary>Format of the schema, such as Json, Protobuf, Avro, or Bytes.</summary>
    public SchemaDataFormat DataFormat { get; set; } = SchemaDataFormat.Unspecified;

    public class Validator : Validator<Validator, CheckSchemaCompatibilityOptions> {
        public Validator() {
            RuleFor(r => r.Identifier)
                .Must(id => id.Value is not null && !id.IsUnset, "Schema identifier must be specified.");

            RuleFor(r => r.SchemaDefinition)
                .NotEmpty()
                .WithMessage("Schema definition cannot be empty or whitespace.");

            RuleFor(r => r.DataFormat)
                .IsInEnum()
                .Must(f => f != SchemaDataFormat.Unspecified, "Data format must be specified.");
        }
    }
}
