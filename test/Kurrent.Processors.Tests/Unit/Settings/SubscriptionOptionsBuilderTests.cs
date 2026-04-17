using Kurrent.Client.Streams;
using Kurrent.Processors.Models;
using Kurrent.Processors.Options;

namespace Kurrent.Processors.Tests.Unit.Settings;

[Category("Processors")]
public class SubscriptionOptionsBuilderTests {
    #region Default Values

    [Test]
    public async ValueTask defaults_filter_expression_is_empty() {
        // Arrange
        // Act
        var settings = new SubscriptionOptions.Builder().Build();

        // Assert
        await Assert.That(settings.FilterExpression).IsEqualTo(string.Empty);
    }

    [Test]
    public async ValueTask defaults_filter_is_none() {
        // Arrange
        // Act
        var settings = new SubscriptionOptions.Builder().Build();

        // Assert
        await Assert.That(settings.Filter).IsEqualTo(RecordFilter.None);
    }

    [Test]
    public async ValueTask defaults_partition_key_selector_is_empty() {
        // Arrange
        // Act
        var settings = new SubscriptionOptions.Builder().Build();

        // Assert
        await Assert.That(settings.PartitionKeySelector).IsEqualTo(string.Empty);
    }

    [Test]
    public async ValueTask defaults_initial_position_is_latest() {
        // Arrange
        // Act
        var settings = new SubscriptionOptions.Builder().Build();

        // Assert
        await Assert.That(settings.InitialPosition).IsEqualTo(SubscriptionInitialPosition.Latest);
    }

    #endregion

    #region FilterExpression

    [Test]
    public async ValueTask filter_expression_sets_expression_and_filter() {
        // Arrange
        const string expression = "stream_id LIKE 'orders-%'";

        // Act
        var settings = new SubscriptionOptions.Builder()
            .FilterExpression(expression)
            .Build();

        // Assert
        await Assert.That(settings.FilterExpression).IsEqualTo(expression);
        await Assert.That(settings.Filter).IsNotNull();
        await Assert.That(settings.Filter.Expression).IsEqualTo(expression);
    }

    #endregion

    #region Filter

    [Test]
    public async ValueTask filter_sets_filter_instance() {
        // Arrange
        var filter = RecordFilter.ByStream("orders");

        // Act
        var settings = new SubscriptionOptions.Builder()
            .Filter(filter)
            .Build();

        // Assert
        await Assert.That(settings.Filter).IsEqualTo(filter);
    }

    #endregion

    #region PartitionKeySelector

    [Test]
    public async ValueTask partition_key_selector_sets_value() {
        // Arrange
        const string selector = "data.customer_id";

        // Act
        var settings = new SubscriptionOptions.Builder()
            .PartitionKeySelector(selector)
            .Build();

        // Assert
        await Assert.That(settings.PartitionKeySelector).IsEqualTo(selector);
    }

    #endregion

    #region InitialPosition

    [Test]
    public async ValueTask initial_position_sets_value() {
        // Arrange
        // Act
        var settings = new SubscriptionOptions.Builder()
            .InitialPosition(SubscriptionInitialPosition.Earliest)
            .Build();

        // Assert
        await Assert.That(settings.InitialPosition).IsEqualTo(SubscriptionInitialPosition.Earliest);
    }

    #endregion

    #region StartFrom

    [Test]
    public async ValueTask start_from_sets_start_position() {
        // Arrange
        var position = RecordPosition.Earliest;

        // Act
        var settings = new SubscriptionOptions.Builder()
            .StartFrom(position)
            .Build();

        // Assert
        await Assert.That(settings.StartPosition).IsEqualTo(position);
    }

    #endregion

    #region Method Chaining

    [Test]
    public async ValueTask chained_calls_apply_all_values() {
        // Arrange
        const string selector = "stream_id";

        // Act
        const string expression = "stream_id LIKE 'orders-%'";

        var settings = new SubscriptionOptions.Builder()
            .FilterExpression(expression)
            .PartitionKeySelector(selector)
            .InitialPosition(SubscriptionInitialPosition.Earliest)
            .Build();

        // Assert
        await Assert.That(settings.FilterExpression).IsEqualTo(expression);
        await Assert.That(settings.PartitionKeySelector).IsEqualTo(selector);
        await Assert.That(settings.InitialPosition).IsEqualTo(SubscriptionInitialPosition.Earliest);
    }

    #endregion
}
