namespace Kurrent.Surge.Client.Indexes;

[PublicAPI]
public readonly record struct IndexField(
    string Name,
    string Selector,
    IndexFieldType Type
);
