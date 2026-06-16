using Kurrent.Client.Streams;
using Kurrent.Schema;
using Kurrent.Validation;

namespace Kurrent.Client.Registry;

[PublicAPI]
public record CheckSchemaCompatibilityRequest : SelfValidating<CheckSchemaCompatibilityRequest, CheckSchemaCompatibilityRequest.Validator> {
    /// <summary>
    /// Start building a <see cref="CheckSchemaCompatibilityRequest"/> for the specified schema identifier.
    /// Accepts <see cref="SchemaName"/> or <see cref="SchemaVersionId"/> via implicit conversion.
    /// </summary>
    public static Builder For(SchemaIdentifier identifier) => new(identifier);

    /// <summary>Schema identifier (either a schema name or a schema version id) to check compatibility against.</summary>
    public SchemaIdentifier Identifier { get; init; }

    /// <summary>Schema definition to check for compatibility in string format.</summary>
    public string SchemaDefinition { get; init; } = "";

    /// <summary>Format of the schema, such as Json, Protobuf, Avro, or Bytes.</summary>
    public SchemaDataFormat DataFormat { get; init; } = SchemaDataFormat.Unspecified;

    /// <summary>
    /// Fluent builder for constructing <see cref="CheckSchemaCompatibilityRequest"/> instances.
    /// </summary>
    [PublicAPI]
    public class Builder(CheckSchemaCompatibilityRequest request) : FluentBuilder<Builder, CheckSchemaCompatibilityRequest>(request) {
        public Builder() : this(new CheckSchemaCompatibilityRequest()) { }
        public Builder(SchemaIdentifier identifier) : this(new CheckSchemaCompatibilityRequest { Identifier = identifier }) { }

        /// <inheritdoc />
        protected override CheckSchemaCompatibilityRequest OnBuild(CheckSchemaCompatibilityRequest request) {
            request.EnsureValid();
            return request;
        }

        public Builder WithDefinition(string definition) =>
            With(r => r with { SchemaDefinition = definition });

        public Builder WithDataFormat(SchemaDataFormat dataFormat) =>
            With(r => r with { DataFormat = dataFormat });
    }

    public class Validator : Validator<Validator, CheckSchemaCompatibilityRequest> {
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
