namespace Kurrent.Processors.Tests;

static class PunyId {
    public static string New() => Guid.NewGuid().ToString("N")[^8..];
}
