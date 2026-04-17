using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Modules.Streams;

[Category("Streams")]
public class ReadRecordsRequestBuilderTests {
    #region ->> Default Values <<-

    [Test]
    public async ValueTask default_request_has_expected_defaults() {
        // Arrange & Act
        var request = new ReadRecordsRequest();

        // Assert
        await Assert.That(request.Start).IsEqualTo(LogPosition.Latest);
        await Assert.That(request.Limit).IsEqualTo(long.MaxValue);
        await Assert.That(request.Direction).IsEqualTo(ReadDirection.Forwards);
        await Assert.That(request.SkipDecoding).IsFalse();
        await Assert.That(request.Filter).IsEqualTo(RecordFilter.None);
    }

    #endregion

    #region ->> FromPosition <<-

    [Test]
    public async ValueTask from_position_sets_start_position() {
        // Arrange
        var position = LogPosition.From(100);

        // Act
        var request = ReadRecordsRequest.New
            .FromPosition(position)
            .Build();

        // Assert
        await Assert.That(request.Start).IsEqualTo(position);
    }

    #endregion

    #region ->> Forward / Backwards <<-

    [Test]
    public async ValueTask forward_sets_forwards_direction() {
        // Arrange & Act
        var request = ReadRecordsRequest.New
            .Forward()
            .Build();

        // Assert
        await Assert.That(request.Direction).IsEqualTo(ReadDirection.Forwards);
    }

    [Test]
    public async ValueTask backwards_sets_backwards_direction() {
        // Arrange & Act
        var request = ReadRecordsRequest.New
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
        var request = ReadRecordsRequest.New
            .WithMaxRecords(200)
            .Build();

        // Assert
        await Assert.That(request.Limit).IsEqualTo(200);
    }

    #endregion

    #region ->> SkipDecoding <<-

    [Test]
    public async ValueTask skip_decoding_sets_flag_to_true() {
        // Arrange & Act
        var request = ReadRecordsRequest.New
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
        var request = ReadRecordsRequest.New
            .WithFilter(filter)
            .Build();

        // Assert
        await Assert.That(request.Filter).IsEqualTo(filter);
    }

    [Test]
    public async ValueTask with_filter_expression_sets_expression_filter() {
        // Arrange
        const string expression = "schema_name = 'OrderPlaced'";

        // Act
        var request = ReadRecordsRequest.New
            .WithFilterExpression(expression)
            .Build();

        // Assert
        await Assert.That(request.Filter).IsNotNull();
        await Assert.That(request.Filter).IsNotEqualTo(RecordFilter.None);
    }

    #endregion

    #region ->> Method Chaining <<-

    [Test]
    public async ValueTask method_chaining_applies_all_configurations() {
        // Arrange
        var position = LogPosition.From(500);

        // Act
        var request = ReadRecordsRequest.New
            .FromPosition(position)
            .Backwards()
            .WithMaxRecords(75)
            .SkipDecoding()
            .Build();

        // Assert
        await Assert.That(request.Start).IsEqualTo(position);
        await Assert.That(request.Direction).IsEqualTo(ReadDirection.Backwards);
        await Assert.That(request.Limit).IsEqualTo(75);
        await Assert.That(request.SkipDecoding).IsTrue();
    }

    #endregion

    #region ->> Build Validation <<-

    [Test]
    public void build_throws_when_max_records_is_zero() {
        // Arrange
        var builder = ReadRecordsRequest.New.WithMaxRecords(0);

        // Act & Assert
        Assert.Throws<Exception>(() => builder.Build());
    }

    [Test]
    public async ValueTask build_succeeds_with_default_values() {
        // Arrange
        var builder = ReadRecordsRequest.New;

        // Act — should not throw
        var request = builder.Build();

        // Assert
        await Assert.That(request).IsNotNull();
    }

    #endregion
}
