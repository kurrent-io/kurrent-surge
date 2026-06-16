using Kurrent.Client.Streams;
using Kurrent.Schema;
using Kurrent.Validation;

namespace Kurrent.Client.Registry;

[PublicAPI]
public record CreateSchemaRequest : SelfValidating<CreateSchemaRequest, CreateSchemaRequest.Validator> {
    internal static readonly IReadOnlyDictionary<string, string> EmptyTags = new Dictionary<string, string>();

    /// <summary>
    /// Start building a <see cref="CreateSchemaRequest"/> for the specified schema.
    /// </summary>
    public static Builder For(SchemaName schemaName) => new(schemaName);

    /// <summary>Unique name of the schema to create.</summary>
    public SchemaName SchemaName { get; init; } = SchemaName.Unset;

    /// <summary>Schema definition in string format.</summary>
    public string SchemaDefinition { get; init; } = "";

    /// <summary>Format of the schema, such as Json, Protobuf, Avro, or Bytes.</summary>
    public SchemaDataFormat DataFormat { get; init; } = SchemaDataFormat.Unspecified;

    /// <summary>Compatibility mode for the schema.</summary>
    public CompatibilityMode CompatibilityMode { get; init; } = CompatibilityMode.None;

    /// <summary>Human-readable description of the schema.</summary>
    public string Description { get; init; } = "";

    /// <summary>Key-value pairs that provide additional metadata or categorization for the schema.</summary>
    public IReadOnlyDictionary<string, string> Tags { get; init; } = EmptyTags;

    /// <summary>
    /// Fluent builder for constructing <see cref="CreateSchemaRequest"/> instances.
    /// </summary>
    [PublicAPI]
    public class Builder(CreateSchemaRequest request) : FluentBuilder<Builder, CreateSchemaRequest>(request) {
        public Builder() : this(new CreateSchemaRequest()) { }
        public Builder(SchemaName name) : this(new CreateSchemaRequest { SchemaName = name }) { }

        /// <inheritdoc />
        protected override CreateSchemaRequest OnBuild(CreateSchemaRequest request) {
            request.EnsureValid();
            return request;
        }

        public Builder WithDefinition(string definition) =>
            With(r => r with { SchemaDefinition = definition });

        public Builder WithDataFormat(SchemaDataFormat dataFormat) =>
            With(r => r with { DataFormat = dataFormat });

        public Builder WithCompatibility(CompatibilityMode mode) =>
            With(r => r with { CompatibilityMode = mode });

        public Builder WithDescription(string description) =>
            With(r => r with { Description = description });

        public Builder WithTags(IReadOnlyDictionary<string, string> tags) =>
            With(r => r with { Tags = tags });

        public Builder WithTag(string key, string value) =>
            With(r => r with { Tags = new Dictionary<string, string>(r.Tags) { [key] = value } });
    }

    public class Validator : Validator<Validator, CreateSchemaRequest> {
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
