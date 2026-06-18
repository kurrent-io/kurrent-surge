using Kurrent.Validation;

namespace Kurrent.Client.Indexes;

[PublicAPI]
public record CreateIndexOptions : SelfValidating<CreateIndexOptions, CreateIndexOptions.Validator> {
    /// <summary>Unique name of the index to create.</summary>
    public IndexName Name { get; set; } = IndexName.Unset;

    /// <summary>Filter expression that determines which records are included in the index.</summary>
    public string Filter { get; set; } = "";

    /// <summary>Fields to index, each with a name, selector, and type.</summary>
    public IList<IndexField> Fields { get; set; } = [];

    /// <summary>
    /// Whether to start the index immediately after creation. When <c>null</c>,
    /// the server applies its default behavior.
    /// </summary>
    public bool? Start { get; set; }

    /// <summary>Appends a field to <see cref="Fields"/>.</summary>
    public void AddField(IndexField field) =>
        Fields.Add(field);

    /// <summary>Appends a field to <see cref="Fields"/>.</summary>
    public void AddField(string name, string selector, IndexFieldType type) =>
        Fields.Add(new IndexField(name, selector, type));

    public class Validator : Validator<Validator, CreateIndexOptions> {
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
