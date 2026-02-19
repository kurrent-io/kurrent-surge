using Kurrent.Client.Streams;
using Kurrent.Testing.TUnit;

namespace Kurrent.Client.Tests.Modules.Streams;

[Category("Streams")]
public class ActualStreamStateTests {
    #region . DU construction via implicit conversion .

    [Test]
    public async ValueTask condition_converts_to_du_implicitly() {
        ActualStreamState state = ActualStreamCondition.NoStream;

        await Assert.That(state.IsActualStreamCondition).IsTrue();
        await Assert.That(state.AsActualStreamCondition).IsEqualTo(ActualStreamCondition.NoStream);
    }

    [Test]
    public async ValueTask revision_converts_to_du_implicitly() {
        ActualStreamState state = StreamRevision.From(42);

        await Assert.That(state.IsStreamRevision).IsTrue();
        await Assert.That(state.AsStreamRevision).IsEqualTo(StreamRevision.From(42));
    }

    #endregion

    #region . Match .

    [Test]
    public async ValueTask match_dispatches_to_condition_branch() {
        ActualStreamState state = ActualStreamCondition.Deleted;

        var result = state.Match(
            onActualStreamCondition: c => c.Name,
            onStreamRevision: _ => "revision"
        );

        await Assert.That(result).IsEqualTo("Deleted");
    }

    [Test]
    public async ValueTask match_dispatches_to_revision_branch() {
        ActualStreamState state = StreamRevision.From(7);

        var result = state.Match(
            onActualStreamCondition: _ => -1L,
            onStreamRevision: r => (long)r
        );

        await Assert.That(result).IsEqualTo(7L);
    }

    #endregion

    #region . Wire round-trip .

    public class WireRoundTripCases : TestCaseGenerator<long> {
        protected override IEnumerable<long> Data() => [-1, -10, -100, 0, 1, 42, 999];
    }

    [Test, WireRoundTripCases]
    public async ValueTask wire_round_trip_preserves_value(long wireValue) {
        var state = ActualStreamState.FromWireValue(wireValue);

        await Assert.That(state.ToWireValue()).IsEqualTo(wireValue);
    }

    [Test]
    public async ValueTask from_wire_value_returns_condition_for_sentinel_values() {
        var noStream   = ActualStreamState.FromWireValue(-1);
        var deleted    = ActualStreamState.FromWireValue(-10);
        var tombstoned = ActualStreamState.FromWireValue(-100);

        await Assert.That(noStream.IsActualStreamCondition).IsTrue();
        await Assert.That(deleted.IsActualStreamCondition).IsTrue();
        await Assert.That(tombstoned.IsActualStreamCondition).IsTrue();
    }

    [Test]
    public async ValueTask from_wire_value_returns_revision_for_non_negative_values() {
        var zero = ActualStreamState.FromWireValue(0);
        var ten  = ActualStreamState.FromWireValue(10);

        await Assert.That(zero.IsStreamRevision).IsTrue();
        await Assert.That(ten.IsStreamRevision).IsTrue();
    }

    [Test]
    public void from_wire_value_throws_for_invalid_sentinel() {
        Assert.Throws<ArgumentOutOfRangeException>(() => ActualStreamState.FromWireValue(-5));
    }

    #endregion

    #region . Equality .

    [Test]
    public async ValueTask same_condition_values_are_equal() {
        ActualStreamState a = ActualStreamCondition.Tombstoned;
        ActualStreamState b = ActualStreamCondition.Tombstoned;

        await Assert.That(a).IsEqualTo(b);
    }

    [Test]
    public async ValueTask same_revision_values_are_equal() {
        ActualStreamState a = StreamRevision.From(5);
        ActualStreamState b = StreamRevision.From(5);

        await Assert.That(a).IsEqualTo(b);
    }

    [Test]
    public async ValueTask condition_and_revision_are_not_equal() {
        ActualStreamState condition = ActualStreamCondition.NoStream;
        ActualStreamState revision  = StreamRevision.From(0);

        await Assert.That(condition).IsNotEqualTo(revision);
    }

    [Test]
    public async ValueTask different_conditions_are_not_equal() {
        ActualStreamState a = ActualStreamCondition.Deleted;
        ActualStreamState b = ActualStreamCondition.Tombstoned;

        await Assert.That(a).IsNotEqualTo(b);
    }

    #endregion

    #region . Cross-DU conversion to ExpectedStreamState .

    [Test]
    public async ValueTask actual_condition_converts_to_expected_state() {
        ExpectedStreamState expected = ActualStreamCondition.NoStream;

        await Assert.That(expected.IsExpectedStreamCondition).IsTrue();
        await Assert.That(expected.AsExpectedStreamCondition.Name).IsEqualTo("NoStream");
    }

    [Test]
    public async ValueTask actual_state_with_revision_converts_to_expected_state() {
        ActualStreamState actual = StreamRevision.From(10);
        ExpectedStreamState expected = actual;

        await Assert.That(expected.IsStreamRevision).IsTrue();
        await Assert.That(expected.AsStreamRevision).IsEqualTo(StreamRevision.From(10));
    }

    [Test]
    public async ValueTask actual_state_with_condition_converts_to_expected_state() {
        ActualStreamState actual = ActualStreamCondition.Tombstoned;
        ExpectedStreamState expected = actual;

        await Assert.That(expected.IsExpectedStreamCondition).IsTrue();
        await Assert.That(expected.AsExpectedStreamCondition.Name).IsEqualTo("Tombstoned");
    }

    #endregion
}
