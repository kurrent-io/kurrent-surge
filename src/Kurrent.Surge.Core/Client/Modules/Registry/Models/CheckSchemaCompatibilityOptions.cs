using Kurrent.Surge.Schema;
using Kurrent.Surge.Validation;

namespace Kurrent.Surge.Client.Registry;

[PublicAPI]
[WithValidation]
public sealed partial class CheckSchemaCompatibilityOptions {
    /// <summary>Schema identifier (either a schema name or a schema version id) to check compatibility against.</summary>
    public SchemaIdentifier Identifier { get; set; }

    /// <summary>Schema definition to check for compatibility in string format.</summary>
    public string SchemaDefinition { get; set; } = "";

    /// <summary>Format of the schema, such as Json, Protobuf, Avro, or Bytes.</summary>
    public SchemaDataFormat DataFormat { get; set; } = SchemaDataFormat.Unspecified;

    static partial void ConfigureRules(Validator rules) {
        rules.RuleFor(r => r.Identifier)
            .Must(id => id.Value is not null && !id.IsUnset, "Schema identifier must be specified.");

        rules.RuleFor(r => r.SchemaDefinition)
            .NotEmpty()
            .WithMessage("Schema definition cannot be empty or whitespace.");

        rules.RuleFor(r => r.DataFormat)
            .IsInEnum()
            .Must(f => f != SchemaDataFormat.Unspecified, "Data format must be specified.");
    }
}
