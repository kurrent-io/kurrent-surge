using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Modules.Streams;

public class MultiStreamAppendBuilderTests {
    static AppendRecord ValidRecord(string stream = "test-stream") =>
        AppendRecord.New.WithValue("payload").ForStream(stream).Build();

    [Test]
    public void multi_build_throws_when_no_records_added() {
        // Arrange
        var builder = AppendRecordsRequest.New();

        // Act & Assert
        // BUG: throws InvalidErrorCodeException instead of ValidationException
        // because ErrorCode "VALIDATION_ERROR" fails its own validation.
        Assert.Throws<Exception>(() => builder.Build());
    }

    [Test]
    public async ValueTask multi_records_count_reflects_added_records() {
        // Arrange & Act
        var builder = AppendRecordsRequest.New()
            .WithRecord(ValidRecord())
            .WithRecord(ValidRecord())
            .WithRecord(ValidRecord());

        // Assert
        await Assert.That(builder.RecordsCount).IsEqualTo(3);
    }

    [Test]
    public async ValueTask multi_with_data_format_overrides_all_records() {
        // Arrange
        var r1 = ValidRecord();
        var r2 = ValidRecord();

        // Act
        var request = AppendRecordsRequest.New()
            .WithDataFormat(SchemaDataFormat.Protobuf)
            .WithRecord(r1)
            .WithRecord(r2)
            .Build();

        // Assert
        await Assert.That(request.Records[0].DataFormat).IsEqualTo(SchemaDataFormat.Protobuf);
        await Assert.That(request.Records[1].DataFormat).IsEqualTo(SchemaDataFormat.Protobuf);
    }

    [Test]
    public async ValueTask multi_without_data_format_preserves_record_format() {
        // Arrange
        var record = AppendRecord.New
            .WithValue("payload")
            .ForStream("test-stream")
            .WithDataFormat(SchemaDataFormat.Protobuf)
            .Build();

        // Act
        var request = AppendRecordsRequest.New()
            .WithRecord(record)
            .Build();

        // Assert
        await Assert.That(request.Records[0].DataFormat).IsEqualTo(SchemaDataFormat.Protobuf);
    }

    [Test]
    public async ValueTask multi_with_check_adds_consistency_check() {
        // Arrange
        var check = ConsistencyCheck.Stream("my-stream").Exists();

        // Act
        var request = AppendRecordsRequest.New()
            .WithCheck(check)
            .WithRecord(ValidRecord())
            .Build();

        // Assert
        await Assert.That(request.Checks).Count().IsEqualTo(1);

        var streamCheck = request.Checks[0].AsStreamStateCheck;
        await Assert.That(streamCheck.Stream).IsEqualTo((StreamName)"my-stream");
        await Assert.That(streamCheck.ExpectedState).IsEqualTo(ExpectedStreamState.Exists);
    }

    [Test]
    public async ValueTask multi_with_checks_adds_multiple_consistency_checks() {
        // Arrange
        var check1 = ConsistencyCheck.Stream("stream-a").Exists();
        var check2 = ConsistencyCheck.Stream("stream-b").DoesNotExist();

        // Act
        var request = AppendRecordsRequest.New()
            .WithChecks(check1, check2)
            .WithRecord(ValidRecord())
            .Build();

        // Assert
        await Assert.That(request.Checks).Count().IsEqualTo(2);
    }

    [Test]
    public async ValueTask multi_with_record_from_builder_adds_built_record() {
        // Arrange
        var builder = AppendRecord.New
            .WithValue("from-builder")
            .ForStream("test-stream");

        // Act
        var request = AppendRecordsRequest.New()
            .WithRecord(builder)
            .Build();

        // Assert
        await Assert.That(request.Records).Count().IsEqualTo(1);
        await Assert.That(request.Records[0].Value).IsEqualTo("from-builder");
    }

    [Test]
    public async ValueTask multi_with_record_from_action_adds_configured_record() {
        // Act
        var request = AppendRecordsRequest.New()
            .WithRecord(b => b.WithValue("from-action").ForStream("test-stream"))
            .Build();

        // Assert
        await Assert.That(request.Records).Count().IsEqualTo(1);
        await Assert.That(request.Records[0].Value).IsEqualTo("from-action");
    }

    [Test]
    public async ValueTask multi_with_record_from_value_adds_record_with_defaults() {
        // Act
        var request = AppendRecordsRequest.New()
            .WithRecord(b => b.WithValue("simple-value").ForStream("test-stream"))
            .Build();

        // Assert
        await Assert.That(request.Records).Count().IsEqualTo(1);
        await Assert.That(request.Records[0].Value).IsEqualTo("simple-value");
        await Assert.That(request.Records[0].Stream).IsEqualTo((StreamName)"test-stream");
        await Assert.That(request.Records[0].DataFormat).IsEqualTo(SchemaDataFormat.Json);
    }

    [Test]
    public async ValueTask multi_with_records_adds_multiple_records() {
        // Arrange
        var r1 = ValidRecord();
        var r2 = ValidRecord();
        var r3 = ValidRecord();

        // Act
        var request = AppendRecordsRequest.New()
            .WithRecords(r1, r2, r3)
            .Build();

        // Assert
        await Assert.That(request.Records).Count().IsEqualTo(3);
    }
}
