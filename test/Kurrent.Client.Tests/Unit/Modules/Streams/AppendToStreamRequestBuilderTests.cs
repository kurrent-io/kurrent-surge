using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Modules.Streams;

[Category("Streams")]
public class AppendStreamRequestBuilderTests {
    static AppendRecord ValidRecord(string stream = "test-stream") =>
        AppendRecord.New.WithValue("payload").ForStream(stream).Build();

    #region Build Validation

    [Test]
    public void build_throws_when_no_records_added() {
        // Arrange
        var builder = AppendStreamRequest.For("test-stream");

        // Act & Assert
        Assert.Throws<Exception>(() => builder.Build());
    }

    [Test]
    public async ValueTask request_stream_matches_builder_stream() {
        // Arrange & Act
        var request = AppendStreamRequest.For("my-stream")
            .WithRecord(ValidRecord())
            .Build();

        // Assert
        await Assert.That(request.Stream).IsEqualTo((StreamName)"my-stream");
    }

    #endregion

    #region Stream Stamping

    [Test]
    public async ValueTask build_stamps_stream_on_all_records() {
        // Arrange
        var r1 = ValidRecord("other-stream");
        var r2 = ValidRecord("another-stream");

        // Act
        var request = AppendStreamRequest.For("target-stream")
            .WithRecord(r1)
            .WithRecord(r2)
            .Build();

        // Assert
        await Assert.That(request.Records[0].Stream).IsEqualTo((StreamName)"target-stream");
        await Assert.That(request.Records[1].Stream).IsEqualTo((StreamName)"target-stream");
    }

    #endregion

    #region Expected State / Consistency Checks

    [Test]
    public async ValueTask build_produces_stream_state_check_from_expecting_state() {
        // Arrange & Act
        var request = AppendStreamRequest.For("test-stream")
            .ExpectingState(ExpectedStreamState.Exists)
            .WithRecord(ValidRecord())
            .Build();

        // Assert
        await Assert.That(request.Checks).Count().IsEqualTo(1);

        var check = request.Checks[0].AsStreamStateCheck;
        await Assert.That(check.Stream).IsEqualTo((StreamName)"test-stream");
        await Assert.That(check.ExpectedState).IsEqualTo(ExpectedStreamState.Exists);
    }

    [Test]
    public async ValueTask build_includes_additional_checks_alongside_expected_state() {
        // Arrange
        var extraCheck = ConsistencyCheck.Stream("other-stream").Exists();

        // Act
        var request = AppendStreamRequest.For("test-stream")
            .ExpectingState(ExpectedStreamState.Exists)
            .WithCheck(extraCheck)
            .WithRecord(ValidRecord())
            .Build();

        // Assert
        await Assert.That(request.Checks).Count().IsEqualTo(2);
    }

    [Test]
    public async ValueTask build_without_expecting_state_has_no_state_checks() {
        // Arrange & Act
        var request = AppendStreamRequest.For("test-stream")
            .WithRecord(ValidRecord())
            .Build();

        // Assert
        await Assert.That(request.Checks).IsEmpty();
    }

    #endregion

    #region Batch Data Format Override

    [Test]
    public async ValueTask with_data_format_overrides_all_records() {
        // Arrange
        var r1 = AppendRecord.New.WithValue("a").ForStream("s").WithDataFormat(SchemaDataFormat.Json).Build();
        var r2 = AppendRecord.New.WithValue("b").ForStream("s").WithDataFormat(SchemaDataFormat.Json).Build();

        // Act
        var request = AppendStreamRequest.For("test-stream")
            .WithDataFormat(SchemaDataFormat.Protobuf)
            .WithRecord(r1)
            .WithRecord(r2)
            .Build();

        // Assert
        await Assert.That(request.Records[0].DataFormat).IsEqualTo(SchemaDataFormat.Protobuf);
        await Assert.That(request.Records[1].DataFormat).IsEqualTo(SchemaDataFormat.Protobuf);
    }

    [Test]
    public async ValueTask preserves_record_data_format_when_no_override() {
        // Arrange
        var record = AppendRecord.New
            .WithValue("payload")
            .ForStream("test-stream")
            .WithDataFormat(SchemaDataFormat.Protobuf)
            .Build();

        // Act
        var request = AppendStreamRequest.For("test-stream")
            .WithRecord(record)
            .Build();

        // Assert
        await Assert.That(request.Records[0].DataFormat).IsEqualTo(SchemaDataFormat.Protobuf);
    }

    #endregion

    #region Batch Metadata Override

    [Test]
    public async ValueTask with_metadata_upserts_on_all_records() {
        // Arrange
        var expectedEntry = new KeyValuePair<string, object?>("source", "batch");

        var builder = AppendStreamRequest.For("test-stream")
            .WithMetadata(expectedEntry)
            .WithRecord(ValidRecord())
            .WithRecord(ValidRecord());

        // Act
        var request = builder.Build();

        // Assert
        await Assert
            .That(request.Records)
            .All(r => r.Metadata.Contains(expectedEntry));
    }

    [Test]
    public async ValueTask with_metadata_overrides_existing_key() {
        // Arrange
        var record = AppendRecord.New
            .ForStream("test-stream")
            .WithValue("payload")
            .WithMetadata("source", "record-level")
            .Build();

        var builder = AppendStreamRequest.For("test-stream")
            .WithMetadata("source", "batch-level")
            .WithRecord(record);

        // Act
        var request = builder.Build();

        // Assert
        await Assert.That(request.Records[0].Metadata["source"]).IsEqualTo("batch-level");
    }

    #endregion

    #region WithRecord Overloads

    [Test]
    public async ValueTask with_record_action_overload() {
        // Act
        var request = AppendStreamRequest.For("test-stream")
            .WithRecord(x => x.WithValue("from-action"))
            .Build();

        // Assert
        await Assert.That(request.Records).Count().IsEqualTo(1);
        await Assert.That(request.Records[0].Value).IsEqualTo("from-action");
        await Assert.That(request.Records[0].Stream).IsEqualTo((StreamName)"test-stream");
    }

    [Test]
    public async ValueTask with_record_value_overload() {
        // Act
        var request = AppendStreamRequest.For("test-stream")
            .WithRecord("simple-value")
            .Build();

        // Assert
        await Assert.That(request.Records).Count().IsEqualTo(1);
        await Assert.That(request.Records[0].Value).IsEqualTo("simple-value");
        await Assert.That(request.Records[0].DataFormat).IsEqualTo(SchemaDataFormat.Json);
        await Assert.That(request.Records[0].Stream).IsEqualTo((StreamName)"test-stream");
    }

    [Test]
    public async ValueTask with_records_params_overload() {
        // Arrange
        var r1 = ValidRecord();
        var r2 = ValidRecord();
        var r3 = ValidRecord();

        // Act
        var request = AppendStreamRequest.For("test-stream")
            .WithRecords(r1, r2, r3)
            .Build();

        // Assert
        await Assert.That(request.Records).Count().IsEqualTo(3);
    }

    [Test]
    public async ValueTask with_records_enumerable_overload() {
        // Arrange
        var records = new List<AppendRecord> { ValidRecord(), ValidRecord() };

        // Act
        var request = AppendStreamRequest.For("test-stream")
            .WithRecords(records)
            .Build();

        // Assert
        await Assert.That(request.Records).Count().IsEqualTo(2);
    }

    #endregion
}
