namespace Kurrent.Surge.AI.HuggingFace;

/// <summary>
/// Pure (IO-free) file selection for a model format. Given a repository <em>listing</em>, decides which file
/// is the primary model and which companions must be downloaded alongside it. One implementation per
/// <see cref="ModelKind"/>; no network, no hub knowledge, no downloading.
/// </summary>
public interface IModelLayout {
    /// <summary>The format this layout handles.</summary>
    ModelKind Kind { get; }

    /// <summary>
    /// Selects the primary file and its companions from <paramref name="files"/>, honouring an optional
    /// <see cref="ModelRef.FilePath"/> on <paramref name="reference"/>.
    /// </summary>
    /// <exception cref="ModelException">No file matching this layout was found in the listing.</exception>
    ModelPlan Select(IReadOnlyList<RemoteFile> files, ModelRef reference);
}