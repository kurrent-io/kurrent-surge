using Google.Protobuf.WellKnownTypes;

namespace Kurrent.Client.Registry;

public readonly record struct Schema(
    SchemaName SchemaName,
    SchemaDetails Details,
    int LatestSchemaVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
) {
    internal static Schema FromProto(KurrentDB.Protocol.V2.Registry.Schema schema) {
        ArgumentNullException.ThrowIfNull(schema);

        return new Schema(
            SchemaName.From(schema.SchemaName),
            SchemaDetails.FromProto(schema.Details),
            schema.LatestSchemaVersion,
            schema.CreatedAt.ToDateTimeOffset(),
            schema.UpdatedAt.ToDateTimeOffset()
        );
    }

    internal KurrentDB.Protocol.V2.Registry.Schema ToProto() =>
        new() {
            SchemaName          = SchemaName,
            Details             = Details.ToProto(),
            LatestSchemaVersion = LatestSchemaVersion,
            CreatedAt           = Timestamp.FromDateTimeOffset(CreatedAt),
            UpdatedAt           = Timestamp.FromDateTimeOffset(UpdatedAt)
        };
}
