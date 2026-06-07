using Kurrent.Client.Schema;
using Kurrent.Streaming.Checkpointing;

// Exports this assembly's contract mappings through the message catalog GENERATOR — the same
// path consumers use. Names are pinned: they are the established wire contract for checkpoint
// streams and must not follow the type's full name (the types are nested under Contracts).
[assembly: MessageType<Contracts.PartitionsCheckpointed>("Kurrent.Streaming.Checkpointing.PartitionsCheckpointed", Catalog = "KurrentStreaming")]
[assembly: MessageType<Contracts.CheckpointReset>("Kurrent.Streaming.Checkpointing.CheckpointReset", Catalog = "KurrentStreaming")]
