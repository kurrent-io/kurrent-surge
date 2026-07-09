using Kurrent.Surge.Client.Streams;
using Kurrent.Schema;

namespace Kurrent.Surge.Client.Registry;

public readonly record struct SchemaDetails(
    SchemaDataFormat DataFormat,
    CompatibilityMode Compatibility,
    string Description,
    IReadOnlyDictionary<string, string> Tags
);
