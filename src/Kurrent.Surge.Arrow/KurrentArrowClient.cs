using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Apache.Arrow.Adbc.Client;
using Apache.Arrow.Adbc.Drivers.FlightSql;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Arrow;

/// <summary>
/// Executes read-only SQL over a KurrentDB node via the Arrow Flight SQL protocol and streams the
/// results, composable with the standard <c>System.Linq</c> async operators
/// (<c>Where</c>/<c>Select</c>/<c>Take</c>/<c>FirstAsync</c>/…).
/// </summary>
/// <remarks>
/// Reads go through raw ADO.NET (<see cref="DbDataReader"/>): ADBC's <c>AdbcCommand</c> only accepts
/// <see cref="System.Data.CommandBehavior.Default"/>, whereas a micro-ORM like Dapper always requests
/// <c>SequentialAccess | SingleResult</c> and throws — and raw reads are the only AOT-safe option here.
/// <para>
/// Rows from <c>kdb.records</c> map onto the SDK's <see cref="Record"/> so queried records decode
/// (<see cref="Record.Decode"/>/<see cref="Record.As{T}"/>) exactly like records from reads and
/// subscriptions.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class KurrentArrowClient : IAsyncDisposable {
    readonly FlightSqlDriver      _driver = new();
    readonly KurrentArrowOptions  _options;
    readonly string               _serverAddress;
    readonly Lazy<IRecordDecoder> _decoder;

    KurrentClient? _ownedClient;

    KurrentArrowClient(KurrentArrowOptions options, IRecordDecoder? decoder) {
        _options       = options;
        _serverAddress = ResolveServerAddress(options);

        // Built on first use: Query<T> with an explicit mapper never touches the decoder, so a
        // client that only runs projections never pays for a KurrentClient.
        _decoder = new(() => decoder ?? OwnDecoder());

        IRecordDecoder OwnDecoder() {
            _ownedClient = KurrentClient.Create(options.ToClientOptions());
            return _ownedClient.ReadPipeline;
        }
    }

    /// <summary>
    /// Creates a client from a pre-built <see cref="KurrentArrowOptions"/> instance.
    /// </summary>
    public static KurrentArrowClient Create(KurrentArrowOptions options) => new(options, decoder: null);

    /// <summary>
    /// Creates a client from a KurrentDB connection string, optionally tuned before construction.
    /// </summary>
    public static KurrentArrowClient Create(string connectionString, Action<KurrentArrowOptions>? configure = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = KurrentArrowOptions.Parse(connectionString);

        configure?.Invoke(options);

        return Create(options);
    }

    /// <summary>
    /// Creates a client that decodes through an existing pipeline instead of building its own.
    /// </summary>
    internal static KurrentArrowClient Create(KurrentArrowOptions options, IRecordDecoder decoder) =>
        new(options, decoder);

    static string ResolveServerAddress(KurrentArrowOptions options) {
        var connection = options.ToClientOptions();

        // KurrentClientOptions.Address only names a real host:port for a single direct endpoint;
        // under discovery it returns a synthetic kurrentdb+discover:// URI that Grpc.Net.Client
        // cannot resolve. The ADBC driver builds its channel from a bare address with no resolver,
        // so a cluster connection has no node to address until the driver is replaced.
        if (connection.ConnectionScheme != KurrentConnectionScheme.Direct || connection.Endpoints.Length != 1)
            throw new InvalidOperationException(
                "Flight SQL requires a single node address. Use a direct kurrentdb:// connection string; discovery is not supported yet.");

        return connection.Address.GetLeftPart(UriPartial.Authority);
    }

    AdbcConnection CreateConnection() =>
        _driver.CreateConnection(
            _serverAddress,
            parameters => {
                if (BuildAuthorizationHeader(_options.ToClientOptions()) is { } authorization)
                    parameters[FlightSqlParameters.Authorization] = authorization;
            });

    // The driver forwards its "authorization" parameter verbatim as gRPC call metadata, so the value
    // is a complete HTTP Authorization header. Mirrors the header the SDK's own channel sends in
    // ClusterChannel — keep the two in step.
    static string? BuildAuthorizationHeader(KurrentClientOptions connection) =>
        connection.Security.Authentication.Value is BasicCredentials basic
            ? $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{basic.Username}:{basic.Password}"))}"
            : null;

    /// <summary>
    /// Runs a SELECT over <c>kdb.records</c> and maps each row to an SDK <see cref="Record"/>.
    /// Use <c>SELECT *</c> (or include the standard columns) so the full record is reconstructed.
    /// </summary>
    public async IAsyncEnumerable<Record> Query(
        string sql, [EnumeratorCancellation] CancellationToken ct = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        // ADBC's AdbcCommand only accepts CommandBehavior.Default (the implicit default).
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var columns = ColumnSet(reader);
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) {
            var record = MapRecord(reader, columns, _decoder.Value);

            // Same contract as the read path: the decoder always travels with the record and
            // SkipDecoding only defers materializing the payload.
            if (!_options.SkipDecoding)
                await record.TryDecode(ct).ConfigureAwait(false);

            yield return record;
        }
    }

    /// <summary>
    /// Runs <paramref name="sql"/> and projects each row with an explicit <paramref name="map"/>
    /// delegate — the AOT-safe equivalent of a generic <c>Query&lt;T&gt;</c> (no reflection).
    /// </summary>
    public async IAsyncEnumerable<T> Query<T>(
        string sql, Func<DbDataReader, T> map, [EnumeratorCancellation] CancellationToken ct = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            yield return map(reader);
    }

    static Record MapRecord(DbDataReader reader, HashSet<string> columns, IRecordDecoder decoder) {
        var schemaFormat = ParseFormat(Value(reader, columns, "schema_format")?.ToString());
        var schemaName   = Value(reader, columns, "schema_name")?.ToString();

        var position = RecordPosition.From(
            LogPosition.From(Value(reader, columns, "log_position")    is { } pos ? Convert.ToInt64(pos) : 0),
            StreamName.From(Value(reader, columns, "stream")?.ToString()),
            Revision.From(Value(reader, columns, "stream_revision")    is { } rev ? Convert.ToInt64(rev) : 0)
        );

        var timestamp = Value(reader, columns, "created_at") is { } ms
            ? DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(ms)).UtcDateTime
            : default;

        return new Record(decoder) {
            Id        = RecordId.From(Value(reader, columns, "record_id") is byte[] bytes ? new Guid(bytes) : Guid.Empty),
            Position  = position,
            Timestamp = timestamp,
            Schema    = new RecordSchemaInfo {
                // A partial projection may omit schema_name; SchemaName.From rejects empty, so fall back.
                Schema   = string.IsNullOrEmpty(schemaName) ? SchemaName.Unset : SchemaName.From(schemaName),
                DataFormat = schemaFormat
            },
            Data      = ToBytes(Value(reader, columns, "data")?.ToString(), schemaFormat),
            Metadata  = ToMetadata(Value(reader, columns, "metadata")?.ToString())
        };
    }

    // kdb.records stores JSON payloads as text and binary payloads as base64-encoded text, so the
    // original record bytes are reconstructed accordingly — leaving Record.Data identical to a read.
    static ReadOnlyMemory<byte> ToBytes(string? data, SchemaDataFormat format) =>
        data is null
            ? ReadOnlyMemory<byte>.Empty
            : format == SchemaDataFormat.Json
                ? Encoding.UTF8.GetBytes(data)
                : Convert.FromBase64String(data);

    static Metadata ToMetadata(string? json) {
        if (string.IsNullOrWhiteSpace(json) || JsonNode.Parse(json) is not JsonObject obj)
            return new Metadata();

        var metadata = new Metadata();
        foreach (var (key, node) in obj) {
            metadata = metadata.With(key, node switch {
                JsonValue v when v.TryGetValue<long>(out var l)   => l,
                JsonValue v when v.TryGetValue<double>(out var d) => d,
                JsonValue v when v.TryGetValue<bool>(out var b)   => b,
                JsonValue v when v.TryGetValue<string>(out var s) => s,
                null                                              => null,
                _                                                 => node.ToJsonString()
            });
        }

        return metadata;
    }

    static SchemaDataFormat ParseFormat(string? format) =>
        Enum.TryParse<SchemaDataFormat>(format, ignoreCase: true, out var parsed)
            ? parsed
            : SchemaDataFormat.Unspecified;

    static HashSet<string> ColumnSet(DbDataReader reader) {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
            columns.Add(reader.GetName(i));
        return columns;
    }

    // Returns the column value only when present in the projection and non-null, so mapping a partial
    // SELECT never throws on a missing/DBNull column.
    static object? Value(DbDataReader reader, HashSet<string> columns, string name) =>
        columns.Contains(name) && reader[name] is { } value and not DBNull ? value : null;

    public async ValueTask DisposeAsync() {
        _driver.Dispose();

        if (_ownedClient is { } client)
            await client.DisposeAsync().ConfigureAwait(false);
    }
}
