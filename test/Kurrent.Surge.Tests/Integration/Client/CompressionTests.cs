using System.Text;
using Grpc.Core;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Tests.Client;

[Category("Integration")]
public class CompressionTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask a_large_compressible_record_round_trips_correctly_against_the_real_server(CancellationToken ct) {
        // Arrange: a real, gzip-enabled client against the real KurrentDB fixture — the one
        // thing the unit tests can't prove: that the server actually accepts and decodes a
        // message compressed with this exact algorithm. A wrong wire encoding name (the Brotli
        // bug fixed this branch) fails the RPC or corrupts the payload here, not there. Gzip is
        // the only algorithm the server currently implements — see CompressionAlgorithm's remarks.
        await using var client = Fixture.GetNewClient(opts => opts.Compression = new() {
            Enabled   = true,
            Algorithm = CompressionAlgorithm.Gzip
        });

        var stream = (await Fixture.TestData.GenerateSet(1, ct)).Stream;

        // Repetition makes the payload genuinely compressible instead of near-incompressible
        // random bytes, so the codec does real work instead of just round-tripping a no-op.
        var payload = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(Fixture.Faker.Lorem.Paragraphs(3), 50)));

        var record = AppendRecord.Create(payload, r => {
            r.Stream     = stream;
            r.DataFormat = SchemaDataFormat.Bytes;
            r.SchemaName = $"compressed-payload-{TestId.New()}";
        });

        // Act
        var appendResult = await client.Streams.AppendRecords([record], ct);

        var read = await client.Streams
            .ReadStream(stream, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        // Assert
        await Assert.That(appendResult.Position).IsGreaterThan(LogPosition.Earliest);
        await Assert.That(read).Count().IsEqualTo(1);
        await Assert.That(read[0].Data.ToArray().SequenceEqual(payload)).IsTrue();
    }

    [Test]
    [Arguments(CompressionAlgorithm.Deflate)]
    [Arguments(CompressionAlgorithm.Brotli)]
    public async ValueTask unsupported_algorithms_are_rejected_by_the_real_server(CompressionAlgorithm algorithm, CancellationToken ct) {
        // Arrange: pins the current, confirmed server limitation as an executable fact instead
        // of a comment that can silently go stale. If the server ever adds support for this
        // algorithm, this test starts failing (the call succeeds instead of throwing) — that's
        // the signal to update CompressionAlgorithm's remarks and this test.
        await using var client = Fixture.GetNewClient(opts => opts.Compression = new() {
            Enabled   = true,
            Algorithm = algorithm
        });

        var stream = (await Fixture.TestData.GenerateSet(1, ct)).Stream;

        var record = AppendRecord.Create(Fixture.Faker.Random.Bytes(32), r => {
            r.Stream     = stream;
            r.DataFormat = SchemaDataFormat.Bytes;
            r.SchemaName = $"unsupported-algorithm-{TestId.New()}";
        });

        // Act
        RpcException? caught = null;
        try {
            await client.Streams.AppendRecords([record], ct);
        }
        catch (RpcException ex) {
            caught = ex;
        }

        // Assert
        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.StatusCode).IsEqualTo(StatusCode.Unimplemented);
    }
}
