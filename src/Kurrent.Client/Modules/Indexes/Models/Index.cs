using Contracts = KurrentDB.Protocol.V2.Indexes;

namespace Kurrent.Client.Indexes;

[PublicAPI]
public readonly record struct Index(
    string Name,
    string Filter,
    IReadOnlyList<IndexField> Fields,
    IndexState State
) {
    internal static Index FromProto(Contracts.Index index) =>
        new(
            index.Name,
            index.Filter,
            index.Fields.Select(IndexField.FromProto).ToList(),
            (IndexState)index.State
        );
}
