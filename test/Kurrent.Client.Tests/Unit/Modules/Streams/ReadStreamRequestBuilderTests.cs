using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Modules.Streams;

[Category("Streams")]
public class ReadStreamRequestBuilderTests {
    #region ->> Default Values <<-

    [Test]
    public async ValueTask default_request_has_expected_defaults() {
        // Arrange & Act
        var request = new ReadStreamRequest();

        // Assert
        await Assert.That(request.Stream).IsEqualTo(StreamName.Unset);
        await Assert.That(request.Start).IsEqualTo(Revision.Min);
        await Assert.That(request.Limit).IsEqualTo(long.MaxValue);
        await Assert.That(request.Direction).IsEqualTo(ReadDirection.Forwards);
        await Assert.That(request.SkipDecoding).IsFalse();
    }

    #endregion

    #region ->> ForStream <<-

    [Test]
    public async ValueTask for_stream_sets_stream_name() {
        // Arrange
        StreamName stream = "my-stream";

        // Act
        var request = ReadStreamRequest.New()
            .Stream(stream)
            .Build();

        // Assert
        await Assert.That(request.Stream).IsEqualTo(stream);
    }

    #endregion

    #region ->> FromStart / FromEnd <<-

    [Test]
    public async ValueTask from_start_sets_min_revision_and_forwards_direction() {
        // Arrange & Act
        var request = ReadStreamRequest.New()
            .Stream("test-stream")
            .FromStart()
            .Build();

        // Assert
        await Assert.That(request.Start).IsEqualTo(Revision.Min);
        await Assert.That(request.Direction).IsEqualTo(ReadDirection.Forwards);
    }

    [Test]
    public async ValueTask from_end_sets_max_revision_and_backwards_direction() {
        // Arrange & Act
        var request = ReadStreamRequest.New()
            .Stream("test-stream")
            .FromEnd()
            .Build();

        // Assert
        await Assert.That(request.Start).IsEqualTo(Revision.Max);
        await Assert.That(request.Direction).IsEqualTo(ReadDirection.Backwards);
    }

    #endregion

    #region ->> FromRevision <<-

    [Test]
    public async ValueTask from_revision_sets_start_revision() {
        // Arrange
        var revision = Revision.From(42);

        // Act
        var request = ReadStreamRequest.New()
            .Stream("test-stream")
            .FromRevision(revision)
            .Build();

        // Assert
        await Assert.That(request.Start).IsEqualTo(revision);
    }

    #endregion

    #region ->> Forward / Backwards <<-

    [Test]
    public async ValueTask forward_sets_forwards_direction() {
        // Arrange & Act
        var request = ReadStreamRequest.New()
            .Stream("test-stream")
            .FromRevision(Revision.From(0))
            .Forwards()
            .Build();

        // Assert
        await Assert.That(request.Direction).IsEqualTo(ReadDirection.Forwards);
    }

    [Test]
    public async ValueTask backwards_sets_backwards_direction() {
        // Arrange & Act
        var request = ReadStreamRequest.New()
            .Stream("test-stream")
            .FromEnd()
            .Backwards()
            .Build();

        // Assert
        await Assert.That(request.Direction).IsEqualTo(ReadDirection.Backwards);
    }

    #endregion

    #region ->> WithMaxRecords <<-

    [Test]
    public async ValueTask with_max_records_sets_limit() {
        // Arrange & Act
        var request = ReadStreamRequest.New()
            .Stream("test-stream")
            .Limit(100)
            .Build();

        // Assert
        await Assert.That(request.Limit).IsEqualTo(100);
    }

    #endregion

    #region ->> SkipDecoding <<-

    [Test]
    public async ValueTask skip_decoding_sets_flag_to_true() {
        // Arrange & Act
        var request = ReadStreamRequest.New()
            .Stream("test-stream")
            .SkipDecoding()
            .Build();

        // Assert
        await Assert.That(request.SkipDecoding).IsTrue();
    }

    #endregion

    #region ->> WithFilter / WithFilterExpression <<-

    [Test]
    public async ValueTask with_filter_sets_record_filter() {
        // Arrange
        var filter = RecordFilter.None;

        // Act
        var request = ReadStreamRequest.New()
            .Stream("test-stream")
            .Filter(filter)
            .Build();

        // Assert
        await Assert.That(request.Filter).IsEqualTo(filter);
    }

    #endregion

    #region ->> Method Chaining <<-

    [Test]
    public async ValueTask method_chaining_applies_all_configurations() {
        // Arrange
        StreamName stream   = "chained-stream";
        var        revision = Revision.From(10);

        // Act
        var request = ReadStreamRequest.New()
            .Stream(stream)
            .FromRevision(revision)
            .Backwards()
            .Limit(50)
            .SkipDecoding()
            .Build();

        // Assert
        await Assert.That(request.Stream).IsEqualTo(stream);
        await Assert.That(request.Start).IsEqualTo(revision);
        await Assert.That(request.Direction).IsEqualTo(ReadDirection.Backwards);
        await Assert.That(request.Limit).IsEqualTo(50);
        await Assert.That(request.SkipDecoding).IsTrue();
    }

    #endregion

    #region ->> Build Validation <<-

    [Test]
    public void build_throws_when_no_stream_set() {
        // Arrange
        var builder = ReadStreamRequest.New();

        // Act & Assert
        Assert.Throws<Exception>(() => builder.Build());
    }

    [Test]
    public void build_throws_when_stream_is_dollar_all() {
        // Arrange
        var builder = ReadStreamRequest.New().Stream("$all");

        // Act & Assert
        Assert.Throws<Exception>(() => builder.Build());
    }

    #endregion
}
