using Contracts = KurrentDB.Protocol.V2.Indexes;

namespace Kurrent.Client.Indexes;

// Protobuf mappers for the Index/IndexField contract types. They live in the client (where the
// protobuf Contracts are available) as C# 14 extension members on the protobuf-free Abstractions
// types — a partial split would be illegal here, since those types compile into a different assembly.
static class IndexMappers {
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
