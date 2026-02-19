using Kurrent.Client.Streams;
using Kurrent.Testing.TUnit;

namespace Kurrent.Client.Tests.Modules.Streams;

[Category("Streams")]
public class ExpectedStreamConditionTests {
    #region . Static instances .

    [Test]
    public async ValueTask any_has_correct_name() {
        await Assert.That(ExpectedStreamCondition.Any.Name).IsEqualTo("Any");
    }

    [Test]
    public async ValueTask exists_has_correct_name() {
        await Assert.That(ExpectedStreamCondition.Exists.Name).IsEqualTo("Exists");
    }

    [Test]
    public async ValueTask no_stream_has_correct_name() {
        await Assert.That(ExpectedStreamCondition.NoStream.Name).IsEqualTo("NoStream");
    }

    [Test]
    public async ValueTask deleted_has_correct_name() {
        await Assert.That(ExpectedStreamCondition.Deleted.Name).IsEqualTo("Deleted");
    }

    [Test]
    public async ValueTask tombstoned_has_correct_name() {
        await Assert.That(ExpectedStreamCondition.Tombstoned.Name).IsEqualTo("Tombstoned");
    }

    #endregion

    #region . ToString .

    [Test]
    public async ValueTask to_string_returns_name() {
        await Assert.That(ExpectedStreamCondition.Any.ToString()).IsEqualTo("Any");
        await Assert.That(ExpectedStreamCondition.Exists.ToString()).IsEqualTo("Exists");
        await Assert.That(ExpectedStreamCondition.NoStream.ToString()).IsEqualTo("NoStream");
        await Assert.That(ExpectedStreamCondition.Deleted.ToString()).IsEqualTo("Deleted");
        await Assert.That(ExpectedStreamCondition.Tombstoned.ToString()).IsEqualTo("Tombstoned");
    }

    #endregion

    #region . FromWireValue .

    public class ValidWireCases : TestCaseGenerator<long, string> {
        protected override IEnumerable<(long, string)> Data() => [
            (-1, "NoStream"),
            (-2, "Any"),
            (-4, "Exists"),
            (-10, "Deleted"),
            (-100, "Tombstoned")
        ];
    }

    [Test, ValidWireCases]
    public async ValueTask from_wire_value_returns_correct_condition(long wireValue, string expectedName) {
        var condition = ExpectedStreamCondition.From(wireValue);

        await Assert.That(condition.Name).IsEqualTo(expectedName);
    }

    public class InvalidWireCases : TestCaseGenerator<long> {
        protected override IEnumerable<long> Data() => [-3, -5, 0, 1, 42];
    }

    [Test, InvalidWireCases]
    public void from_wire_value_throws_for_invalid_values(long wireValue) {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ExpectedStreamCondition.From(wireValue)
        );
    }

    #endregion

    #region . Equality .

    [Test]
    public async ValueTask same_conditions_are_equal() {
        await Assert.That(ExpectedStreamCondition.Any).IsEqualTo(ExpectedStreamCondition.Any);
    }

    [Test]
    public async ValueTask different_conditions_are_not_equal() {
        await Assert.That(ExpectedStreamCondition.Any).IsNotEqualTo(ExpectedStreamCondition.Exists);
    }

    #endregion

    #region . Implicit conversion to ExpectedStreamState .

    [Test]
    public async ValueTask converts_to_expected_stream_state_implicitly() {
        ExpectedStreamState state = ExpectedStreamCondition.Exists;

        await Assert.That(state.IsExpectedStreamCondition).IsTrue();
        await Assert.That(state.AsExpectedStreamCondition).IsEqualTo(ExpectedStreamCondition.Exists);
    }

    #endregion
}
