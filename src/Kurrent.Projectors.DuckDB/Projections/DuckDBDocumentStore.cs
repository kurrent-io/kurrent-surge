using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using DuckDB.NET.Data;

using Kurrent.Projectors.Transactional;
using Kurrent.Quack;

namespace Kurrent.Projectors.DuckDB;

[PublicAPI]
public sealed class DuckDBDocumentStore(TimeProvider? time = null, JsonSerializerOptions? json = null) : IDocumentStore {
    readonly TimeProvider _time = time ?? TimeProvider.System;

    readonly JsonSerializerOptions _json =
        json ?? Surge.Client.Schema.Serialization.Json.JsonSchemaSerializerOptions.DefaultJsonSerializerOptions;

    DuckDBProjectionTransaction? _transaction;

    public void Bind(DuckDBProjectionTransaction transaction) =>
        _transaction = transaction;

    public async ValueTask EnsureCollection<TDocument>(CancellationToken cancellationToken) {
        var connection = await Connection(cancellationToken).ConfigureAwait(false);

        using var command = connection.CreateCommand();

        command.CommandText = $"""
            CREATE TABLE IF NOT EXISTS "{DocumentCollection.For<TDocument>()}" (
                key       VARCHAR     NOT NULL PRIMARY KEY,
                document  JSON        NOT NULL,
                version   BIGINT      NOT NULL,
                timestamp TIMESTAMPTZ NOT NULL
            )
            """;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<StoredDocument<TDocument, TKey>> Load<TDocument, TKey>(
        LoadRequest<TKey> request, [EnumeratorCancellation] CancellationToken cancellationToken
    ) where TDocument : class where TKey : notnull {
        if (request.Keys.Length == 0)
            yield break;

        var requested = new Dictionary<string, TKey>(request.Keys.Length, StringComparer.Ordinal);

        foreach (var key in request.Keys)
            requested[KeyText(key)] = key;

        var connection = await Connection(cancellationToken).ConfigureAwait(false);

        using var command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT key, document, version, timestamp
            FROM "{DocumentCollection.For<TDocument>()}"
            WHERE key IN (SELECT unnest($keys))
            """;

        command.Parameters.Add(new DuckDBParameter("keys", requested.Keys.ToList()));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) {
            var document = JsonSerializer.Deserialize(reader.GetString(1), TypeInfo<TDocument>());

            if (document is null)
                continue;

            yield return new(
                requested[reader.GetString(0)],
                document,
                reader.GetInt64(2),
                reader.GetFieldValue<DateTimeOffset>(3));
        }
    }

    public async ValueTask Save<TDocument, TKey>(
        SaveRequest<TDocument, TKey> request, CancellationToken cancellationToken
    ) where TDocument : class where TKey : notnull {
        if (request.Tracked.Length == 0)
            return;

        var connection = await Connection(cancellationToken).ConfigureAwait(false);

        var keys      = new List<string?>(request.Tracked.Length);
        var documents = new List<string?>(request.Tracked.Length);
        var expected  = new List<string?>(request.Tracked.Length);
        var versions  = new List<string?>(request.Tracked.Length);

        foreach (var (key, expectedVersion, version, document) in request.Tracked) {
            keys.Add(KeyText(key));
            documents.Add(document is null ? null : JsonSerializer.Serialize(document, TypeInfo<TDocument>()));
            expected.Add(expectedVersion.ToString(CultureInfo.InvariantCulture));
            versions.Add(version.ToString(CultureInfo.InvariantCulture));
        }

        using var command = connection.CreateCommand();

        command.CommandText = $"""
            MERGE INTO "{DocumentCollection.For<TDocument>()}" AS t
            USING (SELECT unnest($keys)               AS key,
                          unnest($documents)          AS document,
                          unnest($expected)::BIGINT   AS expected,
                          unnest($versions)::BIGINT   AS version,
                          $timestamp::TIMESTAMPTZ     AS timestamp) AS s
               ON t.key = s.key
             WHEN MATCHED AND t.version = s.expected AND s.document IS NULL THEN
                  DELETE
             WHEN MATCHED AND t.version = s.expected THEN
                  UPDATE SET document = s.document, version = s.version, timestamp = s.timestamp
             WHEN MATCHED THEN
                  ERROR 'key ' || s.key || ' expected version ' || s.expected || ' but the row is at ' || t.version
             WHEN NOT MATCHED AND s.expected = 0 AND s.document IS NOT NULL THEN
                  INSERT (key, document, version, timestamp) VALUES (s.key, s.document, s.version, s.timestamp)
             WHEN NOT MATCHED THEN
                  ERROR 'key ' || s.key || ' expected version ' || s.expected || ' but no row exists'
            """;

        command.Parameters.Add(new DuckDBParameter("keys", keys));
        command.Parameters.Add(new DuckDBParameter("documents", documents));
        command.Parameters.Add(new DuckDBParameter("expected", expected));
        command.Parameters.Add(new DuckDBParameter("versions", versions));
        command.Parameters.Add(new DuckDBParameter("timestamp", _time.GetUtcNow()));

        try {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DuckDBException ex) when (IsVersionMismatch(ex)) {
            throw new DocumentVersionMismatchException(
                $"{request.Tracked.Length} change(s) to {DocumentCollection.For<TDocument>()} were rejected: {ex.Message}", ex);
        }
    }

    ValueTask<DuckDBAdvancedConnection> Connection(CancellationToken cancellationToken) {
        if (_transaction is null)
            throw new InvalidOperationException(
                $"{nameof(DuckDBDocumentStore)} was not bound to a transaction. Create it through a "
              + "projection that binds it when the backend wires the projection, or its writes land "
              + "outside that transaction and survive a rollback.");

        return _transaction.Open(cancellationToken);
    }

    JsonTypeInfo<TDocument> TypeInfo<TDocument>() =>
        (JsonTypeInfo<TDocument>)_json.GetTypeInfo(typeof(TDocument));

    static bool IsVersionMismatch(DuckDBException exception) =>
        exception.Message.Contains("Merge error condition", StringComparison.Ordinal);

    static string KeyText<TKey>(TKey key) where TKey : notnull =>
        key is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : key.ToString()!;
}
