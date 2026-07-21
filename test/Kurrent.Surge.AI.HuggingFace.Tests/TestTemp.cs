namespace Kurrent.Surge.AI.HuggingFace.Tests;

/// <summary>Per-test throwaway cache directory: a unique temp root, cleaned up best-effort.</summary>
static class TestTemp {
    public static string NewDirectory() {
        var directory = Path.Combine(Path.GetTempPath(), "kurrent-surge-ai-v2-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static void Cleanup(string directory) {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        catch (IOException) { /* best-effort */ }
        catch (UnauthorizedAccessException) { /* best-effort */ }
    }
}
