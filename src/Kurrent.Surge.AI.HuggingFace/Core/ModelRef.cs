namespace Kurrent.Surge.AI.HuggingFace;

/// <summary>
/// A reference to a single Hugging Face model file, parsed from its direct file URL — the raw <c>/blob/</c> or
/// <c>/resolve/</c> URL a user copies from the browser. Pulls out the endpoint, repository, revision, the exact
/// repo-relative <see cref="FilePath"/>, and the <see cref="ModelKind"/>. Only a direct model-file URL is
/// accepted; repository (<c>…/tree/…</c>) URLs are not.
/// </summary>
/// <example>
/// <code>
/// var a = ModelRef.From("https://huggingface.co/Xenova/multilingual-e5-small/blob/main/onnx/model_quantized.onnx");
/// </code>
/// </example>
public readonly record struct ModelRef {
    /// <summary>Parses a direct Hugging Face model-file URL (<c>…/blob|resolve/{rev}/{path}</c>) into a reference.</summary>
    /// <param name="url">A Hugging Face <c>/blob/</c> or <c>/resolve/</c> URL pointing at the model file.</param>
    /// <exception cref="ArgumentException"><paramref name="url"/> is not an absolute URL, is not a <c>/blob/</c> or
    /// <c>/resolve/</c> file URL, or does not point at a known model file (.onnx / .gguf / .safetensors).</exception>
    public ModelRef(string url) {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException($"Model URL must be an absolute Hugging Face file URL, but was '{url}'.", nameof(url));

        // Hugging Face file URL shape: /{owner}/{repo}/{blob|resolve}/{revision}/{path…}
        //   file (view): https://huggingface.co/Xenova/multilingual-e5-small/blob/main/onnx/model_quantized.onnx
        //   file (raw) : https://huggingface.co/Xenova/multilingual-e5-small/resolve/main/onnx/model_quantized.onnx
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Require owner/repo, a blob|resolve marker, a revision, and at least one file-path segment.
        if (segments is not [_, _, "blob" or "resolve", _, _, ..])
            throw new ArgumentException(
                $"Model URL must point directly at a model file (a '/blob/' or '/resolve/' URL), but was '{url}'.", nameof(url));

        Url      = url;
        Endpoint = $"{uri.Scheme}://{uri.Authority}";
        Repo     = $"{segments[0]}/{segments[1]}";
        Revision = segments[3];
        FilePath = string.Join('/', segments.Skip(4));
        Kind     = KindOf(FilePath);

        if (Kind is ModelKind.Unknown)
            throw new ArgumentException(
                $"Model URL must point at a known model file (.onnx / .gguf / .safetensors), but was '{FilePath}'.", nameof(url));
    }

    /// <summary>Parses a direct Hugging Face model-file URL into a <see cref="ModelRef"/>.</summary>
    public static ModelRef From(string url) => new(url);

    /// <summary>The original file URL this reference was parsed from.</summary>
    public string Url { get; }

    /// <summary>The hub endpoint (scheme + host), e.g. <c>https://huggingface.co</c> — a mirror / enterprise host falls out here.</summary>
    public string Endpoint { get; }

    /// <summary>The repository, as <c>owner/repo</c>.</summary>
    public string Repo { get; }

    /// <summary>The git revision (branch, tag, or commit).</summary>
    public string Revision { get; }

    /// <summary>The exact repo-relative path to the model file, e.g. <c>onnx/model_quantized.onnx</c>.</summary>
    public string FilePath { get; }

    /// <summary>The model format, derived from <see cref="FilePath"/>'s extension.</summary>
    public ModelKind Kind { get; }

    /// <summary>Builds the raw download URL for a repo-relative <paramref name="filePath"/> (Hugging Face <c>/resolve/</c>).</summary>
    public string ResolveUrl(string filePath) =>
        $"{Endpoint}/{Repo}/resolve/{Revision}/{filePath}";

    static ModelKind KindOf(string file) =>
        Path.GetExtension(file).ToLowerInvariant() switch {
            ".onnx"        => ModelKind.Onnx,
            ".gguf"        => ModelKind.Gguf,
            ".safetensors" => ModelKind.Safetensors,
            _              => ModelKind.Unknown
        };
}
