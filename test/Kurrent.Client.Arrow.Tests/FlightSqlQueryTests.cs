using Apache.Arrow.Adbc.Client;
using Apache.Arrow.Adbc.Drivers.FlightSql;

namespace Kurrent.Client.Arrow.Tests;

[Category("Arrow")]
[Category("Integration")]
public class FlightSqlQueryTests {
    const int TestTimeoutMs = 30_000;

    [ClassDataSource<KurrentDBInsecureFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBInsecureFixture Fixture { get; [UsedImplicitly] init; }

    // The FlightSQL endpoint shares the KurrentDB gRPC port. The .NET ADBC driver passes the
    // address straight to Grpc.Net.Client.GrpcChannel.ForAddress, which only resolves http/https
    // — NOT the grpc:// scheme shown in the Python/Java docs. So translate the fixture string:
    //   kurrentdb://admin:changeit@host:port?tls=false  ->  http://host:port   (insecure)
    string ServerAddress {
        get {
            var uri = new Uri(Fixture.ConnectionString);
            return $"http://{uri.Host}:{uri.Port}";
        }
    }

    [Test, Timeout(TestTimeoutMs)]
    public async ValueTask raw_adbc_reads_seeded_records(CancellationToken ct) {
        // Arrange
        var seeded = await Fixture.SeedTestStream(5, ct);
        var stream = seeded.Stream.ToString();
        var sql    = $"SELECT stream FROM kdb.records WHERE stream = '{stream}'";

        using var driver = new FlightSqlDriver();

        // Act — the DuckDB secondary index is eventually consistent, so retry while empty.
        // Connection/auth errors are NOT swallowed; only an empty result is retried.
        var streams = await ReadUntilNonEmpty(async () => {
            await using var connection = driver.CreateConnection(ServerAddress);
            await connection.OpenAsync(ct);

            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            await using var reader = await command.ExecuteReaderAsync(ct);

            var rows = new List<string>();
            while (await reader.ReadAsync(ct))
                rows.Add(reader["stream"]?.ToString() ?? "");

            return rows;
        }, ct);

        // Assert
        await Assert.That(streams).Count().IsEqualTo(seeded.Records.Length);
        await Assert.That(streams.All(s => s == stream)).IsTrue();
    }

    [Test, Timeout(TestTimeoutMs)]
    public async ValueTask queries_abstraction_reads_seeded_records(CancellationToken ct) {
        // Arrange
        var seeded = await Fixture.SeedTestStream(5, ct);
        var stream = seeded.Stream.ToString();
        var sql    = $"SELECT stream FROM kdb.records WHERE stream = '{stream}'";

        await using var queries = new Queries(Fixture.ConnectionString);

        // Act — Query maps rows to the SDK Record; project the stream name back out.
        var streams = await ReadUntilNonEmpty(
            async () => await queries.Query(sql).Select(r => r.Stream.ToString()).ToListAsync(ct),
            ct);

        // Assert
        await Assert.That(streams).Count().IsEqualTo(seeded.Records.Length);
        await Assert.That(streams.All(s => s == stream)).IsTrue();
    }

    static async Task<List<string>> ReadUntilNonEmpty(Func<Task<List<string>>> read, CancellationToken ct) {
        while (true) {
            ct.ThrowIfCancellationRequested();

            var rows = await read();
            if (rows.Count > 0)
                return rows;

            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        }
    }
}
