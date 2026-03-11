namespace Kurrent.SDK.Testing.Hosting;

public sealed class KurrentDBTestCluster : IAsyncDisposable {
    const    string                     DefaultUsername = "admin";
    const    string                     DefaultPassword = "changeit";
    readonly Dictionary<string, string> _anonymousConnectionStrings;

    readonly DistributedApplication     _app;
    readonly Dictionary<string, string> _connectionStrings;

    internal KurrentDBTestCluster(
        DistributedApplication app,
        Dictionary<string, string> connectionStrings,
        Dictionary<string, string> anonymousConnectionStrings
    ) {
        _app                        = app;
        _connectionStrings          = connectionStrings;
        _anonymousConnectionStrings = anonymousConnectionStrings;

        var defaultName = connectionStrings.Keys.First();
        AuthenticatedConnectionString = connectionStrings[defaultName];
        AnonymousConnectionString     = anonymousConnectionStrings[defaultName];
    }

    public string AuthenticatedConnectionString { get; }

    public string AnonymousConnectionString { get; }

    public async ValueTask DisposeAsync() {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    public string GetConnectionString(string resourceName) =>
        _connectionStrings.TryGetValue(resourceName, out var cs)
            ? cs
            : throw new InvalidOperationException($"No connection string found for resource '{resourceName}'.");

    public string GetAnonymousConnectionString(string resourceName) =>
        _anonymousConnectionStrings.TryGetValue(resourceName, out var cs)
            ? cs
            : throw new InvalidOperationException($"No anonymous connection string found for resource '{resourceName}'.");

    internal static string InjectCredentials(string baseConnectionString) {
        var uri = new Uri(baseConnectionString);

        return $"{uri.Scheme}://{DefaultUsername}:{DefaultPassword}@{uri.Host}:{uri.Port}{uri.PathAndQuery}";
    }
}
