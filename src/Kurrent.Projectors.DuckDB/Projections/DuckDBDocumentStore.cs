using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using DuckDB.NET.Data;

using Kurrent.Projectors.Transactional;
using Kurrent.Quack;

namespace Kurrent.Projectors.DuckDB;

/// <summary>
/// The DuckDB <see cref="IDocumentStore"/>: one table per document type, one row per document, the document
/// itself as JSON.
/// </summary>
/// <remarks>
/// <para>
/// Every statement runs on the connection the projection's transaction is open on, so rows and the positions
/// describing them commit together. The transaction arrives through <see cref="Bind"/> rather than the
/// constructor because a projection builds its store before a backend has wired it.
/// </para>
/// <para>
/// Documents are serialized with System.Text.Json through
/// <see cref="Surge.Client.Schema.Serialization.Json.JsonSchemaSerializerOptions.DefaultJsonSerializerOptions"/>,
/// which resolves source-generated metadata first and falls back to reflection only where the link-time
/// switch leaves reflection enabled. Under Native AOT a document type that no registered
/// <c>JsonSerializerContext</c> covers throws on its first load or write — pass options carrying that
/// context instead of relying on the fallback.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class DuckDBDocumentStore(TimeProvider? time = null, JsonSerializerOptions? json = null) : IDocumentStore {
    readonly TimeProvider _time = time ?? TimeProvider.System;

    readonly JsonSerializerOptions _json =
        json ?? Surge.Client.Schema.Serialization.Json.JsonSchemaSerializerOptions.DefaultJsonSerializerOptions;

    IProjectionTransaction<DuckDBAdvancedConnection>? _transaction;

    /// <summary>Hands the store the transaction whose client every statement runs on.</summary>
    /// <remarks>
    /// Public for the same reason as <see cref="ProjectionProcessor{TClient}.Wire"/> — a backend lives in its own
    /// assembly — not because a user should call it.
    /// </remarks>
    public void Bind(IProjectionTransaction<DuckDBAdvancedConnection> transaction) =>
        _transaction = transaction;

    /// <summary>
    /// Creates the table for <typeparamref name="TDocument"/> if it does not exist. Call it from a
    /// projection's <c>Setup</c>, which runs before the subscription opens.
    /// </summary>
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

    /// <inheritdoc/>
    public async IAsyncEnumerable<StoredDocument<TDocument, TKey>> Load<TDocument, TKey>(
        LoadRequest<TKey> request, [EnumeratorCancellation] CancellationToken cancellationToken
    ) where TDocument : class where TKey : notnull {
        if (request.Keys.Length == 0)
            yield break;

        // Keys reach SQL as text and cannot be parsed back generically, so the row's key text is mapped to
        // the caller's own key rather than reconstructed from the column.
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

            // TIMESTAMPTZ reads as an Unspecified-kind DateTime through GetValue or GetDateTime, which
            // drops the offset and shifts the instant. GetFieldValue<DateTimeOffset> returns it exactly.
            yield return new(
                requested[reader.GetString(0)],
                document,
                reader.GetInt64(2),
                reader.GetFieldValue<DateTimeOffset>(3));
        }
    }

    /// <inheritdoc/>
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

        // One statement for inserts, updates and removals, each guarded on its own version. A null document
        // marks a removal. The guarded branches must precede their catch-all ERROR or the error swallows
        // every write. Numbers travel as VARCHAR lists and are cast back, because list binding covers
        // strings and blobs only.
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
              + "outside the epoch and survive a rollback.");

        // Idempotent while the transaction is open, so this returns the client the projection is already
        // writing through rather than opening a second transaction on top of it.
        return _transaction.Open(cancellationToken);
    }

    JsonTypeInfo<TDocument> TypeInfo<TDocument>() =>
        (JsonTypeInfo<TDocument>)_json.GetTypeInfo(typeof(TDocument));

    // The MERGE's ERROR branches are the only signal a version guard rejected the batch, and DuckDB reports
    // them as a Constraint Error carrying the branch that fired. The prefix is not a documented contract, so
    // it is matched to classify and never parsed for values — the message already reads well enough to log.
    static bool IsVersionMismatch(DuckDBException exception) =>
        exception.Message.Contains("Merge error condition", StringComparison.Ordinal);

    // Keys reach SQL as text, so the formatting has to be culture-independent: a Guid, an int or a
    // DateOnly key written under one culture must read back under any other.
    static string KeyText<TKey>(TKey key) where TKey : notnull =>
        key is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : key.ToString()!;
}
