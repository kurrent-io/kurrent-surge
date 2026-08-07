using System.Globalization;
using System.Runtime.CompilerServices;

using DuckDB.NET.Data;

using Kurrent.Projectors.Transactional;
using Kurrent.Quack;

namespace Kurrent.Projectors.DuckDB.Examples;

public class SchemaVersionQuackProjection : DocumentProjection<DuckDBAdvancedConnection, SchemaVersion, string> {
    public SchemaVersionQuackProjection(SchemaVersionQuackStore store) : base(store) {
        Setup((_, cancellationToken) => store.EnsureCollection(cancellationToken));

        Project<SchemaCreated>(
            msg => msg.SchemaVersionId,
            (msg, _, _) => new SchemaVersion {
                VersionId        = msg.SchemaVersionId,
                SchemaName       = msg.SchemaName,
                VersionNumber    = msg.VersionNumber,
                SchemaDefinition = msg.SchemaDefinition
            });

        Project<SchemaVersionRegistered>(
            msg => msg.SchemaVersionId,
            (msg, current, _) => current is null
                ? null
                : current with { SchemaName = msg.SchemaName, VersionNumber = msg.VersionNumber });

        Project<SchemaVersionDeleted>(
            msg => msg.SchemaVersionId,
            (_, _, _) => null);
    }
}

