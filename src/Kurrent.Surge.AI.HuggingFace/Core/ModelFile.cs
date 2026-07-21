namespace Kurrent.Surge.AI.HuggingFace;

public record ModelFile {
    /// <summary>
    /// The absolute local path of the file.
    /// </summary>
    public string LocalPath { get; init; } = "";

    /// <summary>
    /// The repo-relative path of the file (e.g. <c>onnx/model.onnx</c>).
    /// </summary>
    public string RepoPath { get; init; } = "";

    /// <summary>
    /// The full url of the file in the repository.
    /// </summary>
    public string Url { get; init; } = "";

    /// <summary>
    /// The size of the file in bytes.
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    /// The expected SHA-256 hash of the file, or <c>null</c> if unknown.
    /// </summary>
    public string? Sha256 { get; init; }
}
