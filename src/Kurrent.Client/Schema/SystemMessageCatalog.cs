using Kurrent.Client.Streams;

namespace Kurrent.Client;

/// <summary>
/// SDK-internal system mappings, always unioned into every client's resolver. Lives here —
/// not in module constructors — because the resolver snapshot is taken at client
/// construction, before modules exist.
/// </summary>
static class SystemMessageCatalog {
    public static MessageCatalog Instance { get; } = new MessageCatalog("System")
        .Map<StreamMetadata>("$metadata") // metastream
        .Map<string>("$>")                // link
        .MapScavengeEvents();
}

// class SystemMessageCatalogV2  : MessageCatalog {
//     public SystemMessageCatalogV2() : base("System") =>
//         Map<StreamMetadata>("$metadata")
//             .Map<string>("$>")
//             .MapScavengeEvents();
// }

