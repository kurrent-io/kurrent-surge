using System.Collections.Immutable;
using Kurrent.Validation;

namespace Kurrent.Client.Streams;

[Category("Streams")]
public class AppendRecordsRequestTests {
    [Test]
    public async ValueTask ensure_valid_throws_when_record_has_no_stream() {
        var request = new AppendRecordsRequest(
            [AppendRecord.New.WithValue("event").Build()],
            []
        );

        await Assert.That(request.EnsureValid).Throws<InvalidOperationException>();
    }

    [Test]
    public async ValueTask ensure_valid_throws_when_a_record_is_invalid() {
        var invalidRecord = new AppendRecord {
            RecordId   = Guid.Empty,
            Value      = null!,
            DataFormat = SchemaDataFormat.Unspecified,
            Stream     = "some-stream"
        };

        var request = new AppendRecordsRequest([invalidRecord], []);

        await Assert.That(request.EnsureValid).Throws<ValidationException>();
    }
}
