using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
public class LogQueryBuilderTests {
    static LogQueryBuilder CreateBuilder() => new(null!);

    #region Configuration

    [Test]
    public async Task from_start_sets_earliest_position_and_forwards() {
        var options = CreateBuilder().FromStart().Build();

        await Assert.That(options.Start).IsEqualTo(LogPosition.Earliest);
        await Assert.That(options.Direction).IsEqualTo(ReadDirection.Forwards);
    }

    [Test]
    public async Task from_end_sets_latest_position_and_backwards() {
        var options = CreateBuilder().FromEnd().Build();

        await Assert.That(options.Start).IsEqualTo(LogPosition.Latest);
        await Assert.That(options.Direction).IsEqualTo(ReadDirection.Backwards);
    }

    [Test]
    public async Task from_sets_explicit_position() {
        var position = LogPosition.From(42);
        var options  = CreateBuilder().From(position).Build();

        await Assert.That(options.Start).IsEqualTo(position);
    }

    [Test]
    public async Task forward_sets_direction() {
        var options = CreateBuilder().Forward().Build();

        await Assert.That(options.Direction).IsEqualTo(ReadDirection.Forwards);
    }

    [Test]
    public async Task backwards_sets_direction() {
        var options = CreateBuilder().Backwards().Build();

        await Assert.That(options.Direction).IsEqualTo(ReadDirection.Backwards);
    }

    [Test]
    public async Task with_max_records_sets_limit() {
        var options = CreateBuilder().WithMaxRecords(100).Build();

        await Assert.That(options.Limit).IsEqualTo(100);
    }

    [Test]
    public async Task with_filter_applies_filter() {
        var filter = RecordFilter.ByStream("test-stream");
        var options = CreateBuilder().WithFilter(filter).Build();

        await Assert.That(options.Filter).IsEqualTo(filter);
    }

    [Test]
    public async Task with_filter_expression_applies_expression_filter() {
        var options = CreateBuilder().WithFilterExpression("stream = 'orders'").Build();

        await Assert.That(options.Filter).IsNotNull();
        await Assert.That(options.Filter).IsTypeOf<ExpressionFilter>();
    }

    [Test]
    public async Task skip_decoding_sets_flag() {
        var options = CreateBuilder().SkipDecoding().Build();

        await Assert.That(options.SkipDecoding).IsTrue();
    }

    #endregion

    #region Chaining

    [Test]
    public async Task methods_are_chainable() {
        var builder = CreateBuilder();

        var result = builder
            .FromStart()
            .WithMaxRecords(10)
            .SkipDecoding();

        await Assert.That(result).IsEqualTo(builder);
    }

    [Test]
    public async Task full_chain_produces_correct_options() {
        var options = CreateBuilder()
            .From(10)
            .Forward()
            .WithMaxRecords(50)
            .SkipDecoding()
            .Build();

        await Assert.That(options.Start).IsEqualTo(10);
        await Assert.That(options.Direction).IsEqualTo(ReadDirection.Forwards);
        await Assert.That(options.Limit).IsEqualTo(50);
        await Assert.That(options.SkipDecoding).IsTrue();
    }

    #endregion
}
