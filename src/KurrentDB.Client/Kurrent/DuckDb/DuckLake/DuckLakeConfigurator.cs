using DuckDB.NET.Data;

namespace Kurrent.DuckDB;

/// <summary>
/// Configures DuckLake options on a DuckDB connection using set_option calls.
/// </summary>
public sealed class DuckLakeConfigurator {
    readonly DuckDBConnection _connection;
    readonly string           _catalogName;

    /// <summary>
    /// Creates a new DuckLake configurator.
    /// </summary>
    /// <param name="connection">The DuckDB connection.</param>
    /// <param name="catalogName">The DuckLake catalog name (used in CALL statements).</param>
    public DuckLakeConfigurator(DuckDBConnection connection, string catalogName) {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogName);

        _connection  = connection;
        _catalogName = catalogName;
    }

    /// <summary>
    /// Applies all configuration options from the given DuckLake options.
    /// </summary>
    public void Apply(DuckLakeOptions options) {
        ArgumentNullException.ThrowIfNull(options);

        ApplyRetryOptions(options.Retry);
        ApplyGlobalSettings(options.GlobalSettings);

        foreach (var (schema, settings) in options.SchemaSettings)
            ApplySchemaSettings(schema, settings);

        foreach (var (table, settings) in options.TableSettings) {
            var (schema, tableName) = ParseTableName(table);
            ApplyTableSettings(schema, tableName, settings);
        }
    }

    /// <summary>
    /// Applies all configuration options asynchronously.
    /// </summary>
    public async Task ApplyAsync(DuckLakeOptions options, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(options);

        await ApplyRetryOptionsAsync(options.Retry, ct);
        await ApplyGlobalSettingsAsync(options.GlobalSettings, ct);

        foreach (var (schema, settings) in options.SchemaSettings)
            await ApplySchemaSettingsAsync(schema, settings, ct);

        foreach (var (table, settings) in options.TableSettings) {
            var (schema, tableName) = ParseTableName(table);
            await ApplyTableSettingsAsync(schema, tableName, settings, ct);
        }
    }

    /// <summary>
    /// Applies retry options using SET statements.
    /// </summary>
    public void ApplyRetryOptions(DuckLakeRetryOptions options) {
        foreach (var (key, value) in options.ToDictionary())
            ExecuteSet(key, value);
    }

    /// <summary>
    /// Applies retry options asynchronously.
    /// </summary>
    public async Task ApplyRetryOptionsAsync(DuckLakeRetryOptions options, CancellationToken ct = default) {
        foreach (var (key, value) in options.ToDictionary())
            await ExecuteSetAsync(key, value, ct);
    }

    /// <summary>
    /// Applies global scoped settings (includes parquet, file, data, and maintenance options).
    /// </summary>
    public void ApplyGlobalSettings(DuckLakeScopedSettings settings) {
        foreach (var (key, value) in settings.ToDictionary())
            ExecuteSetOption(key, value);
    }

    /// <summary>
    /// Applies global scoped settings asynchronously.
    /// </summary>
    public async Task ApplyGlobalSettingsAsync(DuckLakeScopedSettings settings, CancellationToken ct = default) {
        foreach (var (key, value) in settings.ToDictionary())
            await ExecuteSetOptionAsync(key, value, ct);
    }

    /// <summary>
    /// Applies schema-level settings.
    /// </summary>
    public void ApplySchemaSettings(string schema, DuckLakeScopedSettings settings) {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        foreach (var (key, value) in settings.ToDictionary())
            ExecuteSetOption(schema, key, value);
    }

    /// <summary>
    /// Applies schema-level settings asynchronously.
    /// </summary>
    public async Task ApplySchemaSettingsAsync(string schema, DuckLakeScopedSettings settings, CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        foreach (var (key, value) in settings.ToDictionary())
            await ExecuteSetOptionAsync(schema, key, value, ct);
    }

    /// <summary>
    /// Applies table-level settings.
    /// </summary>
    public void ApplyTableSettings(string schema, string table, DuckLakeScopedSettings settings) {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);

        foreach (var (key, value) in settings.ToDictionary())
            ExecuteSetOption(schema, table, key, value);
    }

    /// <summary>
    /// Applies table-level settings asynchronously.
    /// </summary>
    public async Task ApplyTableSettingsAsync(string schema, string table, DuckLakeScopedSettings settings, CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);

        foreach (var (key, value) in settings.ToDictionary())
            await ExecuteSetOptionAsync(schema, table, key, value, ct);
    }

    /// <summary>
    /// Sets a single option at global scope.
    /// </summary>
    public void SetOption(string option, string value) => ExecuteSetOption(option, value);

    /// <summary>
    /// Sets a single option at schema scope.
    /// </summary>
    public void SetOption(string schema, string option, string value) => ExecuteSetOption(schema, option, value);

    /// <summary>
    /// Sets a single option at table scope.
    /// </summary>
    public void SetOption(string schema, string table, string option, string value) => ExecuteSetOption(schema, table, option, value);

    /// <summary>
    /// Sets a single option at global scope asynchronously.
    /// </summary>
    public Task SetOptionAsync(string option, string value, CancellationToken ct = default) =>
        ExecuteSetOptionAsync(option, value, ct);

    /// <summary>
    /// Sets a single option at schema scope asynchronously.
    /// </summary>
    public Task SetOptionAsync(string schema, string option, string value, CancellationToken ct = default) =>
        ExecuteSetOptionAsync(schema, option, value, ct);

    /// <summary>
    /// Sets a single option at table scope asynchronously.
    /// </summary>
    public Task SetOptionAsync(string schema, string table, string option, string value, CancellationToken ct = default) =>
        ExecuteSetOptionAsync(schema, table, option, value, ct);

    // SET variable = value (for extension-level settings like ducklake_max_retry_count)
    void ExecuteSet(string variable, string value) {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"SET {variable} = {FormatValue(value)}";
        cmd.ExecuteNonQuery();
    }

    async Task ExecuteSetAsync(string variable, string value, CancellationToken ct) {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"SET {variable} = {FormatValue(value)}";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // CALL catalog.set_option('option', 'value') - global scope
    void ExecuteSetOption(string option, string value) {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"CALL {Quote(_catalogName)}.set_option({Literal(option)}, {Literal(value)})";
        cmd.ExecuteNonQuery();
    }

    async Task ExecuteSetOptionAsync(string option, string value, CancellationToken ct) {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"CALL {Quote(_catalogName)}.set_option({Literal(option)}, {Literal(value)})";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // CALL catalog.set_option('schema', 'option', 'value') - schema scope
    void ExecuteSetOption(string schema, string option, string value) {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"CALL {Quote(_catalogName)}.set_option({Literal(schema)}, {Literal(option)}, {Literal(value)})";
        cmd.ExecuteNonQuery();
    }

    async Task ExecuteSetOptionAsync(string schema, string option, string value, CancellationToken ct) {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"CALL {Quote(_catalogName)}.set_option({Literal(schema)}, {Literal(option)}, {Literal(value)})";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // CALL catalog.set_option('schema', 'table', 'option', 'value') - table scope
    void ExecuteSetOption(string schema, string table, string option, string value) {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"CALL {Quote(_catalogName)}.set_option({Literal(schema)}, {Literal(table)}, {Literal(option)}, {Literal(value)})";
        cmd.ExecuteNonQuery();
    }

    async Task ExecuteSetOptionAsync(string schema, string table, string option, string value, CancellationToken ct) {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"CALL {Quote(_catalogName)}.set_option({Literal(schema)}, {Literal(table)}, {Literal(option)}, {Literal(value)})";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    static string Literal(string value) => $"'{value.Replace("'", "''")}'";

    static string FormatValue(string value) {
        // Numbers don't need quotes
        if (double.TryParse(value, out _))
            return value;

        // Booleans
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("false", StringComparison.OrdinalIgnoreCase))
            return value.ToLowerInvariant();

        return Literal(value);
    }

    static (string Schema, string Table) ParseTableName(string fullyQualifiedName) {
        var dotIndex = fullyQualifiedName.IndexOf('.');
        if (dotIndex < 0)
            return ("main", fullyQualifiedName);

        return (fullyQualifiedName[..dotIndex], fullyQualifiedName[(dotIndex + 1)..]);
    }
}
