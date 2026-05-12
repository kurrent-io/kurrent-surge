using Kurrent.Validation;

namespace Kurrent.Client.Indexes;

[PublicAPI]
public record CreateIndexRequest : SelfValidating<CreateIndexRequest, CreateIndexRequest.Validator> {
    /// <summary>
    /// Start building a <see cref="CreateIndexRequest"/> for the specified index name.
    /// </summary>
    public static Builder For(IndexName name) => new(name);

    /// <summary>Unique name of the index to create.</summary>
    public IndexName Name { get; init; } = IndexName.Unset;

    /// <summary>Filter expression that determines which records are included in the index.</summary>
    public string Filter { get; init; } = "";

    /// <summary>Fields to index, each with a name, selector, and type.</summary>
    public IReadOnlyList<IndexField> Fields { get; init; } = [];

    /// <summary>
    /// Whether to start the index immediately after creation. When <c>null</c>,
    /// the server applies its default behavior.
    /// </summary>
    public bool? Start { get; init; }

    /// <summary>
    /// Fluent builder for constructing <see cref="CreateIndexRequest"/> instances.
    /// </summary>
    [PublicAPI]
    public class Builder(CreateIndexRequest request) : FluentBuilder<Builder, CreateIndexRequest>(request) {
        public Builder() : this(new CreateIndexRequest()) { }
        public Builder(IndexName name) : this(new CreateIndexRequest { Name = name }) { }

        /// <inheritdoc />
        protected override CreateIndexRequest OnBuild(CreateIndexRequest request) {
            request.EnsureValid();
            return request;
        }

        public Builder WithFilter(string filter) =>
            With(r => r with { Filter = filter });

        public Builder WithFields(IEnumerable<IndexField> fields) =>
            With(r => r with { Fields = fields.ToList() });

        public Builder WithField(IndexField field) =>
            With(r => r with { Fields = [.. r.Fields, field] });

        public Builder WithField(string name, string selector, IndexFieldType type) =>
            WithField(new IndexField(name, selector, type));

        public Builder WithStart(bool start = true) =>
            With(r => r with { Start = start });
    }

    public class Validator : Validator<Validator, CreateIndexRequest> {
        public Validator() {
            RuleFor(r => r.Name)
                .Must(name => !name.IsUnset, "Index name must be specified.");

            RuleFor(r => r.Filter)
                .NotEmpty()
                .WithMessage("Filter expression cannot be empty or whitespace.");

            RuleFor(r => r.Fields)
                .NotEmpty()
                .WithMessage("At least one field must be specified.");

            RuleForEach(r => r.Fields).ChildRules(field => {
                field.RuleFor(f => f.Name)
                    .NotEmpty()
                    .WithMessage("Field name cannot be empty or whitespace.");

                field.RuleFor(f => f.Selector)
                    .NotEmpty()
                    .WithMessage("Field selector cannot be empty or whitespace.");

                field.RuleFor(f => f.Type)
                    .Must(t => t != IndexFieldType.Unspecified, "Field type must be specified.");
            });
        }
    }
}
