namespace Kurrent.Surge.AI.HuggingFace;

/// <summary>A single file in a repository listing.</summary>
/// <param name="Path">Repo-relative path (e.g. <c>onnx/model.onnx</c>).</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="Sha256">Expected SHA-256 (LFS oid) when known.</param>
/// <param name="Url">The full URL to access the file.</param>
public readonly record struct RemoteFile(string Path, long Size, string? Sha256 = null, string? Url = null);