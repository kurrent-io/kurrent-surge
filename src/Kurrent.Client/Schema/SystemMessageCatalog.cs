using Kurrent.Client.Schema;
using Kurrent.Client.Streams;

namespace Kurrent.Client;

/// <summary>
/// SDK-internal system mappings ($metadata, $>, scavenge events). Discovered automatically like
/// any <see cref="CustomMessageCatalogAttribute"/>; <c>AlwaysActive = true</c> keeps it active,
/// so these foundational mappings are unioned into every client even when auto-discovery is
/// disabled for strict per-client catalog selection.
/// </summary>
[CustomMessageCatalog]
sealed class SystemMessageCatalog() : MessageCatalog(
    "System", alwaysActive: true, catalog => catalog
        .Map<StreamMetadata>("$metadata") // metastream
        .Map<string>("$>")                // link
        .MapScavengeEvents());
