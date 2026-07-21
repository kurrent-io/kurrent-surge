namespace Kurrent.Surge.AI.HuggingFace;

/// <summary>
/// The files a <see cref="IModelLayout"/> selected from a listing: the primary model file plus every
/// companion that must be downloaded alongside it. <see cref="Files"/> is primary-first and de-duplicated.
/// </summary>
/// <param name="Primary">The primary model file the loader opens.</param>
/// <param name="Files">All files to download — the primary first, then its companions.</param>
public sealed record ModelPlan(RemoteFile Primary, IReadOnlyList<RemoteFile> Files);
