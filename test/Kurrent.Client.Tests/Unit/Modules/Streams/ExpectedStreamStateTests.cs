using Kurrent.Client.Streams;
using Kurrent.Testkit;

namespace Kurrent.Client.Tests.Modules.Streams;

[Category("Streams")]
public class ExpectedStreamStateTests {
    // #region . DU construction via implicit conversion .
    //
    // [Test]
    // public async ValueTask condition_converts_to_du_implicitly() {
    //     ExpectedStreamState state = ExpectedStreamCondition.Any;
    //
    //     await Assert.That(state.IsExpectedStreamCondition).IsTrue();
    //     await Assert.That(state.AsExpectedStreamCondition).IsEqualTo(ExpectedStreamCondition.Any);
    // }
    //
    // [Test]
    // public async ValueTask revision_converts_to_du_implicitly() {
    //     ExpectedStreamState state = Revision.From(99);
    //
    //     await Assert.That(state.IsRevision).IsTrue();
    //     await Assert.That(state.AsRevision).IsEqualTo(Revision.From(99));
    // }
    //
    // [Test]
    // public async ValueTask any_static_field_wraps_any_condition() {
    //     var state = ExpectedStreamState.Any;
    //
    //     await Assert.That(state.IsExpectedStreamCondition).IsTrue();
    //     await Assert.That(state.AsExpectedStreamCondition).IsEqualTo(ExpectedStreamCondition.Any);
    // }
    //
    // #endregion
    //
    // #region . Match .
    //
    // [Test]
    // public async ValueTask match_dispatches_to_condition_branch() {
    //     ExpectedStreamState state = ExpectedStreamCondition.Exists;
    //
    //     var result = state.Match(
    //         onExpectedStreamCondition: c => c.Name,
    //         onRevision: _ => "revision"
    //     );
    //
    //     await Assert.That(result).IsEqualTo("Exists");
    // }
    //
    // [Test]
    // public async ValueTask match_dispatches_to_revision_branch() {
    //     ExpectedStreamState state = Revision.From(3);
    //
    //     var result = state.Match(
    //         onExpectedStreamCondition: _ => -1L,
    //         onRevision: r => (long)r
    //     );
    //
    //     await Assert.That(result).IsEqualTo(3L);
    // }
    //
    // #endregion
    //
    // #region . Wire round-trip .
    //
    // public class WireRoundTripCases : TestCaseGenerator<long> {
    //     protected override IEnumerable<long> Data() => [-1, -2, -4, -10, -100, 0, 1, 42, 999];
    // }
    //
    // [Test, WireRoundTripCases]
    // public async ValueTask wire_round_trip_preserves_value(long wireValue) {
    //     var state = ExpectedStreamState.FromWireValue(wireValue);
    //
    //     await Assert.That(state.ToWireValue()).IsEqualTo(wireValue);
    // }
    //
    // [Test]
    // public async ValueTask from_wire_value_maps_unset_revision_to_any() {
    //     var state = ExpectedStreamState.FromWireValue(-1000);
    //
    //     await Assert.That(state.IsExpectedStreamCondition).IsTrue();
    //     await Assert.That(state.AsExpectedStreamCondition).IsEqualTo(ExpectedStreamCondition.Any);
    // }
    //
    // public class SentinelValueCases : TestCaseGenerator<long, ExpectedStreamCondition> {
    //     protected override IEnumerable<(long, ExpectedStreamCondition)> Data() => [
    //         (-1, ExpectedStreamCondition.NoStream),
    //         (-2, ExpectedStreamCondition.Any),
    //         (-4, ExpectedStreamCondition.Exists),
    //         (-10, ExpectedStreamCondition.Deleted),
    //         (-100, ExpectedStreamCondition.Tombstoned)
    //     ];
    // }
    //
    // [Test, SentinelValueCases]
    // public async ValueTask from_wire_value_returns_condition_for_sentinel_values(long wireValue, ExpectedStreamCondition expected) {
    //     var state = ExpectedStreamState.FromWireValue(wireValue);
    //
    //     await Assert.That(state.IsExpectedStreamCondition).IsTrue();
    //     await Assert.That(state.AsExpectedStreamCondition).IsEqualTo(expected);
    // }
    //
    // [Test]
    // public async ValueTask from_wire_value_returns_revision_for_non_negative_values() {
    //     var zero = ExpectedStreamState.FromWireValue(0);
    //     var ten  = ExpectedStreamState.FromWireValue(10);
    //
    //     await Assert.That(zero.IsRevision).IsTrue();
    //     await Assert.That(ten.IsRevision).IsTrue();
    // }
    //
    // [Test]
    // public void from_wire_value_throws_for_invalid_sentinel() {
    //     Assert.Throws<ArgumentOutOfRangeException>(() => ExpectedStreamState.FromWireValue(-5));
    // }
    //
    // #endregion
    //
    // #region . Equality .
    //
    // [Test]
    // public async ValueTask same_condition_values_are_equal() {
    //     ExpectedStreamState a = ExpectedStreamCondition.NoStream;
    //     ExpectedStreamState b = ExpectedStreamCondition.NoStream;
    //
    //     await Assert.That(a).IsEqualTo(b);
    // }
    //
    // [Test]
    // public async ValueTask same_revision_values_are_equal() {
    //     ExpectedStreamState a = Revision.From(5);
    //     ExpectedStreamState b = Revision.From(5);
    //
    //     await Assert.That(a).IsEqualTo(b);
    // }
    //
    // [Test]
    // public async ValueTask condition_and_revision_are_not_equal() {
    //     ExpectedStreamState condition = ExpectedStreamCondition.NoStream;
    //     ExpectedStreamState revision  = Revision.From(0);
    //
    //     await Assert.That(condition).IsNotEqualTo(revision);
    // }
    //
    // [Test]
    // public async ValueTask different_conditions_are_not_equal() {
    //     ExpectedStreamState a = ExpectedStreamCondition.Any;
    //     ExpectedStreamState b = ExpectedStreamCondition.Exists;
    //
    //     await Assert.That(a).IsNotEqualTo(b);
    // }
    //
    // #endregion
    //
    // #region . Cross-DU: ActualStreamState -> ExpectedStreamState .
    //
    // [Test]
    // public async ValueTask actual_condition_converts_to_expected_condition() {
    //     ActualStreamState actual = ActualStreamCondition.Deleted;
    //     ExpectedStreamState expected = actual;
    //
    //     await Assert.That(expected.IsExpectedStreamCondition).IsTrue();
    //     await Assert.That(expected.AsExpectedStreamCondition.Name).IsEqualTo("Deleted");
    // }
    //
    // [Test]
    // public async ValueTask actual_revision_converts_to_expected_revision() {
    //     ActualStreamState actual = Revision.From(77);
    //     ExpectedStreamState expected = actual;
    //
    //     await Assert.That(expected.IsRevision).IsTrue();
    //     await Assert.That(expected.AsRevision).IsEqualTo(Revision.From(77));
    // }
    //
    // [Test]
    // public async ValueTask cross_du_wire_round_trip_preserves_value() {
    //     ActualStreamState actual = ActualStreamCondition.Tombstoned;
    //     ExpectedStreamState expected = actual;
    //
    //     await Assert.That(expected.ToWireValue()).IsEqualTo(-100L);
    // }
    //
    // #endregion
}
