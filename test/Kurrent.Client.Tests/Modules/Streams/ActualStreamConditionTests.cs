using Kurrent.Client.Streams;
using Kurrent.Testing.TUnit;

namespace Kurrent.Client.Tests.Modules.Streams;

[Category("Streams")]
public class ActualStreamConditionTests {
    #region . Static instances .

    [Test]
    public async ValueTask no_stream_has_correct_name() {
        await Assert.That(ActualStreamCondition.NoStream.Name).IsEqualTo("NoStream");
    }

    [Test]
    public async ValueTask deleted_has_correct_name() {
        await Assert.That(ActualStreamCondition.Deleted.Name).IsEqualTo("Deleted");
    }

    [Test]
    public async ValueTask tombstoned_has_correct_name() {
        await Assert.That(ActualStreamCondition.Tombstoned.Name).IsEqualTo("Tombstoned");
    }

    #endregion

    #region . ToString .

    [Test]
    public async ValueTask to_string_returns_name() {
        await Assert.That(ActualStreamCondition.NoStream.ToString()).IsEqualTo("NoStream");
        await Assert.That(ActualStreamCondition.Deleted.ToString()).IsEqualTo("Deleted");
        await Assert.That(ActualStreamCondition.Tombstoned.ToString()).IsEqualTo("Tombstoned");
    }

    #endregion

    #region . FromWireValue .

    public class ValidWireCases : TestCaseGenerator<long, string> {
        protected override IEnumerable<(long, string)> Data() => [
            (-1, "NoStream"),
            (-10, "Deleted"),
            (-100, "Tombstoned")
        ];
    }

    [Test, ValidWireCases]
    public async ValueTask from_wire_value_returns_correct_condition(long wireValue, string expectedName) {
        var condition = ActualStreamCondition.From(wireValue);

        await Assert.That(condition.Name).IsEqualTo(expectedName);
    }

    public class InvalidWireCases : TestCaseGenerator<long> {
        protected override IEnumerable<long> Data() => [-2, -4, -5, 0, 1, 42];
    }

    [Test, InvalidWireCases]
    public void from_wire_value_throws_for_invalid_values(long wireValue) {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ActualStreamCondition.From(wireValue)
        );
    }

    #endregion

    #region . Equality .

    [Test]
    public async ValueTask same_conditions_are_equal() {
        await Assert.That(ActualStreamCondition.NoStream).IsEqualTo(ActualStreamCondition.NoStream);
    }

    [Test]
    public async ValueTask different_conditions_are_not_equal() {
        await Assert.That(ActualStreamCondition.Deleted).IsNotEqualTo(ActualStreamCondition.Tombstoned);
    }

    #endregion

    #region . Cross-DU implicit conversion .

    [Test]
    public async ValueTask converts_to_expected_stream_state_implicitly() {
        ExpectedStreamState expected = ActualStreamCondition.NoStream;

        await Assert.That(expected.IsExpectedStreamCondition).IsTrue();
        await Assert.That(expected.AsExpectedStreamCondition.Name).IsEqualTo("NoStream");
    }

    [Test]
    public async ValueTask converts_to_actual_stream_state_implicitly() {
        ActualStreamState actual = ActualStreamCondition.Deleted;

        await Assert.That(actual.IsActualStreamCondition).IsTrue();
        await Assert.That(actual.AsActualStreamCondition).IsEqualTo(ActualStreamCondition.Deleted);
    }

    #endregion
}
