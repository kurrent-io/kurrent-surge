namespace Kurrent.Surge.AI.HuggingFace;

/// <summary>A model snapshot found in the local cache, one entry per cached revision.</summary>
/// <param name="Repo">The repository, as <c>owner/repo</c>.</param>
/// <param name="Revision">The git revision (branch, tag, or commit) this snapshot was downloaded at.</param>
/// <param name="Path">Absolute local path to this snapshot's directory.</param>
/// <param name="FileCount">Number of files in this snapshot, counted recursively.</param>
/// <param name="TotalBytes">Total size in bytes of every file in this snapshot.</param>
public readonly record struct CachedModel(string Repo, string Revision, string Path, int FileCount, long TotalBytes);
