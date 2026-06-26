namespace Kurrent.Client.Indexes;

[PublicAPI]
public readonly record struct Index(
    IndexName Name,
    string Filter,
    IReadOnlyList<IndexField> Fields,
    IndexState State
);
