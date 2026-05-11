using Kurrent.Validation;

namespace Kurrent.Client.Registry;

[PublicAPI]
public record ListSchemasRequest : SelfValidating<ListSchemasRequest, ListSchemasRequest.Validator> {
    /// <summary>Start building a new <see cref="ListSchemasRequest"/>.</summary>
    public static Builder New => new();

    /// <summary>
    /// Prefix to filter schemas by name. Only schemas whose names start with this prefix are returned.
    /// Empty means no prefix filter.
    /// </summary>
    public string SchemaNamePrefix { get; init; } = "";

    /// <summary>
    /// Key-value pairs to filter schemas by tags. Only schemas matching all specified tags are returned.
    /// Empty means no tag filter.
    /// </summary>
    public IReadOnlyDictionary<string, string> Tags { get; init; } = CreateSchemaRequest.EmptyTags;

    /// <summary>
    /// Fluent builder for constructing <see cref="ListSchemasRequest"/> instances.
    /// </summary>
    [PublicAPI]
    public class Builder(ListSchemasRequest request) : FluentBuilder<Builder, ListSchemasRequest>(request) {
        public Builder() : this(new ListSchemasRequest()) { }

        /// <inheritdoc />
        protected override ListSchemasRequest OnBuild(ListSchemasRequest request) {
            request.EnsureValid();
            return request;
        }

        public Builder WithPrefix(string prefix) =>
            With(r => r with { SchemaNamePrefix = prefix });

        public Builder WithTags(IReadOnlyDictionary<string, string> tags) =>
            With(r => r with { Tags = tags });

        public Builder WithTag(string key, string value) =>
            With(r => r with { Tags = new Dictionary<string, string>(r.Tags) { [key] = value } });
    }

    public class Validator : Validator<Validator, ListSchemasRequest> {
        public Validator() {
            // No rules today — both filters are optional. Validator structure is kept
            // so future facets (paging, sort, limits) can attach rules without a shape change.
        }
    }
}
