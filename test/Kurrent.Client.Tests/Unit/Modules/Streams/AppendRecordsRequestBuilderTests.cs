using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Modules.Streams;

[Category("Streams")]
public class AppendRecordsRequestBuilderTests {
    static AppendRecord ValidRecord(string stream = "test-stream") =>
        AppendRecord.New().Value("payload").Stream(stream).Build();

    #region ->> Build Validation <<-

    [Test]
    public void build_throws_when_no_records_added() {
        // Arrange
        var builder = AppendRecordsRequest.New();

        // Act & Assert
        Assert.Throws<Exception>(() => builder.Build());
    }

    [Test]
    public void build_validates_all_records_have_streams() {
        // Arrange
        var builder = AppendRecordsRequest.New()
            .AddRecord(b => b.Value("no-stream-assigned"));

        // Act & Assert
        Assert.Throws<Exception>(() => builder.Build());
    }

    [Test]
    public async ValueTask build_succeeds_when_all_records_have_streams() {
        // Arrange & Act
        var request = AppendRecordsRequest.New()
            .AddRecord(ValidRecord("stream-a"))
            .AddRecord(ValidRecord("stream-b"))
            .Build();

        // Assert
        await Assert.That(request.Records).Count().IsEqualTo(2);
        await Assert.That(request.Records[0].Stream).IsEqualTo((StreamName)"stream-a");
        await Assert.That(request.Records[1].Stream).IsEqualTo((StreamName)"stream-b");
    }

    #endregion

    #region ->> Consistency Checks <<-

    [Test]
    public async ValueTask with_check_adds_consistency_check() {
        // Arrange
        var check = ConsistencyCheck.StreamExists("my-stream");

        // Act
        var request = AppendRecordsRequest.New()
            .AddCheck(check)
            .AddRecord(ValidRecord())
            .Build();

        // Assert
        await Assert.That(request.Checks).Count().IsEqualTo(1);

        var streamCheck = request.Checks[0].AsStreamStateCheck;
        await Assert.That(streamCheck.Stream).IsEqualTo((StreamName)"my-stream");
        await Assert.That(streamCheck.ExpectedState).IsEqualTo(ExpectedStreamState.Exists);
    }

    [Test]
    public async ValueTask with_checks_adds_multiple_consistency_checks() {
        // Arrange
        var check1 = ConsistencyCheck.StreamExists("stream-a");
        var check2 = ConsistencyCheck.StreamDoesNotExist("stream-b");

        // Act
        var request = AppendRecordsRequest.New()
            .AddChecks(check1, check2)
            .AddRecord(ValidRecord())
            .Build();

        // Assert
        await Assert.That(request.Checks).Count().IsEqualTo(2);
    }

    #endregion

    #region ->> Batch Overrides <<-

    [Test]
    public async ValueTask with_data_format_overrides_all_records() {
        // Arrange
        var r1 = ValidRecord("stream-a");
        var r2 = ValidRecord("stream-b");

        // Act
        var request = AppendRecordsRequest.New()
            .DataFormat(SchemaDataFormat.Protobuf)
            .AddRecord(r1)
            .AddRecord(r2)
            .Build();

        // Assert
        await Assert.That(request.Records[0].DataFormat).IsEqualTo(SchemaDataFormat.Protobuf);
        await Assert.That(request.Records[1].DataFormat).IsEqualTo(SchemaDataFormat.Protobuf);
    }

    [Test]
    public async ValueTask with_metadata_upserts_on_all_records() {
        // Arrange
        var r1 = ValidRecord("stream-a");
        var r2 = ValidRecord("stream-b");

        // Act
        var request = AppendRecordsRequest.New()
            .AddMetadata("source", "batch")
            .AddRecord(r1)
            .AddRecord(r2)
            .Build();

        // Assert
        await Assert.That(request.Records[0].Metadata["source"]).IsEqualTo("batch");
        await Assert.That(request.Records[1].Metadata["source"]).IsEqualTo("batch");
    }

    #endregion

    #region ->> WithRecord Overloads <<-

    [Test]
    public async ValueTask with_record_action_overload() {
        // Act
        var request = AppendRecordsRequest.New()
            .AddRecord(b => b.Value("from-action").Stream("test-stream"))
            .Build();

        // Assert
        await Assert.That(request.Records).Count().IsEqualTo(1);
        await Assert.That(request.Records[0].Value).IsEqualTo("from-action");
        await Assert.That(request.Records[0].Stream).IsEqualTo((StreamName)"test-stream");
    }

    [Test]
    public async ValueTask with_records_params_overload() {
        // Arrange
        var r1 = ValidRecord("stream-a");
        var r2 = ValidRecord("stream-b");
        var r3 = ValidRecord("stream-c");

        // Act
        var request = AppendRecordsRequest.New()
            .AddRecords(r1, r2, r3)
            .Build();

        // Assert
        await Assert.That(request.Records).Count().IsEqualTo(3);
    }

    #endregion
}
