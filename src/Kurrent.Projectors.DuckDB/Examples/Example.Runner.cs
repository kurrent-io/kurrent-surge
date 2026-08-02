namespace Kurrent.Projectors.DuckDB.Examples;

/// <summary>
/// How a user wires and runs one of these projections: pick a connection provider, configure the
/// options, create the projector, run it. Every projection type in this folder runs the same way —
/// only the constructor of the projection instance changes.
/// </summary>
/// <remarks>
/// A projection that brings a store of its own — <see cref="SchemaVersionQuackProjection"/> — is built from
/// the transaction the projector hands the factory instead, so the store writes on the transaction's connection:
/// <c>DuckDBProjector.Create(tx => new SchemaVersionQuackProjection(new SchemaVersionQuackStore(tx)), …)</c>.
/// </remarks>
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