public sealed class SchemaVersionQuackStore(
    DuckDBProjectionTransaction transaction,
    TimeProvider? time = null
) : IDocumentStore {
    readonly TimeProvider _time = time ?? TimeProvider.System;

    public async ValueTask EnsureCollection(CancellationToken cancellationToken) {
        var connection = await transaction.Open(cancellationToken).ConfigureAwait(false);

        connection.ExecuteNonQuery<EnsureTableStatement>();
    }

    public async IAsyncEnumerable<StoredDocument<TDocument, TKey>> Load<TDocument, TKey>(
        LoadRequest<TKey> request, [EnumeratorCancellation] CancellationToken cancellationToken
    ) where TDocument : class where TKey : notnull {
        if (request.Keys.Length == 0)
            yield break;

        var connection = await transaction.Open(cancellationToken).ConfigureAwait(false);

        var requested = new Dictionary<string, TKey>(request.Keys.Length, StringComparer.Ordinal);

        foreach (var key in request.Keys)
            requested[KeyText(key)] = key;

        var rows = new List<StoredRow>(request.Keys.Length);

        foreach (ref readonly var row in connection.ExecuteQuery<LoadArgs, StoredRow, LoadByKeys>(new([.. requested.Keys])))
            rows.Add(row);

        foreach (var row in rows)
            yield return new(
                requested[row.VersionId],
                (TDocument)(object)row.Document,
                row.Version,
                row.Timestamp);
    }

    public async ValueTask Save<TDocument, TKey>(
        SaveRequest<TDocument, TKey> request, CancellationToken cancellationToken
    ) where TDocument : class where TKey : notnull {
        if (request.Tracked.Length == 0)
            return;

        var connection = await transaction.Open(cancellationToken).ConfigureAwait(false);

        var count = request.Tracked.Length;

        var keys     = new string[count];
        var names    = new string?[count];
        var numbers  = new string?[count];
        var expected = new string[count];
        var versions = new string[count];

        var bufferLength = 0;

        foreach (var (_, _, _, document) in request.Tracked)
            bufferLength += ((SchemaVersion?)(object?)document)?.SchemaDefinition.Length ?? 0;

        var definitions = new byte[bufferLength];
        var ranges      = new Range?[count];
        var offset      = 0;

        for (var index = 0; index < count; index++) {
            var (key, expectedVersion, version, document) = request.Tracked[index];
            var schemaVersion = (SchemaVersion?)(object?)document;

            keys[index]     = KeyText(key);
            names[index]    = schemaVersion?.SchemaName;
            numbers[index]  = schemaVersion?.VersionNumber.ToString(CultureInfo.InvariantCulture);
            expected[index] = expectedVersion.ToString(CultureInfo.InvariantCulture);
            versions[index] = version.ToString(CultureInfo.InvariantCulture);

            if (schemaVersion is null) {
                ranges[index] = null;
                continue;
            }

            schemaVersion.SchemaDefinition.CopyTo(definitions, offset);
            ranges[index] = new Range(offset, offset + schemaVersion.SchemaDefinition.Length);
            offset += schemaVersion.SchemaDefinition.Length;
        }

        try {
            connection.ExecuteNonQuery<SaveArgs, SaveStatement>(
                new(keys, names, numbers, expected, versions, _time.GetUtcNow().UtcDateTime, definitions, ranges));
        }
        catch (DuckDBException ex) when (ex.Message.Contains("Merge error condition", StringComparison.Ordinal)) {
            throw new DocumentVersionMismatchException(
                $"{count} change(s) to schema_versions_quack were rejected: {ex.Message}", ex);
        }
    }

    static string KeyText<TKey>(TKey key) where TKey : notnull =>
        key is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : key.ToString()!;

    readonly record struct StoredRow(string VersionId, SchemaVersion Document, long Version, DateTimeOffset Timestamp);

    struct EnsureTableStatement : IParameterlessStatement {
        public static ReadOnlySpan<byte> CommandText => """
            CREATE TABLE IF NOT EXISTS schema_versions_quack (
                version_id        TEXT      NOT NULL,
                schema_name       TEXT      NOT NULL,
                version_number    INT       NOT NULL,
                schema_definition BLOB      NOT NULL,
                version           BIGINT    NOT NULL,
                timestamp         TIMESTAMP NOT NULL,
                PRIMARY KEY (version_id)
            );
            """u8;
    }

    readonly record struct LoadArgs(string[] VersionIds);

    struct LoadByKeys : IQuery<LoadArgs, StoredRow> {
        public static StatementBindingResult Bind(in LoadArgs args, PreparedStatement statement) =>
            new(statement) { args.VersionIds };

        public static ReadOnlySpan<byte> CommandText => """
            SELECT version_id, schema_name, version_number, schema_definition, version, timestamp
            FROM schema_versions_quack
            WHERE version_id IN (SELECT unnest($1::VARCHAR[]));
            """u8;

        public static StoredRow Parse(ref DataChunk.Row row) {
            var versionId = row.ReadString();

            var document = new SchemaVersion {
                VersionId        = versionId,
                SchemaName       = row.ReadString(),
                VersionNumber    = row.ReadInt32(),
                SchemaDefinition = row.ReadBlob().Reference.AsSpan().ToArray()
            };

            var version = row.ReadInt64();

            var timestamp = new DateTimeOffset(DateTime.SpecifyKind(row.ReadDateTime(), DateTimeKind.Utc));

            return new(versionId, document, version, timestamp);
        }
    }

    readonly record struct SaveArgs(
        string[]  VersionIds,
        string?[] SchemaNames,
        string?[] VersionNumbers,
        string[]  ExpectedVersions,
        string[]  Versions,
        DateTime  Timestamp,
        byte[]    DefinitionsBuffer,
        Range?[]  DefinitionRanges
    );

    struct SaveStatement : IPreparedStatement<SaveArgs> {
        public static StatementBindingResult Bind(in SaveArgs args, PreparedStatement statement) {
            var result = new StatementBindingResult(statement) {
                args.VersionIds,
                args.SchemaNames,
                args.VersionNumbers,
                args.ExpectedVersions,
                args.Versions,
                args.Timestamp
            };

            statement.BindList(6, args.DefinitionsBuffer, args.DefinitionRanges);

            return result;
        }

        public static ReadOnlySpan<byte> CommandText => """
            MERGE INTO schema_versions_quack AS t
            USING (
                SELECT unnest($1::VARCHAR[])          AS version_id,
                       unnest($2::VARCHAR[])          AS schema_name,
                       unnest($3::VARCHAR[])::INTEGER AS version_number,
                       unnest($4::VARCHAR[])::BIGINT  AS expected,
                       unnest($5::VARCHAR[])::BIGINT  AS version,
                       $6                             AS timestamp,
                       unnest($7)                     AS schema_definition
            ) AS s
               ON t.version_id = s.version_id
             WHEN MATCHED AND t.version = s.expected AND s.schema_name IS NULL THEN
                  DELETE
             WHEN MATCHED AND t.version = s.expected THEN
                  UPDATE SET schema_name       = s.schema_name,
                             version_number    = s.version_number,
                             schema_definition = s.schema_definition,
                             version           = s.version,
                             timestamp         = s.timestamp
             WHEN MATCHED THEN
                  ERROR 'key ' || s.version_id || ' expected version ' || s.expected || ' but the row is at ' || t.version
             WHEN NOT MATCHED AND s.expected = 0 AND s.schema_name IS NOT NULL THEN
                  INSERT (version_id, schema_name, version_number, schema_definition, version, timestamp)
                  VALUES (s.version_id, s.schema_name, s.version_number, s.schema_definition, s.version, s.timestamp)
             WHEN NOT MATCHED THEN
                  ERROR 'key ' || s.version_id || ' expected version ' || s.expected || ' but no row exists';
            """u8;
    }
}
