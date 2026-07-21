namespace Kurrent.Surge.AI.HuggingFace;

/// <summary>
/// Static one-liner entry points on <see cref="HuggingFaceHub"/> for the common case: a standalone hub built
/// from default <see cref="HubOptions"/> (env-resolved token, default cache root), with no options to construct.
/// </summary>
public static class HuggingFaceHubExtensions {
    extension(HuggingFaceHub) {
        /// <summary>Parses <paramref name="url"/> and inspects it against the Hugging Face Hub.</summary>
        public static ValueTask<ModelBundle> InspectAsync(string url, CancellationToken ct = default) =>
            new HuggingFaceHub(new HubOptions()).InspectModelAsync(ModelRef.From(url), ct);

        /// <summary>Inspects <paramref name="url"/> and downloads the resulting bundle.</summary>
        public static async ValueTask<ModelBundle> DownloadAsync(
            string url, IProgress<DownloadProgress>? progress = null, bool force = false, CancellationToken ct = default
        ) {
            var bundle = await HuggingFaceHub.InspectAsync(url, ct).ConfigureAwait(false);
            await bundle.DownloadAsync(progress, force, ct).ConfigureAwait(false);
            return bundle;
        }
    }
}
