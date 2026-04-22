namespace Kurrent.Sdk.Testkit.Hosting;

public sealed class KurrentDBTestCluster : IAsyncDisposable {
    const string DefaultUsername = "admin";
    const string DefaultPassword = "changeit";

    readonly Dictionary<string, string> _anonymousConnectionStrings;

    readonly DistributedApplication     _app;
    readonly Dictionary<string, string> _connectionStrings;

    internal KurrentDBTestCluster(
        DistributedApplication     app,
        Dictionary<string, string> connectionStrings,
        Dictionary<string, string> anonymousConnectionStrings,
        string?                    certsDirectory
    ) {
        _app                        = app;
        _connectionStrings          = connectionStrings;
        _anonymousConnectionStrings = anonymousConnectionStrings;
        CertsDirectory              = certsDirectory;

        var defaultName = connectionStrings.Keys.First();

        AuthenticatedConnectionString = connectionStrings[defaultName];
        AnonymousConnectionString     = anonymousConnectionStrings[defaultName];
    }

    public string AuthenticatedConnectionString { get; }

    public string AnonymousConnectionString { get; }

    public string? CertsDirectory { get; }

    public async ValueTask DisposeAsync() {
        await _app.StopAsync();
        await _app.DisposeAsync();

        if (CertsDirectory is { } certsDirectory && Directory.Exists(certsDirectory))
            Directory.Delete(certsDirectory, recursive: true);
    }

    public string GetConnectionString(string resourceName) =>
        _connectionStrings.TryGetValue(resourceName, out var cs)
            ? cs
            : throw new InvalidOperationException($"No connection string found for resource '{resourceName}'.");

    public string GetAnonymousConnectionString(string resourceName) =>
        _anonymousConnectionStrings.TryGetValue(resourceName, out var cs)
            ? cs
            : throw new InvalidOperationException(
                $"No anonymous connection string found for resource '{resourceName}'."
            );

    internal static string InjectCredentials(string baseConnectionString) {
        var uri = new Uri(baseConnectionString);

        return $"{uri.Scheme}://{DefaultUsername}:{DefaultPassword}@{uri.Host}:{uri.Port}{uri.PathAndQuery}";
    }
}
