namespace Kurrent.Projectors.DuckDB.Examples;

public static class Runner {
    public static async Task RunAsync(CancellationToken ct) {
        var clientProvider = DuckDBConnectionProvider.Create("DataSource=schema_registry.duckdb");

        var projector = DuckDBProjector.Create(
            new SchemaVersionProjection(),
            options => {
                options.ClientProvider = clientProvider;
                options.ProjectionId   = "schema-version-projection";
            });

        await projector.RunAsync(ct).ConfigureAwait(false);
    }
}
