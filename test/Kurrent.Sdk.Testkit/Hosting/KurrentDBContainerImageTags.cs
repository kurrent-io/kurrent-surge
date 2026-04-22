namespace Kurrent.Sdk.Testkit.Hosting;

public static class KurrentDBContainerImageTags {
    public const string Registry = "docker.kurrent.io";

    public static class Latest {
        public const string Image = $"{Registry}/kurrent-latest/kurrentdb";
        public const string Tag   = "latest";
    }

    public static class Nightly {
        public const string Image = $"{Registry}/kurrent-staging/kurrentdb";
        public const string Tag   = "nightly";
    }
}
