using Kurrent.Client.Streams;
using Kurrent.Validation;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
public class AppendStreamRequestTests {
    static readonly StreamName TestStream = $"test-stream-{Guid.NewGuid():N}";

    [Test]
    public async ValueTask ensure_valid_stamps_stream_on_all_records() {
        var request = new AppendStreamRequest(
            TestStream,
            [AppendRecord.New().Value("event-1").Build(), AppendRecord.New().Value("event-2").Build()],
            ExpectedStreamState.Any
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

        var request = new AppendStreamRequest(TestStream, [invalidRecord], ExpectedStreamState.Any);

        var vex = await Assert.That(request.EnsureValid).Throws<ValidationException>();
    }
}
