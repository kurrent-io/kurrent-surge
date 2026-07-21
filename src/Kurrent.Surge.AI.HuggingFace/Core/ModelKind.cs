namespace Kurrent.Surge.AI.HuggingFace;

/// <summary>
/// The model's on-disk format, which selects the download layout. Derived from the file extension in
/// <see cref="ModelRef.FilePath"/>; <see cref="Unknown"/> when no file is known yet (resolved after listing).
/// </summary>
public enum ModelKind {
    /// <summary>No file is known yet (e.g. a repository URL with no specific file); resolved from the listing.</summary>
    Unknown,

    /// <summary>ONNX graph (<c>.onnx</c>), optionally with external-data shards + tokenizer/config companions.</summary>
    Onnx,

    /// <summary>GGUF single-file weights (<c>.gguf</c>), possibly split across numbered shards.</summary>
    Gguf,

    /// <summary>Safetensors weights (<c>.safetensors</c>), single or sharded via <c>model.safetensors.index.json</c>.</summary>
    Safetensors
}
