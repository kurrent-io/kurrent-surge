using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Downloader;

namespace Kurrent.Surge.AI.HuggingFace;

/// <summary>
/// A read-only Hugging Face Hub transport primitive: list a repository's files and download them. The download
/// engine owns all transport for downloads — auth (Bearer) and resume flow through <see cref="DownloadConfiguration"/>;
/// only the tree listing, a JSON REST call, uses a shared, pooled <see cref="HttpClient"/>.
/// </summary>
public sealed class HuggingFaceClient {
    // Identify to the Hub in ModelSharp's User-Agent shape — product/version plus a source URL — rebranded for
    // Kurrent. Version is a placeholder until the package carries a real one.
    const string UserAgent = "Kurrent.Surge.AI.HuggingFace/2.0 (+https://github.com/kurrent-io/kurrent-surge)";

    // One shared handler (connection pool) across all instances; PooledConnectionLifetime recycles connections so
    // a long-lived process reacts to DNS changes. Downloads never use it — the download engine owns its transport.
    static readonly SocketsHttpHandler SharedHandler = new() { PooledConnectionLifetime = TimeSpan.FromMinutes(2) };

    // The Bearer credential for authenticated requests, or null when no token was supplied.
    readonly AuthenticationHeaderValue? _authorization;

    // Pre-configured client for the tree-API REST calls: auth + User-Agent live on its default headers, so
    // requests carry them with no per-call setup. Fresh per instance over the shared pool (disposeHandler:false).
    readonly HttpClient _listing;

    /// <summary>Creates a client that authenticates with an optional Hugging Face <paramref name="token"/> (Bearer).</summary>
    public HuggingFaceClient(string? token = null) {
        _authorization = string.IsNullOrWhiteSpace(token) ? null : new AuthenticationHeaderValue("Bearer", token);

        _listing = new HttpClient(SharedHandler, disposeHandler: false);
        _listing.DefaultRequestHeaders.UserAgent.TryParseAdd(UserAgent);
        _listing.DefaultRequestHeaders.Authorization = _authorization;
    }

    // A fresh download config per call — the engine mutates it during a download, so it can't be shared —
    // preloaded with the recommended fast-and-reliable settings (Downloader README) plus our auth + User-Agent.
    DownloadConfiguration CreateDownloadConfiguration() => new() {
        ChunkCount                  = 8,                // split into 8 parts
        ParallelDownload            = true,             // download chunks in parallel
        ParallelCount               = 4,                // cap concurrent connections — a good citizen
        MaxTryAgainOnFailure        = 5,                // retry transient network errors
        EnableAutoResumeDownload    = true,             // survive interruptions with no extra code
        MaximumMemoryBufferBytes    = 50 * 1024 * 1024, // cap buffering RAM at 50 MB
        CheckDiskSizeBeforeDownload = true,             // fail early on insufficient disk space
        RequestConfiguration = {
            UserAgent     = UserAgent,
            Authorization = _authorization
        }
    };

    #region ->> Listing <<-

