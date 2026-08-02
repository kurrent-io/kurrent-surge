using System.Reflection;
using Bogus;
using Kurrent.Processors.Configuration;
using Kurrent.Projectors;

namespace Kurrent.Surge.Streaming.Tests.Projections;

/// <summary>
/// Pins the projection configuration surface. <see cref="ProjectorOptions{TClient}"/> deliberately does
/// not derive from <see cref="ProcessorOptions"/> — it declares its own knobs and maps onto one — so
/// nothing forces the two to stay aligned. A knob added to the processor and forgotten here is invisible
/// to every projection, and a switch that leaks onto the projection surface breaks exactly-once.
/// </summary>
[Category("Projectors")]
public class ProjectorOptionsSurfaceTests {
    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    /// <summary>
    /// Processor knobs a projection does not get, and why. Anything here is a decision; anything missing
    /// from both this list and the projector surface is an oversight.
    /// </summary>
    static readonly Dictionary<string, string> DeliberatelyAbsent = new() {
        ["ProcessorId"]    = "named ProjectionId on a projection, and mapped onto this",
        ["DroneId"]        = "named InstanceId on a projection, and mapped onto this",
        ["MaxParallelism"] = "a projection commits its rows and its positions in one transaction — single writer"
    };

    #region ->> Surface <<-

    [Test]
    public async ValueTask every_processor_knob_is_either_offered_to_projections_or_deliberately_absent() {
        // Arrange
        var offeredToProjections = PublicPropertyNames(typeof(ProjectorOptions<FakeProjectionClient>));

        // Act — processor knobs that a projection can neither set nor is documented as not getting.
        var unaccountedFor = PublicPropertyNames(typeof(ProcessorOptions))
            .Except(offeredToProjections)
            .Except(DeliberatelyAbsent.Keys)
            .Order()
            .ToArray();

        // Assert
        await Assert.That(unaccountedFor).IsEquivalentTo(Array.Empty<string>());
    }

    [Test]
    public async ValueTask the_absent_list_does_not_name_a_processor_knob_that_no_longer_exists() {
        // Arrange — a stale entry silently excuses the next real omission, so the list is checked too.
        var processorKnobs = PublicPropertyNames(typeof(ProcessorOptions));

        // Act
        var stale = DeliberatelyAbsent.Keys.Except(processorKnobs).Order().ToArray();

        // Assert
        await Assert.That(stale).IsEquivalentTo(Array.Empty<string>());
    }

    [Test]
    public async ValueTask projections_are_offered_only_the_checkpoint_interval() {
        // Arrange — AutoTrack and AutoCheckpoint gate the tracking a projection depends on, and
        // RetentionCount and FailurePolicy govern a checkpoint store it never writes to.
        var expected = new[] { nameof(ProjectorCheckpointOptions.Interval) };

        // Act
        var offered = PublicPropertyNames(typeof(ProjectorCheckpointOptions));

        // Assert
        await Assert.That(offered).IsEquivalentTo(expected);
    }

    #endregion

    #region ->> Mapping <<-

    [Test]
    public async ValueTask mapping_pins_the_switches_a_projection_must_not_flip() {
        // Arrange — the interval is a projection's epoch boundary and travels; the two switches do not
        // exist on the projector surface, so the mapped options must carry their defaults.
        var interval = TimeSpan.FromSeconds(Faker.Random.Int(1, 60));
        var options  = NewOptions();

        options.Checkpointing(checkpoint => checkpoint.IntervalFrom(interval));

        // Act
        var mapped = options.ToProcessorOptions();

        // Assert
        await Assert.That(mapped.Checkpoint.Interval).IsEqualTo(interval);
        await Assert.That(mapped.Checkpoint.AutoTrack).IsTrue();
        await Assert.That(mapped.Checkpoint.AutoCheckpoint).IsTrue();
        await Assert.That(mapped.MaxParallelism).IsEqualTo(1);
    }

    [Test]
    public async ValueTask mapping_derives_the_drone_id_when_no_instance_id_was_set() {
        // Arrange — assigning ProcessorId derives a DroneId, so mapping a blank InstanceId over it would
        // undo that derivation and leave the drone unidentified.
        var options       = NewOptions();
        var derivedPrefix = $"{options.ProjectionId}-";

        // Act
        var mapped = options.ToProcessorOptions();

        // Assert
        await Assert.That(mapped.ProcessorId).IsEqualTo(options.ProjectionId);
        await Assert.That(mapped.DroneId.StartsWith(derivedPrefix, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async ValueTask mapping_uses_the_instance_id_when_one_was_set() {
        // Arrange
        var options = NewOptions();

        options.InstanceId = Faker.Internet.DomainWord();

        // Act
        var mapped = options.ToProcessorOptions();

        // Assert
        await Assert.That(mapped.DroneId).IsEqualTo(options.InstanceId);
    }

    [Test]
    public async ValueTask mapping_carries_the_nested_option_objects_by_reference() {
        // Arrange — a caller configures these through the projector options before the map runs; copying
        // them would silently drop everything that was set.
        var options = NewOptions();

        // Act
        var mapped = options.ToProcessorOptions();

        // Assert
        await Assert.That(mapped.Subscription).IsSameReferenceAs(options.Subscription);
        await Assert.That(mapped.Batching).IsSameReferenceAs(options.Batching);
        await Assert.That(mapped.Resilience).IsSameReferenceAs(options.Resilience);
        await Assert.That(mapped.Observers).IsSameReferenceAs(options.Observers);
    }

    #endregion

    #region ->> Arranging <<-

    ProjectorOptions<FakeProjectionClient> NewOptions() =>
        new() { ProjectionId = Faker.Internet.DomainWord() };

    static string[] PublicPropertyNames(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.DeclaringType != typeof(object))
            .Select(property => property.Name)
            .ToArray();

    #endregion
}
