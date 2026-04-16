using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Modules.Streams;

[Category("Streams")]
public class CachedValidationTests {
    static AppendRecord ValidRecord(string stream = "test-stream") =>
        AppendRecord.New.WithValue("payload").ForStream(stream).Build();

    #region AppendRecord

    [Test]
    public async ValueTask record_validate_returns_cached_result() {
        var record = ValidRecord();

        var r1 = record.Validate();
        var r2 = record.Validate();

        await Assert.That(ReferenceEquals(r1, r2)).IsTrue();
    }

    [Test]
    public async ValueTask record_with_invalidates_cache() {
        var record = ValidRecord();
        await Assert.That(record.Validate().IsSuccess).IsTrue();

        var bad = record with { RecordId = Guid.Empty };
        await Assert.That(bad.Validate().IsSuccess).IsFalse();
    }

    [Test]
    public async ValueTask record_validate_caches_failure_result() {
        var bad = new AppendRecord {
            RecordId   = Guid.Empty,
            Value      = null!,
            DataFormat = SchemaDataFormat.Unspecified,
            Stream     = "test-stream"
        };

        var r1 = bad.Validate();
        var r2 = bad.Validate();

        await Assert.That(r1.IsSuccess).IsFalse();
        await Assert.That(ReferenceEquals(r1, r2)).IsTrue();
    }

    #endregion

    #region AppendRecordsRequest

    [Test]
    public async ValueTask request_validate_returns_cached_result() {
        var request = new AppendRecordsRequest(
            [ValidRecord("stream-a"), ValidRecord("stream-b")],
            []
        );

        var r1 = request.Validate();
        var r2 = request.Validate();

        await Assert.That(r1.IsSuccess).IsTrue();
        await Assert.That(ReferenceEquals(r1, r2)).IsTrue();
    }

    #endregion

    #region AppendStreamRequest

    [Test]
    public async ValueTask stream_request_validate_returns_cached_result() {
        var request = new AppendStreamRequest(
            "test-stream",
            [ValidRecord()],
            ExpectedStreamState.Any
        );

        var r1 = request.Validate();
        var r2 = request.Validate();

        await Assert.That(r1.IsSuccess).IsTrue();
        await Assert.That(ReferenceEquals(r1, r2)).IsTrue();
    }

    #endregion
}
