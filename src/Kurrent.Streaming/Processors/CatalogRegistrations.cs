using Kurrent.Client;
using Kurrent.Client.Schema;
using Kurrent.Streaming.Checkpointing;

namespace Kurrent.Streaming;

/// <summary>
/// This assembly's contract mappings, discovered through the message catalog generator — the same
/// path consumers use. Names are pinned: they are the established wire contract for checkpoint
/// streams and must not follow the type's full name (the types are nested under Contracts).
/// <c>AlwaysActive = true</c> keeps the catalog active, so checkpoint contracts resolve in every
/// client regardless of its auto-discovery setting.
/// </summary>
[CustomMessageCatalog]
sealed class StreamingMessageCatalog() : MessageCatalog(
    "KurrentStreaming", alwaysActive: true, catalog => catalog
        .Map<Contracts.PartitionsCheckpointed>("Kurrent.Streaming.Checkpointing.PartitionsCheckpointed")
        .Map<Contracts.CheckpointReset>("Kurrent.Streaming.Checkpointing.CheckpointReset"));
