using Kurrent.Client.Streams;

namespace Kurrent.Client.Registry;

public readonly record struct SchemaDetails(
    SchemaDataFormat DataFormat,
    CompatibilityMode Compatibility,
    string Description,
    IReadOnlyDictionary<string, string> Tags
) {
    internal static SchemaDetails FromProto(KurrentDB.Protocol.V2.Registry.SchemaDetails details) {
		ArgumentNullException.ThrowIfNull(details);

		return new SchemaDetails(
            (SchemaDataFormat)details.DataFormat,
            (CompatibilityMode)details.Compatibility,
            details.Description = details.HasDescription ? details.Description : string.Empty,
            details.Tags
        );
    }

    internal KurrentDB.Protocol.V2.Registry.SchemaDetails ToProto() {
        var details = new KurrentDB.Protocol.V2.Registry.SchemaDetails {
            DataFormat    = (KurrentDB.Protocol.V2.Registry.SchemaDataFormat)DataFormat,
            Compatibility = (KurrentDB.Protocol.V2.Registry.CompatibilityMode)Compatibility,
            //Tags          = { Tags.ToDictionary(x => x.Key, x => x.Value) }
        };

        if (!string.IsNullOrEmpty(Description))
            details.Description = Description;

        foreach (var tag in Tags)
            details.Tags.Add(tag.Key, tag.Value);

        return details;
    }
}
