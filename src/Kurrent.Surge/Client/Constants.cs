namespace Kurrent.Surge;

static class Constants {
	public static class Headers {

		/// <summary>
		/// Name of the client (e.g., dotnet, java, go, python, rust, nodejs)
		/// </summary>
		public const string ClientName = "kurrentdb.client.name";

		/// <summary>
		/// Version of the client (e.g., 2.0.1)
		/// </summary>
		public const string ClientVersion = "kurrentdb.client.version";

		/// <summary>
		/// Operating system name (e.g., linux, windows, darwin)
		/// </summary>
		public const string OsName = "kurrentdb.client.env.os.name";

		/// <summary>
		/// Operating system version (e.g., 10.0.22621)
		/// </summary>
		public const string OsVersion = "kurrentdb.client.env.os.version";

		/// <summary>
		/// Runtime or interpreter name (e.g., .NET, Node.js, Python)
		/// </summary>
		public const string RuntimeName = "kurrentdb.client.env.runtime.name";

		/// <summary>
		/// Runtime or interpreter version (e.g., 8.0.3, 20.10.0, 3.11.1)
		/// </summary>
		public const string RuntimeVersion = "kurrentdb.client.env.runtime.version";

		/// <summary>
		/// CPU architecture (e.g., x64, arm64)
		/// </summary>
		public const string HostArchitecture = "kurrentdb.client.env.architecture";

		/// <summary>
		/// Logical connection name supplied by the client options.
		/// </summary>
		public const string ConnectionName = "connection-name";
    }
    
    public static class Metadata {
        public const string Type        = "type";
        public const string Created     = "created";
        public const string ContentType = "content-type";
    }
}
