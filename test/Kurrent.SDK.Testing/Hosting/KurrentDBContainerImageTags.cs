namespace Kurrent.SDK.Testing.Hosting;

public static class KurrentDBContainerImageTags {
    public const string Registry = "docker.kurrent.io";

    public static class Latest {
        public const string Image = "kurrent-latest/kurrentdb";
        public const string Tag   = "latest";
    }

    public static class Staging {
        public const string Image = "kurrent-staging/kurrentdb";
        public const string Tag   = "ci";
    }
}

// {
// "ci": {
//     "tag": "ci",
//     "image": "kurrentdb",
//     "registry": "docker.kurrent.io/kurrent-staging",
//     "fullname": "docker.kurrent.io/kurrent-staging/kurrentdb:ci"
// }
// }
