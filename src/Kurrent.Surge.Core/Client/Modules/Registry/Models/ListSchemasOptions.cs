using Kurrent.Validation;

namespace Kurrent.Surge.Client.Registry;

[PublicAPI]
public sealed class ListSchemasOptions : SelfValidatingClass<ListSchemasOptions, ListSchemasOptions.Validator> {
    /// <summary>
    /// Prefix to filter schemas by name. Only schemas whose names start with this prefix are returned.
    /// Empty means no prefix filter.
    /// </summary>
    public string SchemaNamePrefix { get; set; } = "";

    /// <summary>
    /// Key-value pairs to filter schemas by tags. Only schemas matching all specified tags are returned.
    /// Empty means no tag filter.
    /// </summary>
    public IDictionary<string, string> Tags {
        get;
        set => field = value ?? new Dictionary<string, string>();
    } = new Dictionary<string, string>();

    /// <summary>Adds or overwrites a tag filter in <see cref="Tags"/>.</summary>
    public void AddTag(string key, string value) =>
        Tags[key] = value;

    public class Validator : Validator<Validator, ListSchemasOptions> {
        public Validator() { }
    }
}