    /// <summary>
    /// Lists every file in the repository's tree at its revision, via the paginated Hugging Face
    /// <c>/api/models/{repo}/tree/{revision}</c> endpoint. Each <see cref="RemoteFile"/> carries its resolved
    /// absolute download <see cref="RemoteFile.Url"/>, so downloading needs no further reference.
    /// </summary>
    /// <param name="repositoryUrl">A Hugging Face repository or file URL (parsed into endpoint / repo / revision).</param>
    public async ValueTask<IReadOnlyList<RemoteFile>> ListFilesAsync(string repositoryUrl, CancellationToken ct = default) {
        var reference = new ModelRef(repositoryUrl);
        var files     = new List<RemoteFile>();

        // Follow the HF Link-header pages, streaming each page's entries straight off the wire (no buffered
        // List<TreeEntry>). maxPages only bounds a pathological cyclic "next" chain; real repos are one page.
        var       nextUrl  = $"{reference.Endpoint}/api/models/{reference.Repo}/tree/{Uri.EscapeDataString(reference.Revision)}?recursive=true";
        const int maxPages = 100;

        for (var page = 0; nextUrl is not null; page++) {
            if (page >= maxPages)
                throw new ModelException($"Tree listing for '{reference.Repo}' exceeded {maxPages} pages.");

            using var response = await _listing.GetAsync(nextUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new ModelException($"Failed to list files for '{reference.Repo}' at '{reference.Revision}' (HTTP {(int)response.StatusCode}).");

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

            await foreach (var entry in JsonSerializer.DeserializeAsyncEnumerable(stream, HubJson.Default.TreeEntry, ct).ConfigureAwait(false)) {
                if (entry is null || !string.Equals(entry.Type, "file", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(entry.Path))
                    continue;

                // Size is always present for file entries (lfs.size for LFS, else size); the 0 floor is an
                // unreachable guard for the non-null RemoteFile.Size. Url is resolved here so download is
                // reference-free downstream.
                files.Add(new(entry.Path, entry.Lfs?.Size ?? entry.Size ?? 0L, entry.Lfs?.Oid, reference.ResolveUrl(entry.Path)));
            }

            // Follow the RFC 5988 Link header's rel="next" to the next page, or stop when there isn't one.
            nextUrl = response.Headers.TryGetValues("Link", out var links) &&
                      LinkHeaderParser.Parse(links).TryGetValue("next", out var next)
                ? next
                : null;
        }

        return files;
    }

    #endregion // Listing

    #region ->> Downloading <<-

    /// <summary>
    /// Downloads every <see cref="ModelFile"/> from its <see cref="ModelFile.Url"/> to its
    /// <see cref="ModelFile.LocalPath"/>, one at a time, verifying each against its <see cref="ModelFile.Sha256"/>
    /// when present. Files are fetched <b>unconditionally</b> (any existing copy is overwritten); cache-skip and
    /// force policy are the caller's concern.
    /// </summary>
    public async ValueTask DownloadFilesAsync(ModelFile[] files, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default) {
        // One file at a time: the engine already saturates the link per file via chunk parallelism, so file-level
        // concurrency would just multiply connections against the same host — the Downloader's own multi-file
        // sample downloads sequentially for the same reason.
        for (var i = 0; i < files.Length; i++)
            await FetchAsync(files[i], progress, i, files.Length, ct).ConfigureAwait(false);
    }

    async Task FetchAsync(ModelFile file, IProgress<DownloadProgress>? progress, int fileIndex, int fileCount, CancellationToken ct) {
        // A fresh config per download (the engine mutates it during the download). Auth/UA go through
        // RequestConfiguration inside the factory — NOT CustomHttpClientFactory, which makes the engine skip its
        // own header/auth config entirely.
        var config = CreateDownloadConfiguration();

        using var downloader = new DownloadService(config);

        if (progress is not null)
            downloader.DownloadProgressChanged += (_, e) =>
                progress.Report(
                    new DownloadProgress(
                        file.RepoPath, e.ReceivedBytesSize,
                        e.TotalBytesToReceive > 0 ? e.TotalBytesToReceive : file.Size,
                        fileIndex, fileCount));


        try {
            await downloader.DownloadFileTaskAsync(file.Url, file.LocalPath, ct).ConfigureAwait(false);
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            throw;
        } catch (Exception ex) when (ex is not ModelException) {
            throw new ModelException($"Failed to download '{file.RepoPath}' from '{file.Url}': {ex.Message}", ex);
        }

        // The LFS oid IS the file's SHA-256; a mismatch means the bytes were corrupted in transit — never hand
        // that file out.
        if (file.Sha256 is not null && !await MatchesExpectedHashAsync(file.LocalPath, file.Sha256, ct).ConfigureAwait(false))
            throw new ModelException($"Integrity check failed for '{file.RepoPath}' from '{file.Url}': SHA-256 mismatch.");
    }

    /// <summary>Streams the file through SHA-256 and compares (case-insensitive hex) to <paramref name="expectedSha256"/>.</summary>
    static async Task<bool> MatchesExpectedHashAsync(string localPath, string expectedSha256, CancellationToken ct) {
        await using var stream = File.OpenRead(localPath);

        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);

        return string.Equals(Convert.ToHexStringLower(hash), expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    #endregion // Downloading
}
