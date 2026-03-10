var builder = DistributedApplication.CreateBuilder(args);

// If a connection string is already provided (e.g. from a running instance),
// just reference it. Otherwise, spin up a container.
if (string.IsNullOrEmpty(builder.Configuration["ConnectionStrings:kurrentdb"])) {
    var kurrentdb = builder.AddKurrentDB("kurrentdb");

    // Standard test configuration matching KurrentDBConfiguration.Insecure
    kurrentdb
        .WithEnvironment("KURRENTDB_MEM_DB", "false")
        .WithEnvironment("KURRENTDB_DISABLE_LOG_FILE", "true")
        .WithEnvironment("KURRENTDB_LOG_LEVEL", "Debug")
        .WithEnvironment("KURRENTDB_ENABLE_ATOM_PUB_OVER_HTTP", "true")
        .WithEnvironment("KURRENTDB_RUN_PROJECTIONS", "All")
        .WithEnvironment("KURRENTDB_START_STANDARD_PROJECTIONS", "true")
        .WithEnvironment("KURRENTDB_TELEMETRY_OPTOUT", "true")
        .WithEnvironment("KURRENTDB_ALLOW_UNKNOWN_OPTIONS", "true");

    // Secure mode: mount TLS certificates and configure encryption
    if (string.Equals(builder.Configuration["KurrentDB:Secure"], "true", StringComparison.OrdinalIgnoreCase)) {
        var certsPath = Path.GetFullPath(
            builder.Configuration["KurrentDB:CertsPath"] ?? "../../certs"
        );

        kurrentdb
            .WithBindMount(certsPath, "/etc/kurrentdb/certs", isReadOnly: true)
            .WithEnvironment("KURRENTDB_INSECURE", "false")
            .WithEnvironment("KURRENTDB_USER_CERTIFICATES__ENABLED", "true")
            .WithEnvironment("KURRENTDB_TRUSTED_ROOT_CERTIFICATES_PATH", "/etc/kurrentdb/certs/ca")
            .WithEnvironment("KURRENTDB_CERTIFICATE_FILE", "/etc/kurrentdb/certs/node/node.crt")
            .WithEnvironment("KURRENTDB_CERTIFICATE_PRIVATE_KEY_FILE", "/etc/kurrentdb/certs/node/node.key");
    } else {
        kurrentdb
            .WithEnvironment("KURRENTDB_INSECURE", "true")
            .WithEnvironment("KURRENTDB_USER_CERTIFICATES__ENABLED", "false");
    }
} else {
    builder.AddConnectionString("kurrentdb");
}

builder.Build().Run();
