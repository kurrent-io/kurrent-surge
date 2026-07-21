namespace Kurrent.Surge.AI.HuggingFace;

/// <summary>Options for constructing a <see cref="HuggingFaceHub"/> without a DI container.</summary>
public sealed class HubOptions {
    /// <summary>
    /// Local cache root; when null, resolved from <c>HF_HUB_CACHE</c>, then <c>~/.cache/huggingface/hub</c>
    /// (the same default the Hugging Face CLI and Python tooling use).
    /// </summary>
    public string? CacheDirectory { get; set; }

    /// <summary>
    /// Explicit Hugging Face token; when null, resolved from <c>HF_TOKEN</c> / the token file. Sent as a
    /// <c>Bearer</c> credential on every request.
    /// </summary>
    public string? Token { get; set; }
}
