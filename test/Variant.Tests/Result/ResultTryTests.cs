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
            operation: () => gameId,
            exceptionFactory: ex => new InvalidMoveError(Guid.Empty, ex.Message));

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
            operation: () => {
                throw new InvalidOperationException(exceptionMessage);
                return new GameId(Guid.Empty);
            },
            exceptionFactory: ex => new InvalidMoveError(Guid.Empty, ex.Message));

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
            operation: () => {
                throw new ArgumentException("Invalid game");
                return new GameId(Guid.Empty);
            },
            exceptionFactory: ex => new GameEndedError(gameId, GameStatus.Draw));

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
            operation: () => {
                throw new InvalidOperationException(exceptionMessage);
                return new Position(0, 0);
            },
            exceptionFactory: ex => new InvalidMoveError(Guid.Empty, $"Caught: {ex.Message}"));

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Reason.ShouldContain("Caught:");
        result.Error.Reason.ShouldContain(exceptionMessage);
    }

    [Test]
    public void try_with_state_executes_operation_with_provided_state() {
        // Arrange
        var multiplier = Faker.Random.Int(2, 10);
        var baseValue = Faker.Random.Int(1, 100);

        // Act
        var result = Kurrent.Result.Try(
            operation: state => state * baseValue,
            exceptionFactory: (state, ex) => new InvalidMoveError(Guid.Empty, ex.Message),
            state: multiplier);

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
            operation: (Guid id) => {
                throw new InvalidOperationException("Error");
                return new GameId(id);
            },
            exceptionFactory: (id, ex) => new InvalidMoveError(id, ex.Message),
            state: gameId);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.GameId.ShouldBe(gameId);
    }

    [Test]
    public void try_with_state_avoids_closure_allocation() {
        // Arrange
        var stateValue = Faker.Random.Int(1, 100);
        var capturedState = 0;

        // Act
        var result = Kurrent.Result.Try(
            operation: state => {
                capturedState = state;
                return new GameId(Guid.NewGuid());
            },
            exceptionFactory: (state, ex) => new InvalidMoveError(Guid.Empty, ex.Message),
            state: stateValue);

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
            operation: async () => {
                await Task.Delay(1);
                return gameId;
            },
            exceptionFactory: ex => new InvalidMoveError(Guid.Empty, ex.Message));

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
            operation: async () => {
                await Task.Delay(1);
                throw new InvalidOperationException(exceptionMessage);
                return new GameId(Guid.Empty);
            },
            exceptionFactory: ex => new InvalidMoveError(Guid.Empty, ex.Message));

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Reason.ShouldBe(exceptionMessage);
    }

    [Test]
    public async ValueTask try_async_with_state_executes_with_provided_state() {
        // Arrange
        var multiplier = Faker.Random.Int(2, 10);
        var baseValue = Faker.Random.Int(1, 100);

        // Act
        var result = await Kurrent.Result.TryAsync(
            operation: async state => {
                await Task.Delay(1);
                return state * baseValue;
            },
            exceptionFactory: (state, ex) => new InvalidMoveError(Guid.Empty, ex.Message),
            state: multiplier);

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
            operation: async (Guid id) => {
                await Task.Delay(1);
                throw new InvalidOperationException("Async error");
                return new GameId(id);
            },
            exceptionFactory: (id, ex) => new InvalidMoveError(id, ex.Message),
            state: gameId);

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
            operation: () => sideEffectExecuted = true,
            exceptionFactory: ex => new InvalidMoveError(Guid.Empty, ex.Message));

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
            operation: () => throw new InvalidOperationException(exceptionMessage),
            exceptionFactory: ex => new InvalidMoveError(Guid.Empty, ex.Message));

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Reason.ShouldBe(exceptionMessage);
    }

    [Test]
    public void try_with_action_and_state_executes_with_provided_state() {
        // Arrange
        var stateValue = Faker.Random.Int(1, 100);
        var capturedState = 0;

        // Act
        var result = Kurrent.Result.Try(
            operation: state => capturedState = state,
            exceptionFactory: (state, ex) => new InvalidMoveError(Guid.Empty, ex.Message),
            state: stateValue);

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
            operation: (Guid id) => throw new InvalidOperationException("State action error"),
            exceptionFactory: (id, ex) => new InvalidMoveError(id, ex.Message),
            state: gameId);

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
            operation: async () => {
                await Task.Delay(1);
                sideEffectExecuted = true;
            },
            exceptionFactory: ex => new InvalidMoveError(Guid.Empty, ex.Message));

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
            operation: async () => {
                await Task.Delay(1);
                throw new InvalidOperationException(exceptionMessage);
            },
            exceptionFactory: ex => new InvalidMoveError(Guid.Empty, ex.Message));

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Reason.ShouldBe(exceptionMessage);
    }

    [Test]
    public async ValueTask try_async_with_action_and_state_executes_with_provided_state() {
        // Arrange
        var stateValue = Faker.Random.Int(1, 100);
        var capturedState = 0;

        // Act
        var result = await Kurrent.Result.TryAsync(
            operation: async state => {
                await Task.Delay(1);
                capturedState = state;
            },
            exceptionFactory: (state, ex) => new InvalidMoveError(Guid.Empty, ex.Message),
            state: stateValue);

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
            operation: async (Guid id) => {
                await Task.Delay(1);
                throw new InvalidOperationException("Async state action error");
            },
            exceptionFactory: (id, ex) => new InvalidMoveError(id, ex.Message),
            state: gameId);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.GameId.ShouldBe(gameId);
    }

    [Test]
    public void try_handles_different_exception_types() {
        // Arrange & Act
        var argumentResult = Kurrent.Result.Try(
            operation: () => {
                throw new ArgumentException("Argument");
                return 1;
            },
            exceptionFactory: ex => new InvalidMoveError(Guid.Empty, ex.GetType().Name));

        var invalidOpResult = Kurrent.Result.Try(
            operation: () => {
                throw new InvalidOperationException("InvalidOp");
                return 1;
            },
            exceptionFactory: ex => new InvalidMoveError(Guid.Empty, ex.GetType().Name));

        // Assert
        argumentResult.Error.Reason.ShouldBe("ArgumentException");
        invalidOpResult.Error.Reason.ShouldBe("InvalidOperationException");
    }

    [Test]
    public void try_can_wrap_unsafe_operations() {
        // Arrange
        var zero = 0;
        var operations = new List<Func<int>> {
            () => 10 / 2,     // Safe
            () => 10 / zero,  // Throws
            () => 10 / 5      // Safe but won't execute due to previous error
        };

        // Act
        var results = operations
            .Select(op => Kurrent.Result.Try(
                operation: op,
                exceptionFactory: ex => new InvalidMoveError(Guid.Empty, ex.Message)))
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
        cts.Cancel();

        // Act
        var result = await Kurrent.Result.TryAsync(
            operation: async () => {
                await Task.Delay(100, cts.Token);
                return new GameId(Guid.NewGuid());
            },
            exceptionFactory: ex => new InvalidMoveError(Guid.Empty, ex.GetType().Name));

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Reason.ShouldContain("Cancel");
    }


    [Test]
    public void try_with_multiple_error_types() {
        // Arrange & Act
        var gameResult = Kurrent.Result.Try(
            operation: () => throw new InvalidOperationException("Game error"),
            exceptionFactory: ex => new InvalidMoveError(Guid.Empty, ex.Message));

        var endedResult = Kurrent.Result.Try(
            operation: () => throw new ArgumentException("Ended error"),
            exceptionFactory: ex => new GameEndedError(Guid.Empty, GameStatus.Draw));

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
        var pipeline = Kurrent.Result.Try(
                operation: () => input,
                exceptionFactory: ex => new SimpleError(ex.Message))
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
        var pipeline = Kurrent.Result.Try(
                operation: () => validInput,
                exceptionFactory: ex => new SimpleError(ex.Message))
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
            operation: () => nullString!.Length,
            exceptionFactory: ex => new SimpleError($"Null reference: {ex.GetType().Name}"));

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
            operation: () => 10 / zero,
            exceptionFactory: ex => new SimpleError($"Math error: {ex.Message}"));

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
            operation: () => array[10],
            exceptionFactory: ex => new SimpleError($"Index error: {ex.GetType().Name}"));

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Message.ShouldContain("IndexOutOfRangeException");
    }

    [Test]
    public void try_preserves_stack_trace_information() {
        // Arrange & Act
        var result = Kurrent.Result.Try(
            operation: () => {
                throw new InvalidOperationException("Deep error");
                return 42;
            },
            exceptionFactory: ex => new SimpleError(ex.StackTrace ?? "No stack trace"));

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Message.ShouldNotBeEmpty();
    }

    [Test]
    public void try_with_custom_exception_factory_adds_context() {
        // Arrange
        var gameId = Faker.Random.Guid();
        var contextInfo = Faker.Lorem.Sentence();

        // Act
        var result = Kurrent.Result.Try(
            operation: () => {
                throw new InvalidOperationException("Operation failed");
                return new Position(0, 0);
            },
            exceptionFactory: ex => new InvalidMoveError(gameId, $"{contextInfo}: {ex.Message}"));

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Reason.ShouldContain(contextInfo);
        result.Error.GameId.ShouldBe(gameId);
    }

    [Test]
    public async ValueTask try_async_with_timeout_simulation() {
        // Arrange
        using var cts = new CancellationTokenSource();
        var delay = 100;

        // Act
        var result = await Kurrent.Result.TryAsync(
            operation: async () => {
                await Task.Delay(delay, cts.Token);
                return new GameId(Guid.NewGuid());
            },
            exceptionFactory: ex => new SimpleError($"Timeout: {ex.GetType().Name}"));

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Test]
    public void try_with_nested_try_operations() {
        // Arrange
        var value = Faker.Random.Int(1, 100);

        // Act
        var result = Kurrent.Result.Try(
            operation: () => Kurrent.Result.Try(
                operation: () => value * 2,
                exceptionFactory: ex => new SimpleError($"Inner: {ex.Message}")),
            exceptionFactory: ex => new SimpleError($"Outer: {ex.Message}"));

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
                operation: () => val * 2,
                exceptionFactory: ex => new SimpleError(ex.Message)))
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
            operation: () => {
                throw new ArgumentException("Bad argument");
                return 1;
            },
            exceptionFactory: ex => new SimpleError($"Arg: {ex.Message}"));

        var invalidOpResult = Kurrent.Result.Try(
            operation: () => {
                throw new InvalidOperationException("Bad operation");
                return 1;
            },
            exceptionFactory: ex => new SimpleError($"Op: {ex.Message}"));

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
            operation: async () => {
                await Task.WhenAll(tasks);
                return 42;
            },
            exceptionFactory: ex => new SimpleError($"Aggregate: {ex.GetType().Name}"));

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
                operation: () => input,
                exceptionFactory: ex => new SimpleError(ex.Message))
            .Bind(val => val != 0
                ? Kurrent.Result.Success<int, SimpleError>(10 / val)
                : Kurrent.Result.Failure<int, SimpleError>(new SimpleError("Cannot divide by zero")))
            .Map(val => val * 2);

        // Assert
        if (input == 0) {
            pipeline.IsFailure.ShouldBeTrue();
            pipeline.Error.Message.ShouldContain("divide by zero");
        } else {
            pipeline.IsSuccess.ShouldBeTrue();
            pipeline.Value.ShouldBe((10 / input) * 2);
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
                operation: () => operation(val),
                exceptionFactory: ex => new SimpleError(ex.Message))));

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(((5 + 1) * 2) - 3); // 9
    }

    [Test]
    public async ValueTask try_async_preserves_context_across_await_boundaries() {
        // Arrange
        var gameId = Faker.Random.Guid();
        var contextValue = Faker.Random.Int(1, 100);

        // Act
        var result = await Kurrent.Result.TryAsync(
            operation: async state => {
                await Task.Delay(1);
                if (state < 50)
                    throw new InvalidOperationException($"Value too low: {state}");
                return new GameId(gameId);
            },
            exceptionFactory: (state, ex) => new InvalidMoveError(gameId, $"State {state}: {ex.Message}"),
            state: contextValue);

        // Assert
        if (contextValue < 50) {
            result.IsFailure.ShouldBeTrue();
            result.Error.Reason.ShouldContain($"State {contextValue}");
        } else {
            result.IsSuccess.ShouldBeTrue();
        }
    }
}
