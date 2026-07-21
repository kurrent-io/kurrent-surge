using System.Security.Cryptography;

namespace Kurrent.Surge.AI.HuggingFace;

public sealed class ModelBundle {
    readonly HuggingFaceClient _client;

    internal ModelBundle(HuggingFaceClient client, ModelRef reference, IReadOnlyList<ModelFile> files, bool cached) {
        _client    = client;
        Reference  = reference;
        Files      = files;
        Cached     = cached;
        TotalBytes = Files.Sum(f => f.Size);
    }

    /// <summary>
    /// The reference this model was inspected from.
    /// </summary>
    public ModelRef Reference { get; }

    /// <summary>
    /// True if every file in the bundle is already cached locally, and no download is needed.
    /// </summary>
    public bool Cached { get; }

    /// <summary>
    /// All the files in the bundle, provided by the Layout. The first file is the primary model file, and the rest are companions that must be downloaded alongside it.
    ///</summary>
    public IReadOnlyList<ModelFile> Files { get; }

    /// <summary>
    /// Total size in bytes of every file in the bundle.
    /// </summary>
    public long TotalBytes { get; }

    /// <summary>Downloads every file in the bundle into the local cache, skipping any already-valid copy unless <paramref name="force"/>.</summary>
    /// <param name="progress">Optional sink for per-file download progress.</param>
    /// <param name="force">Re-downloads every file even when a valid cached copy exists.</param>
    /// <param name="ct">Cancellation token.</param>
    public async ValueTask DownloadAsync(IProgress<DownloadProgress>? progress = null, bool force = false, CancellationToken ct = default) {
        // The client downloads unconditionally, so the skip/force policy lives here. A cached copy is valid only
        // when it exists at the expected size AND (when the listing carried an LFS oid) hashes to it — a same-size
        // corrupted file re-fetches instead of being trusted (self-healing cache).
        var pending = new List<ModelFile>(Files.Count);

        foreach (var file in Files)
            if (force || !await IsCachedCopyValidAsync(file, ct).ConfigureAwait(false))
                pending.Add(file);

        if (pending.Count > 0)
            await _client.DownloadFilesAsync(pending.ToArray(), progress, ct).ConfigureAwait(false);

        static async ValueTask<bool> IsCachedCopyValidAsync(ModelFile file, CancellationToken ct) {
            if (!File.Exists(file.LocalPath) || new FileInfo(file.LocalPath).Length != file.Size)
                return false;

            if (file.Sha256 is null)
                return true;

            await using var stream = File.OpenRead(file.LocalPath);
            var             hash   = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
            return string.Equals(Convert.ToHexStringLower(hash), file.Sha256, StringComparison.OrdinalIgnoreCase);
        }
    }
}
