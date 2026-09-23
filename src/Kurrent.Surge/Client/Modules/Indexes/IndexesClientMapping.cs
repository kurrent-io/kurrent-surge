using Contracts = KurrentDB.Protocol.V2.Indexes;

namespace Kurrent.Surge.Client.Indexes;

// Protobuf mapping for the Index/IndexField contract types. It lives in the client (where the
// protobuf Contracts are available) as C# 14 extension members on the protobuf-free Core
// types — a partial split would be illegal here, since those types compile into a different assembly.
static class IndexesClientMapping {
    extension(Index) {
        internal static Index FromProto(Contracts.Index index) =>
            new(
                IndexName.From(index.Name),
                index.Filter,
                index.Fields.Select(f => IndexField.FromProto(f)).ToList(),
                (IndexState)index.State
            );
    }

    extension(IndexField) {
        internal static IndexField FromProto(Contracts.IndexField field) =>
            new(field.Name, field.Selector, (IndexFieldType)field.Type);
    }

    extension(IndexField field) {
        internal Contracts.IndexField ToProto() =>
            new() {
                Name     = field.Name,
                Selector = field.Selector,
                Type     = (Contracts.IndexFieldType)field.Type
            };
    }
}
