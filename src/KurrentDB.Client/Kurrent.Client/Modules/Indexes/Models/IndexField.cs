using Contracts = KurrentDB.Protocol.V2.Indexes;

namespace Kurrent.Client.Indexes;

[PublicAPI]
public readonly record struct IndexField(
    string Name,
    string Selector,
    IndexFieldType Type
) {
    internal static IndexField FromProto(Contracts.IndexField field) =>
        new(field.Name, field.Selector, (IndexFieldType)field.Type);

    internal Contracts.IndexField ToProto() =>
        new() {
            Name     = Name,
            Selector = Selector,
            Type     = (Contracts.IndexFieldType)Type
        };
}
