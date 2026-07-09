using Kurrent.Schema.Catalogs;
using Kurrent.Surge.Streaming.Checkpointing;

namespace Kurrent.Surge.Streaming;

/// <summary>
/// This assembly's contract mappings, discovered through the message catalog generator — the same
/// path consumers use. Names are pinned: they are the established wire contract for checkpoint
/// streams and must not follow the protobuf type's full name.
/// <c>AlwaysActive = true</c> keeps the catalog active, so checkpoint contracts resolve in every
/// client regardless of its auto-discovery setting.
/// </summary>
[CustomMessageCatalog]
sealed class StreamingMessageCatalog() : MessageCatalog(
    "KurrentStreaming", alwaysActive: true, catalog => catalog
        .Map<PartitionsCheckpointed>("Kurrent.Surge.Streaming.Checkpointing.PartitionsCheckpointed")
        .Map<CheckpointReset>("Kurrent.Surge.Streaming.Checkpointing.CheckpointReset"));
