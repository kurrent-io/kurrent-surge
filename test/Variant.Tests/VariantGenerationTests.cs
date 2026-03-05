// ReSharper disable NotAccessedPositionalProperty.Global
// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable UnusedType.Global

namespace Kurrent.Variant.Tests;

// Define a test type that implements IVariant directly
// The source generator will generate the full implementation.
public readonly partial record struct TestVariantResult : IVariant<string, int> {
    // User-defined part is empty or can contain other non-generated members.
}

public readonly partial record struct TestVariantComplexResult : IVariant<MyError, MySuccess, MyWarning> {
    // Generator will handle this
}

public record MyError(string Code, string Message);

public record MySuccess(Guid Id);

public record MyWarning(string Text);

[UsedImplicitly]
public class VariantGenerationTests {
    [Test]
    public async Task is_string_should_be_true_when_value_is_string() {
        TestVariantResult result = "hello";
        await Assert.That(result.IsString).IsTrue();
        await Assert.That(result.IsInt).IsFalse();
    }

    [Test]
    public async Task as_string_should_return_string_value_when_value_is_string() {
        TestVariantResult result = "hello";
        await Assert.That(result.AsString).IsEqualTo("hello");
    }

    [Test]
    public async Task as_string_should_throw_when_value_is_not_string() {
        TestVariantResult result = 123; // int value
        await Assert.That(() => result.AsString).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task is_int_should_be_true_when_value_is_int() {
        TestVariantResult result = 123;
        await Assert.That(result.IsInt).IsTrue();
        await Assert.That(result.IsString).IsFalse();
    }

    [Test]
    public async Task as_int_should_return_int_value_when_value_is_int() {
        TestVariantResult result = 123;
        await Assert.That(result.AsInt).IsEqualTo(123);
    }

    [Test]
    public async Task as_int_should_throw_when_value_is_not_int() {
        TestVariantResult result = "not an int";
        await Assert.That(() => result.AsInt).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task value_property_should_return_correct_object() {
        TestVariantResult resultString = "test";
        await Assert.That(resultString.Value).IsEqualTo("test");
        await Assert.That(resultString.Value).IsTypeOf<string>();

        TestVariantResult resultInt = 42;
        await Assert.That(resultInt.Value).IsEqualTo(42);
        await Assert.That(resultInt.Value).IsTypeOf<int>();
    }

    [Test]
    public async Task index_property_should_return_correct_index() {
        TestVariantResult resultString = "test";
        await Assert.That(resultString.Index).IsEqualTo(0);

        TestVariantResult resultInt = 42;
        await Assert.That(resultInt.Index).IsEqualTo(1);
    }

    [Test]
    public async Task to_string_should_return_underlying_value_to_string() {
        TestVariantResult resultString = "test";
        await Assert.That(resultString.ToString()).IsEqualTo("test");

        TestVariantResult resultInt = 42;
        await Assert.That(resultInt.ToString()).IsEqualTo("42");
    }

    [Test]
    public async Task equals_should_return_true_for_same_value_and_type() {
        TestVariantResult result1A = "hello";
        TestVariantResult result1B = "hello";
        await Assert.That(result1A.Equals(result1B)).IsTrue();
    }

    [Test]
    public async Task equals_should_return_false_for_different_value_same_type() {
        TestVariantResult result1A = "hello";
        TestVariantResult result1B = "world";
        await Assert.That(result1A.Equals(result1B)).IsFalse();
    }

    [Test]
    public async Task equals_should_return_false_for_different_type() {
        TestVariantResult result1A = "hello";
        TestVariantResult result2  = 123;
        await Assert.That(result1A.Equals(result2)).IsFalse();
    }

    [Test]
    public async Task equals_operator_should_work_correctly() {
        TestVariantResult  result1A   = "hello";
        TestVariantResult  result1B   = "hello";
        TestVariantResult  result2    = "world";
        TestVariantResult  result3    = 123;
        TestVariantResult? nullResult = null;

        await Assert.That(result1A == result1B).IsTrue();
        await Assert.That(result1A == result2).IsFalse();
        await Assert.That(result1A == result3).IsFalse();
        await Assert.That(result1A == nullResult).IsFalse();
        await Assert.That(nullResult == result1A).IsFalse();

        TestVariantResult resultInt1 = 10;
        TestVariantResult resultInt2 = 10;
        await Assert.That(resultInt1 == resultInt2).IsTrue();
    }

    [Test]
    public async Task not_equals_operator_should_work_correctly() {
        TestVariantResult  result1A   = "hello";
        TestVariantResult  result1B   = "hello";
        TestVariantResult  result2    = "world";
        TestVariantResult? nullResult = null;

        await Assert.That(result1A != result1B).IsFalse();
        await Assert.That(result1A != result2).IsTrue();
        await Assert.That(result1A != nullResult).IsTrue();
        await Assert.That(nullResult != result1A).IsTrue();
    }

    [Test]
    public async Task get_hash_code_should_be_consistent_for_equal_objects() {
        TestVariantResult result1A = "hello";
        TestVariantResult result1B = "hello";
        await Assert.That(result1A.GetHashCode()).IsEqualTo(result1B.GetHashCode());

        TestVariantResult resultInt1 = 123;
        TestVariantResult resultInt2 = 123;
        await Assert.That(resultInt1.GetHashCode()).IsEqualTo(resultInt2.GetHashCode());
    }

    [Test]
    public async Task get_hash_code_should_generally_be_different_for_unequal_objects() {
        TestVariantResult result1 = "hello";
        TestVariantResult result2 = "world";
        TestVariantResult result3 = 123;

        // Note: Hash code collisions are possible but should be rare for distinct small inputs.
        await Assert.That(result1.GetHashCode()).IsNotEqualTo(result2.GetHashCode());
        await Assert.That(result1.GetHashCode()).IsNotEqualTo(result3.GetHashCode());
    }

    [Test]
    public async Task implicit_conversion_from_string_should_work() {
        TestVariantResult result = "implicit";
        await Assert.That(result.IsString).IsTrue();
        await Assert.That(result.AsString).IsEqualTo("implicit");
    }

    [Test]
    public async Task implicit_conversion_from_int_should_work() {
        TestVariantResult result = 789;
        await Assert.That(result.IsInt).IsTrue();
        await Assert.That(result.AsInt).IsEqualTo(789);
    }


    [Test]
    public async Task switch_should_execute_correct_action_for_string() {
        TestVariantResult result             = "switch_me";
        var                stringActionCalled = false;
        var                intActionCalled    = false;

        result.Switch(
            _ => stringActionCalled = true,
            _ => intActionCalled    = true
        );

        await Assert.That(stringActionCalled).IsTrue();
        await Assert.That(intActionCalled).IsFalse();
    }

    [Test]
    public async Task switch_should_execute_correct_action_for_int() {
        TestVariantResult result             = 555;
        var                stringActionCalled = false;
        var                intActionCalled    = false;

        result.Switch(
            _ => stringActionCalled = true,
            _ => intActionCalled    = true
        );

        await Assert.That(stringActionCalled).IsFalse();
        await Assert.That(intActionCalled).IsTrue();
    }

    [Test]
    public async Task match_should_return_correct_value_for_string() {
        TestVariantResult result = "match_me";
        var outcome = result.Match(
            s => $"String: {s}",
            i => $"Int: {i}"
        );

        await Assert.That(outcome).IsEqualTo("String: match_me");
    }

    [Test]
    public async Task match_should_return_correct_value_for_int() {
        TestVariantResult result = 777;
        var outcome = result.Match(
            s => $"String: {s}",
            i => $"Int: {i}"
        );

        await Assert.That(outcome).IsEqualTo("Int: 777");
    }

    // [Test]
    // public void try_pick_string_should_work() {
    //     TestVariantResult result = "pick_string";
    //
    //     await Assert.That(result.TryPickString(out var pickedString)).IsTrue();
    //     await Assert.That(pickedString).IsEqualTo("pick_string");
    //
    //     TestVariantResult intResult = 123;
    //     await Assert.That(intResult.TryPickString(out pickedString)).IsFalse();
    //     await Assert.That(pickedString).IsNull();
    // }
    //
    // [Test]
    // public void try_pick_int_should_work() {
    //     TestVariantResult result = 987;
    //
    //     await Assert.That(result.TryPickInt(out var pickedInt)).IsTrue();
    //     await Assert.That(pickedInt).IsEqualTo(987);
    //
    //     TestVariantResult stringResult = "not_an_int";
    //     await Assert.That(stringResult.TryPickInt(out pickedInt)).IsFalse();
    //     await Assert.That(pickedInt).IsEqualTo(0);
    // }

    [Test]
    public async Task complex_type_generation_is_my_error_should_be_true() {
        var                       error  = new MyError("E01", "Test Error");
        TestVariantComplexResult result = error;

        await Assert.That(result.IsMyError).IsTrue();
        await Assert.That(result.IsMySuccess).IsFalse();
        await Assert.That(result.IsMyWarning).IsFalse();
        await Assert.That(result.AsMyError).IsEqualTo(error);
    }

    [Test]
    public async Task complex_type_generation_is_my_success_should_be_true() {
        var                       success = new MySuccess(Guid.NewGuid());
        TestVariantComplexResult result  = success;

        await Assert.That(result.IsMyError).IsFalse();
        await Assert.That(result.IsMySuccess).IsTrue();
        await Assert.That(result.IsMyWarning).IsFalse();
        await Assert.That(result.AsMySuccess).IsEqualTo(success);
    }

    [Test]
    public async Task constructor_should_throw_argument_null_exception_for_null_reference_type() {
        await Assert.That(() => new TestVariantResult(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task constructor_should_not_throw_for_value_type_default() {
        // This test is tricky because int default is 0, which is valid.
        // The generator's null check is `if (parameterName == null)`, which doesn't apply to non-nullable value types.
        // So, this test effectively checks that the constructor for int doesn't throw.
        await Assert.That(() => new TestVariantResult(0)).ThrowsNothing();
        await Assert.That(() => new TestVariantResult(0)).ThrowsNothing();
    }

    // Case enum tests
    [Test]
    public async Task case_property_should_return_correct_enum_value_for_string() {
        TestVariantResult result = "test";
        await Assert.That(result.Case).IsEqualTo(TestVariantResult.TestVariantResultCase.String);
    }

    [Test]
    public async Task case_property_should_return_correct_enum_value_for_int() {
        TestVariantResult result = 123;
        await Assert.That(result.Case).IsEqualTo(TestVariantResult.TestVariantResultCase.Int);
    }

    // Async Switch tests
    [Test]
    public async Task switch_async_should_execute_correct_action_for_string() {
        TestVariantResult result = "async_test";
        var stringActionCalled = false;
        var intActionCalled = false;

        await result.SwitchAsync(
            async _ => { stringActionCalled = true; await ValueTask.CompletedTask; },
            async _ => { intActionCalled = true; await ValueTask.CompletedTask; }
        );

        await Assert.That(stringActionCalled).IsTrue();
        await Assert.That(intActionCalled).IsFalse();
    }

    // Async Match tests
    [Test]
    public async Task match_async_should_return_correct_value_for_string() {
        TestVariantResult result = "async_match";
        var outcome = await result.MatchAsync(
            async s => { await ValueTask.CompletedTask; return $"String: {s}"; },
            async i => { await ValueTask.CompletedTask; return $"Int: {i}"; }
        );

        await Assert.That(outcome).IsEqualTo("String: async_match");
    }

    // State parameter tests
    [Test]
    public async Task switch_with_state_should_pass_state_correctly() {
        TestVariantResult result = "state_test";
        var capturedState = "";
        var state = "test_state";

        result.Switch(
            (_, st) => capturedState = st,
            (_, _) => capturedState = "wrong",
            state
        );

        await Assert.That(capturedState).IsEqualTo("test_state");
    }
}
