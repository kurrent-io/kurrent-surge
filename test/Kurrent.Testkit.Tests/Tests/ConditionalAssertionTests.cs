// ReSharper disable CheckNamespace

namespace Kurrent.Testkit.TUnit.Assertions;

[Category("Testing")]
public class ConditionalAssertionTests {
    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    #region ->> suffix When on Assertion<T> <<-

    [Test]
    public async ValueTask suffix_when_true_evaluates_passing_assertion() {
        var value = Faker.Random.Int();

        await Assert.That(value).IsEqualTo(value).When(true);
    }

    [Test]
    public async ValueTask suffix_when_true_evaluates_failing_assertion() {
        var actual   = Faker.Random.Int(1, 1000);
        var expected = actual + 1;

        var action = async () => await Assert.That(actual).IsEqualTo(expected).When(true);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask suffix_when_false_skips_failing_assertion() {
        var actual   = Faker.Random.Int(1, 1000);
        var expected = actual + 1;

        await Assert.That(actual).IsEqualTo(expected).When(false);
    }

    [Test]
    public async ValueTask suffix_when_false_returns_default() {
        var value = Faker.Lorem.Word();

        var result = await Assert.That(value).IsEqualTo($"{value}_nope").When(false);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async ValueTask suffix_when_true_returns_value() {
        var value = Faker.Lorem.Word();

        var result = await Assert.That(value).IsEqualTo(value).When(true);

        await Assert.That(result).IsEqualTo(value);
    }

    #endregion

    #region ->> branch When on IAssertionSource<T> <<-

    [Test]
    public async ValueTask branch_when_true_evaluates_passing_assertion() {
        var value = Faker.Random.Int();

        await Assert.That(value).When(true, x => x.IsEqualTo(value));
    }

    [Test]
    public async ValueTask branch_when_true_evaluates_failing_assertion() {
        var actual   = Faker.Random.Int(1, 1000);
        var expected = actual + 1;

        var action = async () => await Assert.That(actual).When(true, x => x.IsEqualTo(expected));

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask branch_when_false_skips_failing_assertion() {
        var actual   = Faker.Random.Int(1, 1000);
        var expected = actual + 1;

        await Assert.That(actual).When(false, x => x.IsEqualTo(expected));
    }

    [Test]
    public async ValueTask branch_when_false_returns_original_value() {
        var value = Faker.Lorem.Word();

        var result = await Assert.That(value).When(false, x => x.IsEqualTo($"{value}_nope"));

        await Assert.That(result).IsEqualTo(value);
    }

    #endregion

    #region ->> When + Otherwise <<-

    [Test]
    public async ValueTask otherwise_executes_when_condition_is_false() {
        var value = Faker.Random.Int();

        await Assert.That(value)
            .When(false, x => x.IsEqualTo(value + 1))
            .Otherwise(x => x.IsEqualTo(value));
    }

    [Test]
    public async ValueTask otherwise_skipped_when_condition_is_true() {
        var value = Faker.Random.Int();

        await Assert.That(value)
            .When(true, x => x.IsEqualTo(value))
            .Otherwise(x => x.IsEqualTo(value + 1));
    }

    [Test]
    public async ValueTask otherwise_fails_when_its_assertion_fails() {
        var value = Faker.Random.Int(1, 1000);

        var action = async () => await Assert.That(value)
            .When(false, x => x.IsEqualTo(value))
            .Otherwise(x => x.IsEqualTo(value + 1));

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask when_branch_fails_even_if_otherwise_would_pass() {
        var value = Faker.Random.Int(1, 1000);

        var action = async () => await Assert.That(value)
            .When(true, x => x.IsEqualTo(value + 1))
            .Otherwise(x => x.IsEqualTo(value));

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask otherwise_returns_value_from_executed_branch() {
        var value = Faker.Lorem.Word();

        var whenResult = await Assert.That(value)
            .When(true, x => x.IsEqualTo(value))
            .Otherwise(x => x.IsEqualTo($"{value}_nope"));

        await Assert.That(whenResult).IsEqualTo(value);

        var otherwiseResult = await Assert.That(value)
            .When(false, x => x.IsEqualTo($"{value}_nope"))
            .Otherwise(x => x.IsEqualTo(value));

        await Assert.That(otherwiseResult).IsEqualTo(value);
    }

    #endregion

    #region ->> predicate When on IAssertionSource<T> <<-

    [Test]
    public async ValueTask predicate_when_matching_evaluates_passing_assertion() {
        var value = Faker.Random.Int(10, 100);

        await Assert.That(value).When(x => x > 0, x => x.IsGreaterThan(0));
    }

    [Test]
    public async ValueTask predicate_when_matching_evaluates_failing_assertion() {
        var value = Faker.Random.Int(10, 100);

        var action = async () => await Assert.That(value).When(x => x > 0, x => x.IsNegative());

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask predicate_when_not_matching_skips_assertion() {
        var value = Faker.Random.Int(-100, -1);

        await Assert.That(value).When(x => x > 0, x => x.IsGreaterThan(1000));
    }

    [Test]
    public async ValueTask predicate_with_otherwise_takes_false_branch() {
        var value = Faker.Random.Int(-100, -1);

        await Assert.That(value)
            .When(x => x > 0, x => x.IsPositive())
            .Otherwise(x => x.IsNegative());
    }

    [Test]
    public async ValueTask predicate_with_otherwise_takes_true_branch() {
        var value = Faker.Random.Int(1, 100);

        await Assert.That(value)
            .When(x => x > 0, x => x.IsPositive())
            .Otherwise(x => x.IsNegative());
    }

    [Test]
    public async ValueTask predicate_on_string_value() {
        var value = Faker.Lorem.Word();

        await Assert.That(value)
            .When(x => x.Length > 0, x => x.IsNotEmpty())
            .Otherwise(x => x.IsEmpty());
    }

    #endregion

    #region ->> realistic scenarios <<-

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async ValueTask conditional_null_check_based_on_flag(bool isNull) {
        var word = Faker.Lorem.Word();
        object? value = isNull ? null : word;

        await Assert.That(value)
            .When(isNull, x => x.IsNull())
            .Otherwise(x => x.IsNotNull());
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async ValueTask suffix_on_parameterized_test(bool shouldMatch) {
        var actual   = Faker.Random.Int(1, 1000);
        var expected = shouldMatch ? actual : actual + 1;

        await Assert.That(actual).IsEqualTo(expected).When(shouldMatch);
    }

    #endregion
}
