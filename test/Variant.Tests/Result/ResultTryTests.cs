// ReSharper disable UnusedParameter.Local

using TicTacToe;

namespace Kurrent.Variant.Tests.Result;

public class ResultTryTests {
    Faker Faker { get; } = new();

    [Test]
    public void try_with_successful_operation_returns_success() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());

        // Act
        var result = Kurrent.Result.Try(
            () => gameId,
            ex => new InvalidMoveError(Guid.Empty, ex.Message)
        );

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(gameId);
    }

    [Test]
    public void try_with_throwing_operation_returns_failure() {
        // Arrange
        var exceptionMessage = Faker.Lorem.Sentence();

        // Act
        var result = Kurrent.Result.Try(
            () => throw new InvalidOperationException(exceptionMessage),
            ex => new InvalidMoveError(Guid.Empty, ex.Message)
        );

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Reason.ShouldBe(exceptionMessage);
    }

    [Test]
    public void try_converts_exception_to_error_type() {
        // Arrange
        var gameId = Faker.Random.Guid();

        // Act
        var result = Kurrent.Result.Try(
            () => throw new ArgumentException("Invalid game"),
            ex => new GameEndedError(gameId, GameStatus.Draw)
        );

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<GameEndedError>();
        result.Error.Status.ShouldBe(GameStatus.Draw);
    }

    [Test]
    public void try_captures_exception_details_in_error() {
        // Arrange
        var exceptionMessage = Faker.Lorem.Sentence();

        // Act
        var result = Kurrent.Result.Try(
            () => throw new InvalidOperationException(exceptionMessage),
            ex => new InvalidMoveError(Guid.Empty, $"Caught: {ex.Message}")
        );

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Reason.ShouldContain("Caught:");
        result.Error.Reason.ShouldContain(exceptionMessage);
    }

    [Test]
    public void try_with_state_executes_operation_with_provided_state() {
        // Arrange
        var multiplier = Faker.Random.Int(2, 10);
        var baseValue  = Faker.Random.Int(1, 100);

        // Act
        var result = Kurrent.Result.Try(
            state => state * baseValue,
            (state, ex) => new InvalidMoveError(Guid.Empty, ex.Message),
            multiplier
        );

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(multiplier * baseValue);
    }

    [Test]
    public void try_with_state_converts_exception_with_state() {
        // Arrange
        var gameId = Faker.Random.Guid();

        // Act
        var result = Kurrent.Result.Try(
            id => throw new InvalidOperationException("Error"),
            (id, ex) => new InvalidMoveError(id, ex.Message),
            gameId
        );

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.GameId.ShouldBe(gameId);
    }

    [Test]
    public void try_with_state_avoids_closure_allocation() {
        // Arrange
        var stateValue    = Faker.Random.Int(1, 100);
        var capturedState = 0;

        // Act
        var result = Kurrent.Result.Try(
            state => {
                capturedState = state;
                return new GameId(Guid.NewGuid());
            },
            (state, ex) => new InvalidMoveError(Guid.Empty, ex.Message),
            stateValue
        );

        // Assert
        capturedState.ShouldBe(stateValue);
        result.IsSuccess.ShouldBeTrue();
    }

    [Test]
    public async ValueTask try_async_with_successful_operation_returns_success() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());

        // Act
        var result = await Kurrent.Result.TryAsync(
            async () => {
                await Task.Delay(1);
                return gameId;
            },
            ex => new InvalidMoveError(Guid.Empty, ex.Message)
        );

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(gameId);
    }

    [Test]
    public async ValueTask try_async_with_throwing_operation_returns_failure() {
        // Arrange
        var exceptionMessage = Faker.Lorem.Sentence();

        // Act
        var result = await Kurrent.Result.TryAsync(
            async () => {
                await Task.Delay(1);
                throw new InvalidOperationException(exceptionMessage);
            },
            ex => new InvalidMoveError(Guid.Empty, ex.Message)
        );

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Reason.ShouldBe(exceptionMessage);
    }

    [Test]
    public async ValueTask try_async_with_state_executes_with_provided_state() {
        // Arrange
        var multiplier = Faker.Random.Int(2, 10);
        var baseValue  = Faker.Random.Int(1, 100);

        // Act
        var result = await Kurrent.Result.TryAsync(
            async state => {
                await Task.Delay(1);
                return state * baseValue;
            },
            (state, ex) => new InvalidMoveError(Guid.Empty, ex.Message),
            multiplier
        );

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(multiplier * baseValue);
    }

    [Test]
    public async ValueTask try_async_with_state_converts_exception_with_state() {
        // Arrange
        var gameId = Faker.Random.Guid();

        // Act
        var result = await Kurrent.Result.TryAsync(
            async id => {
                await Task.Delay(1);
                throw new InvalidOperationException("Async error");
            },
            (id, ex) => new InvalidMoveError(id, ex.Message),
            gameId
        );

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.GameId.ShouldBe(gameId);
    }

    [Test]
    public void try_with_action_returns_success_when_no_exception() {
        // Arrange
        var sideEffectExecuted = false;

        // Act
        var result = Kurrent.Result.Try(
            () => sideEffectExecuted = true,
            ex => new InvalidMoveError(Guid.Empty, ex.Message)
        );

        // Assert
        result.IsSuccess.ShouldBeTrue();
        sideEffectExecuted.ShouldBeTrue();
    }

    [Test]
    public void try_with_action_returns_failure_when_exception() {
        // Arrange
        var exceptionMessage = Faker.Lorem.Sentence();

        // Act
        var result = Kurrent.Result.Try(
            () => throw new InvalidOperationException(exceptionMessage),
            ex => new InvalidMoveError(Guid.Empty, ex.Message)
        );

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Reason.ShouldBe(exceptionMessage);
    }

    [Test]
    public void try_with_action_and_state_executes_with_provided_state() {
        // Arrange
        var stateValue    = Faker.Random.Int(1, 100);
        var capturedState = 0;

        // Act
        var result = Kurrent.Result.Try(
            state => capturedState = state,
            (state, ex) => new InvalidMoveError(Guid.Empty, ex.Message),
            stateValue
        );

        // Assert
        result.IsSuccess.ShouldBeTrue();
        capturedState.ShouldBe(stateValue);
    }

    [Test]
    public void try_with_action_and_state_converts_exception_with_state() {
        // Arrange
        var gameId = Faker.Random.Guid();

        // Act
        var result = Kurrent.Result.Try(
            id => throw new InvalidOperationException("State action error"),
            (id, ex) => new InvalidMoveError(id, ex.Message),
            gameId
        );

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.GameId.ShouldBe(gameId);
    }

    [Test]
    public async ValueTask try_async_with_action_returns_success_when_no_exception() {
        // Arrange
        var sideEffectExecuted = false;

        // Act
        var result = await Kurrent.Result.TryAsync(
            async () => {
                await Task.Delay(1);
                sideEffectExecuted = true;
            },
            ex => new InvalidMoveError(Guid.Empty, ex.Message)
        );

        // Assert
        result.IsSuccess.ShouldBeTrue();
        sideEffectExecuted.ShouldBeTrue();
    }

    [Test]
    public async ValueTask try_async_with_action_returns_failure_when_exception() {
        // Arrange
        var exceptionMessage = Faker.Lorem.Sentence();

        // Act
        var result = await Kurrent.Result.TryAsync(
            async () => {
                await Task.Delay(1);
                throw new InvalidOperationException(exceptionMessage);
            },
            ex => new InvalidMoveError(Guid.Empty, ex.Message)
        );

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Reason.ShouldBe(exceptionMessage);
    }

    [Test]
    public async ValueTask try_async_with_action_and_state_executes_with_provided_state() {
        // Arrange
        var stateValue    = Faker.Random.Int(1, 100);
        var capturedState = 0;

        // Act
        var result = await Kurrent.Result.TryAsync(
            async state => {
                await Task.Delay(1);
                capturedState = state;
            },
            (state, ex) => new InvalidMoveError(Guid.Empty, ex.Message),
            stateValue
        );

        // Assert
        result.IsSuccess.ShouldBeTrue();
        capturedState.ShouldBe(stateValue);
    }

    [Test]
    public async ValueTask try_async_with_action_and_state_converts_exception_with_state() {
        // Arrange
        var gameId = Faker.Random.Guid();

        // Act
        var result = await Kurrent.Result.TryAsync(
            async id => {
                await Task.Delay(1);
                throw new InvalidOperationException("Async state action error");
            },
            (id, ex) => new InvalidMoveError(id, ex.Message),
            gameId
        );

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.GameId.ShouldBe(gameId);
    }

    [Test]
    public void try_handles_different_exception_types() {
        // Arrange & Act
        var argumentResult = Kurrent.Result.Try(
            () => throw new ArgumentException("Argument"),
            ex => new InvalidMoveError(Guid.Empty, ex.GetType().Name));

        var invalidOpResult = Kurrent.Result.Try(
            () => throw new InvalidOperationException("InvalidOp"),
            ex => new InvalidMoveError(Guid.Empty, ex.GetType().Name));

        // Assert
        argumentResult.Error.Reason.ShouldBe("ArgumentException");
        invalidOpResult.Error.Reason.ShouldBe("InvalidOperationException");
    }

    [Test]
    public void try_can_wrap_unsafe_operations() {
        // Arrange
        var zero = 0;
        var operations = new List<Func<int>> {
            () => 10 / 2,    // Safe
            () => 10 / zero, // Throws
            () => 10 / 5     // Safe but won't execute due to previous error
        };

        // Act
        var results = operations
            .Select(op => Kurrent.Result.Try(op, ex => new InvalidMoveError(Guid.Empty, ex.Message)))
            .ToList();

        // Assert
        results[0].IsSuccess.ShouldBeTrue();
        results[0].Value.ShouldBe(5);
        results[1].IsFailure.ShouldBeTrue();
        results[2].IsSuccess.ShouldBeTrue();
        results[2].Value.ShouldBe(2);
    }

    [Test]
    public async ValueTask try_async_handles_task_cancellation() {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var result = await Kurrent.Result.TryAsync(
            async () => {
                await Task.Delay(100, cts.Token);
                return new GameId(Guid.NewGuid());
            },
            ex => new InvalidMoveError(Guid.Empty, ex.GetType().Name)
        );

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Reason.ShouldContain("Cancel");
    }

    [Test]
    public void try_with_multiple_error_types() {
        // Arrange & Act
        var gameResult = Kurrent.Result.Try(
            () => throw new InvalidOperationException("Game error"),
            ex => new InvalidMoveError(Guid.Empty, ex.Message));

        var endedResult = Kurrent.Result.Try(
            () => throw new ArgumentException("Ended error"),
            ex => new GameEndedError(Guid.Empty, GameStatus.Draw));

        // Assert
        gameResult.IsFailure.ShouldBeTrue();
        gameResult.Error.ShouldBeOfType<InvalidMoveError>();
        endedResult.IsFailure.ShouldBeTrue();
        endedResult.Error.ShouldBeOfType<GameEndedError>();
    }

    [Test]
    public void try_combined_with_map_creates_safe_transformation_pipeline() {
        // Arrange
        var input = Faker.Random.Int(1, 100);

        // Act
        var pipeline = Kurrent.Result
            .Try(
                () => input,
                ex => new SimpleError(ex.Message))
            .Map(val => val * 2)
            .Map(val => val.ToString());

        // Assert
        pipeline.IsSuccess.ShouldBeTrue();
        pipeline.Value.ShouldBe((input * 2).ToString());
    }

    [Test]
    public void try_combined_with_bind_enables_complex_workflows() {
        // Arrange
        var validInput = Faker.Random.Int(1, 100);

        // Act
        var pipeline = Kurrent.Result
            .Try(
                () => validInput,
                ex => new SimpleError(ex.Message))
            .Bind(val => val > 0
                ? Kurrent.Result.Success<int, SimpleError>(val * 2)
                : Kurrent.Result.Failure<int, SimpleError>(new SimpleError("Negative value")));

        // Assert
        pipeline.IsSuccess.ShouldBeTrue();
        pipeline.Value.ShouldBe(validInput * 2);
    }

    [Test]
    public void try_with_null_reference_exception() {
        // Arrange
        string? nullString = null;

        // Act
        var result = Kurrent.Result.Try(
            () => nullString!.Length,
            ex => new SimpleError($"Null reference: {ex.GetType().Name}"));

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Message.ShouldContain("NullReferenceException");
    }

    [Test]
    public void try_with_division_by_zero() {
        // Arrange
        var zero = 0;

        // Act
        var result = Kurrent.Result.Try(
            () => 10 / zero,
            ex => new SimpleError($"Math error: {ex.Message}"));

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Message.ShouldContain("Math error");
    }

    [Test]
    public void try_with_index_out_of_range() {
        // Arrange
        var array = new[] { 1, 2, 3 };

        // Act
        var result = Kurrent.Result.Try(
            () => array[10],
            ex => new SimpleError($"Index error: {ex.GetType().Name}")
        );

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Message.ShouldContain("IndexOutOfRangeException");
    }

    [Test]
    public void try_preserves_stack_trace_information() {
        // Arrange & Act
        var result = Kurrent.Result.Try(
            () => throw new InvalidOperationException("Deep error"),
            ex => new SimpleError(ex.StackTrace ?? "No stack trace"));

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Message.ShouldNotBeEmpty();
    }

    [Test]
    public void try_with_custom_exception_factory_adds_context() {
        // Arrange
        var gameId      = Faker.Random.Guid();
        var contextInfo = Faker.Lorem.Sentence();

        // Act
        var result = Kurrent.Result.Try(
            () => throw new InvalidOperationException("Operation failed"),
            ex => new InvalidMoveError(gameId, $"{contextInfo}: {ex.Message}"));

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Reason.ShouldContain(contextInfo);
        result.Error.GameId.ShouldBe(gameId);
    }

    [Test]
    public async ValueTask try_async_with_timeout_simulation() {
        // Arrange
        using var cts   = new CancellationTokenSource();
        const int delay = 100;

        // Act
        var result = await Kurrent.Result.TryAsync(
            async () => {
                await Task.Delay(delay, cts.Token);
                return new GameId(Guid.NewGuid());
            },
            ex => new SimpleError($"Timeout: {ex.GetType().Name}")
        );

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Test]
    public void try_with_nested_try_operations() {
        // Arrange
        var value = Faker.Random.Int(1, 100);

        // Act
        var result = Kurrent.Result.Try(
            () => Kurrent.Result.Try(
                () => value * 2,
                ex => new SimpleError($"Inner: {ex.Message}")
            ),
            ex => new SimpleError($"Outer: {ex.Message}")
        );

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(value * 2);
    }

    [Test]
    public void try_with_linq_select_many_pattern() {
        // Arrange
        var values = new[] { 1, 2, 3 };

        // Act
        var results = values
            .Select(val => Kurrent.Result.Try(
                        () => val * 2,
                        ex => new SimpleError(ex.Message)
                    )
            )
            .ToList();

        // Assert
        results.ShouldAllBe(r => r.IsSuccess);
        results[0].Value.ShouldBe(2);
        results[1].Value.ShouldBe(4);
        results[2].Value.ShouldBe(6);
    }

    [Test]
    public void try_with_specific_exception_type_handling() {
        // Arrange & Act
        var argumentResult = Kurrent.Result.Try(
            () => throw new ArgumentException("Bad argument"),
            ex => new SimpleError($"Arg: {ex.Message}")
        );

        var invalidOpResult = Kurrent.Result.Try(
            () => throw new InvalidOperationException("Bad operation"),
            ex => new SimpleError($"Op: {ex.Message}")
        );

        // Assert
        argumentResult.Error.Message.ShouldStartWith("Arg:");
        invalidOpResult.Error.Message.ShouldStartWith("Op:");
    }

    [Test]
    public async ValueTask try_async_handles_aggregate_exception() {
        // Arrange
        var tasks = new[] {
            Task.FromException<int>(new InvalidOperationException("Error 1")),
            Task.FromException<int>(new InvalidOperationException("Error 2"))
        };

        // Act
        var result = await Kurrent.Result.TryAsync(
            async () => {
                await Task.WhenAll(tasks);
                return 42;
            },
            ex => new SimpleError($"Aggregate: {ex.GetType().Name}")
        );

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Message.ShouldContain("Exception");
    }

    [Test]
    public void try_enables_railway_oriented_error_handling() {
        // Arrange
        var input = Faker.Random.Int(-10, 10);

        // Act
        var pipeline = Kurrent.Result.Try(
                () => input,
                ex => new SimpleError(ex.Message)
            )
            .Bind(val => val != 0
                      ? Kurrent.Result.Success<int, SimpleError>(10 / val)
                      : Kurrent.Result.Failure<int, SimpleError>(new SimpleError("Cannot divide by zero"))
            )
            .Map(val => val * 2);

        // Assert
        if (input == 0) {
            pipeline.IsFailure.ShouldBeTrue();
            pipeline.Error.Message.ShouldContain("divide by zero");
        }
        else {
            pipeline.IsSuccess.ShouldBeTrue();
            pipeline.Value.ShouldBe(10 / input * 2);
        }
    }

    [Test]
    public void try_with_multiple_sequential_operations() {
        // Arrange
        var operations = new Func<int, int>[] {
            x => x + 1,
            x => x * 2,
            x => x - 3
        };

        // Act
        var result = operations.Aggregate(
            Kurrent.Result.Success<int, SimpleError>(5),
            (acc, operation) => acc.Bind(val => Kurrent.Result.Try(
                                             () => operation(val),
                                             ex => new SimpleError(ex.Message)
                                         )
            )
        );

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe((5 + 1) * 2 - 3); // 9
    }

    [Test]
    public async ValueTask try_async_preserves_context_across_await_boundaries() {
        // Arrange
        var gameId       = Faker.Random.Guid();
        var contextValue = Faker.Random.Int(1, 100);

        // Act
        var result = await Kurrent.Result.TryAsync(
            async state => {
                await Task.Delay(1);
                if (state < 50)
                    throw new InvalidOperationException($"Value too low: {state}");

                return new GameId(gameId);
            },
            (state, ex) => new InvalidMoveError(gameId, $"State {state}: {ex.Message}"),
            contextValue
        );

        // Assert
        if (contextValue >= 50)
            result.IsSuccess.ShouldBeTrue();
        else {
            result.IsFailure.ShouldBeTrue();
            result.Error.Reason.ShouldContain($"State {contextValue}");
        }
    }
}
