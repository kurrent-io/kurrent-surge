namespace Kurrent.Sdk.Testkit.Hosting;

static class KurrentDBContainerImageTags {
    public const string Registry = "docker.kurrent.io";
    public const string Image    = "kurrent-staging/kurrentdb";
    public const string Tag      = "nightly";
    
    public static class Latest {
        public const string Image = $"kurrent-latest/kurrentdb";
        public const string Tag   = "latest";
    }
    
    public static class Preview {
        public const string Image = $"kurrent-preview/kurrentdb";
        public const string Tag   = "latest";
    }
    
    public static class Nightly {
        public const string Image = $"kurrent-staging/kurrentdb";
        public const string Tag   = "nightly";
    }
}
