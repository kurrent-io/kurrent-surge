namespace Kurrent.Surge.Mcp.Core.Tests;

/// <summary>
/// Provides a unique temp DuckDB file path per test and deletes it (and its WAL) on teardown.
/// Use with <c>[ClassDataSource&lt;TempDuckDb&gt;(Shared = SharedType.None)]</c> for a fresh
/// database per test.
/// </summary>
public sealed class TempDuckDb : IDisposable {
    readonly string _path;

    public TempDuckDb() {
        _path            = Path.Combine(Path.GetTempPath(), $"mcp-tasks-{TestId.New()}.ddb");
        ConnectionString = $"Data Source={_path}";
    }

    /// <summary>File-backed DuckDB connection string for this test's database.</summary>
    public string ConnectionString { get; }

    public void Dispose() {
        Delete(_path);
        Delete(_path + ".wal");
    }

    static void Delete(string path) {
        try {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch {
            // best-effort cleanup — a lingering temp file is harmless
        }
    }
}
