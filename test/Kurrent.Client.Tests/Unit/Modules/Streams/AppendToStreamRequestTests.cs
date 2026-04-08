using Kurrent.Validation;

namespace Kurrent.Client.Streams;

[Category("Streams")]
public class AppendStreamRequestTests {
    static readonly StreamName TestStream = $"test-stream-{Guid.NewGuid():N}";

    [Test]
    public async ValueTask ensure_valid_stamps_stream_on_all_records() {
        var request = new AppendStreamRequest(
            TestStream,
            [AppendRecord.New.WithValue("event-1").Build(), AppendRecord.New.WithValue("event-2").Build()]
        );

        var validated = request.EnsureValid();

        await Assert.That(validated.Records[0].Stream).IsEqualTo(TestStream);
        await Assert.That(validated.Records[1].Stream).IsEqualTo(TestStream);
    }

    [Test]
    public async ValueTask ensure_valid_throws_when_a_record_is_invalid() {
        var invalidRecord = new AppendRecord {
            RecordId   = Guid.Empty,
            Value      = null!,
            DataFormat = SchemaDataFormat.Unspecified
        };

        var request = new AppendStreamRequest(TestStream, [invalidRecord]);

        var vex = await Assert.That(request.EnsureValid).Throws<ValidationException>();
    }
}
