namespace Kurrent.SDK.Testing.Hosting;

public static class KurrentDBContainerImageTags {
    public const string Registry = "docker.kurrent.io";

    public const string Latest  = $"{Registry}/kurrent-latest/kurrentdb:latest";
    public const string LTS     = $"{Registry}/kurrent-latest/kurrentdb:lts";
    public const string Nightly = $"{Registry}/kurrent-staging/kurrentdb:nightly";
    public const string CI      = $"{Registry}/kurrent-staging/kurrentdb:ci";
}
