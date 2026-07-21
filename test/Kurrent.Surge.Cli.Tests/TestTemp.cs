namespace Kurrent.Surge.Cli.Tests;

/// <summary>Per-test throwaway cache directory: a unique temp root, cleaned up best-effort.</summary>
static class TestTemp {
    public static string NewDirectory() {
        var directory = Path.Combine(Path.GetTempPath(), "kurrent-surge-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static void Cleanup(string directory) {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        catch (IOException) { /* best-effort */ }
        catch (UnauthorizedAccessException) { /* best-effort */ }
    }
}
