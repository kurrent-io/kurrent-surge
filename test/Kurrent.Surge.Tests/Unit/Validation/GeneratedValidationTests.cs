using Bogus;
using Kurrent.Surge.Client.Indexes;

namespace Kurrent.Surge.Tests.Validation;

/// Covers the wiring the [WithValidation] generator emits: that the configured rules are reached,
/// and that a verdict always describes the object as it is at the moment it is asked for.
[Category("Validation")]
public class GeneratedValidationTests {
    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask reports_the_verdict_for_the_current_property_value() {
        // Arrange
        var options = ValidOptions();
        await Assert.That(options.Validate().IsValid).IsTrue();

        // Act — the property carrying the rule changes after the first verdict
        options.Filter = "";

        // Assert
        await Assert.That(options.Validate().IsValid).IsFalse();
    }

    [Test]
    public async ValueTask reports_the_verdict_for_the_current_collection_contents() {
        // Arrange
        var options = ValidOptions();
        await Assert.That(options.Validate().IsValid).IsTrue();

        // Act — mutating the collection touches no property setter
        options.Fields.Clear();

        // Assert
        await Assert.That(options.Validate().IsValid).IsFalse();
    }

    [Test]
    public async ValueTask reports_the_verdict_after_a_field_is_appended() {
        // Arrange — a fresh instance has no fields, so it starts invalid
        var options = new CreateIndexOptions {
            Name   = Faker.Random.AlphaNumeric(10),
            Filter = "type = 'order'"
        };

        await Assert.That(options.Validate().IsValid).IsFalse();

        // Act
        options.AddField(Faker.Random.AlphaNumeric(8), "$.customerId", IndexFieldType.String);

        // Assert
        await Assert.That(options.Validate().IsValid).IsTrue();
    }

    [Test]
    public async ValueTask names_the_field_that_failed() {
        // Arrange
        var options = ValidOptions();

        // Act
        options.Filter = "";

        // Assert — proves the configured rule ran, not just that something failed
        var problems = options.Validate().Problems;

        await Assert.That(problems.Length).IsEqualTo(1);
        await Assert.That(problems[0].FieldName).IsEqualTo(nameof(CreateIndexOptions.Filter));
    }

    [Test]
    public async ValueTask ensure_valid_throws_for_an_invalid_instance() {
        // Arrange
        var options = ValidOptions();
        options.Filter = "";

        // Act & Assert
        await Assert.That(() => options.EnsureValid()).ThrowsExactly<Kurrent.Surge.Validation.ValidationException>();
    }

    [Test]
    public async ValueTask ensure_valid_returns_for_a_valid_instance() {
        // Arrange
        var options = ValidOptions();

        // Act & Assert
        await Assert.That(() => options.EnsureValid()).ThrowsNothing();
    }

    CreateIndexOptions ValidOptions() {
        var options = new CreateIndexOptions {
            Name   = Faker.Random.AlphaNumeric(10),
            Filter = "type = 'order'"
        };

        options.AddField(Faker.Random.AlphaNumeric(8), "$.customerId", IndexFieldType.String);

        return options;
    }
}
