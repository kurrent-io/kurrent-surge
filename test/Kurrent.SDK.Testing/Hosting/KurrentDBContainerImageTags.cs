namespace Kurrent.SDK.Testing.Hosting;

public static class KurrentDBContainerImageTags {
    public const string Registry = "docker.kurrent.io";

    public static class Latest {
        public const string Image = "kurrent-latest/kurrentdb";
        public const string Tag   = "latest";
    }

    public static class LTS {
        public const string Image = "kurrent-lts/kurrentdb";
        public const string Tag   = "latest";
    }

    public static class Preview {
        public const string Image = "kurrent-preview/kurrentdb";
        public const string Tag   = "latest";
    }
}
