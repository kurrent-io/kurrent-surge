namespace Kurrent.Surge.AI.HuggingFace;

/// <summary>A sink for per-file download progress, reported through <see cref="ModelBundle.DownloadAsync"/>.</summary>
public interface IProgressTracker : IProgress<DownloadProgress>;
