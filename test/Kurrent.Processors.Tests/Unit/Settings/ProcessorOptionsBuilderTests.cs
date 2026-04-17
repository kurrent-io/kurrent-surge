using Kurrent.Client.Streams;
using Kurrent.Processors.Models;
using Kurrent.Processors.Options;

namespace Kurrent.Processors.Tests.Unit.Settings;

[Category("Processors")]
public class ProcessorOptionsBuilderTests {
    #region Default Values

    [Test]
    public async ValueTask defaults_processor_id_is_empty() {
        // Arrange
        // Act
        var builder = new ProcessorOptions.Builder();

        // Assert
        await Assert.That(builder.Settings.ProcessorId).IsEqualTo(string.Empty);
    }

    [Test]
    public async ValueTask defaults_worker_id_is_not_empty() {
        // Arrange
        // Act
        var builder = new ProcessorOptions.Builder();

        // Assert
        await Assert.That(builder.Settings.WorkerId).IsNotNull().And.IsNotEmpty();
    }

    [Test]
    public async ValueTask defaults_debug_is_false() {
        // Arrange
        // Act
        var builder = new ProcessorOptions.Builder();

        // Assert
        await Assert.That(builder.Settings.Debug).IsFalse();
    }

    #endregion

    #region WithProcessorId

    [Test]
    public async ValueTask with_processor_id_sets_processor_id() {
        // Arrange
        const string processorId = "my-processor";

        // Act
        var builder = new ProcessorOptions.Builder()
            .WithProcessorId(processorId);

        // Assert
        await Assert.That(builder.Settings.ProcessorId).IsEqualTo(processorId);
    }

    #endregion

    #region WithWorkerId

    [Test]
    public async ValueTask with_worker_id_sets_worker_id() {
        // Arrange
        const string workerId = "worker-001";

        // Act
        var builder = new ProcessorOptions.Builder()
            .WithWorkerId(workerId);

        // Assert
        await Assert.That(builder.Settings.WorkerId).IsEqualTo(workerId);
    }

    #endregion

    #region WithDebugMode

    [Test]
    public async ValueTask with_debug_mode_true_sets_debug_true() {
        // Arrange
        // Act
        var builder = new ProcessorOptions.Builder()
            .WithDebugMode(true);

        // Assert
        await Assert.That(builder.Settings.Debug).IsTrue();
    }

    [Test]
    public async ValueTask with_debug_mode_false_sets_debug_false() {
        // Arrange
        // Act
        var builder = new ProcessorOptions.Builder()
            .WithDebugMode(false);

        // Assert
        await Assert.That(builder.Settings.Debug).IsFalse();
    }

    #endregion

    #region ConfigureSubscription

    [Test]
    public async ValueTask configure_subscription_applies_subscription_settings() {
        // Arrange
        const string selector = "stream_id";

        // Act
        var builder = new ProcessorOptions.Builder()
            .ConfigureSubscription(s => s
                .PartitionKeySelector(selector)
                .InitialPosition(SubscriptionInitialPosition.Earliest));

        // Assert
        await Assert.That(builder.Settings.Subscription.PartitionKeySelector).IsEqualTo(selector);
        await Assert.That(builder.Settings.Subscription.InitialPosition).IsEqualTo(SubscriptionInitialPosition.Earliest);
    }

    #endregion

    #region ConfigureBatching

    [Test]
    public async ValueTask configure_batching_applies_batching_settings() {
        // Arrange
        // Act
        var builder = new ProcessorOptions.Builder()
            .ConfigureBatching(b => b
                .MaxSize(50)
                .Timeout(100L));

        // Assert
        await Assert.That(builder.Settings.Batching.MaxSize).IsEqualTo(50);
        await Assert.That(builder.Settings.Batching.Timeout).IsEqualTo(100);
    }

    #endregion

    #region ConfigureCheckpointing

    [Test]
    public async ValueTask configure_checkpointing_applies_checkpoint_settings() {
        // Arrange
        // Act
        var builder = new ProcessorOptions.Builder()
            .ConfigureCheckpointing(c => c
                .Interval(5000L)
                .RetentionCount(5));

        // Assert
        await Assert.That(builder.Settings.Checkpoint.Interval).IsEqualTo(5000);
        await Assert.That(builder.Settings.Checkpoint.RetentionCount).IsEqualTo(5);
    }

    #endregion

    #region ConfigureWorker

    [Test]
    public async ValueTask configure_worker_applies_worker_settings() {
        // Arrange
        // Act
        var builder = new ProcessorOptions.Builder()
            .ConfigureWorker(w => w
                .BufferCapacity(500)
                .SkipDecoding(true));

        // Assert
        await Assert.That(builder.Settings.Worker.BufferCapacity).IsEqualTo(500);
        await Assert.That(builder.Settings.Worker.SkipDecoding).IsTrue();
    }

    #endregion

    #region Method Chaining

    [Test]
    public async ValueTask chained_calls_apply_all_values() {
        // Arrange
        const string processorId = "chained-processor";
        const string workerId    = "chained-worker";

        // Act
        var builder = new ProcessorOptions.Builder()
            .WithProcessorId(processorId)
            .WithWorkerId(workerId)
            .WithDebugMode(true)
            .ConfigureBatching(b => b.MaxSize(10))
            .ConfigureCheckpointing(c => c.RetentionCount(1));

        // Assert
        await Assert.That(builder.Settings.ProcessorId).IsEqualTo(processorId);
        await Assert.That(builder.Settings.WorkerId).IsEqualTo(workerId);
        await Assert.That(builder.Settings.Debug).IsTrue();
        await Assert.That(builder.Settings.Batching.MaxSize).IsEqualTo(10);
        await Assert.That(builder.Settings.Checkpoint.RetentionCount).IsEqualTo(1);
    }

    #endregion
}
