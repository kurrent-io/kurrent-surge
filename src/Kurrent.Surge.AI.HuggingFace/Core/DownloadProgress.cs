namespace Kurrent.Surge.AI.HuggingFace;

/// <summary>A progress update for one downloading file within a bundle.</summary>
/// <param name="File">Repo-relative path of the file being downloaded.</param>
/// <param name="BytesDownloaded">Bytes received so far for this file.</param>
/// <param name="TotalBytes">Total size in bytes, when known.</param>
/// <param name="FileIndex">Zero-based index of this file within the plan.</param>
/// <param name="FileCount">Total number of files in the plan.</param>
public readonly record struct DownloadProgress(
    string File,
    long BytesDownloaded,
    long? TotalBytes,
    int FileIndex,
    int FileCount
) {
    /// <summary>Fraction of this file complete in [0,1], or null when the total size is unknown.</summary>
    public double? Fraction => TotalBytes is > 0 ? (double)BytesDownloaded / TotalBytes.Value : null;
}
