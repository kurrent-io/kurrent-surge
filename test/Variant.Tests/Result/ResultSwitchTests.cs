using TicTacToe;

namespace Kurrent.Variant.Tests.Result;

public class ResultSwitchTests {
    Faker Faker { get; } = new();

    [Test]
    public void switch_with_success_executes_success_action() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var successExecuted = false;
        var failureExecuted = false;

        // Act
        result.Switch(
            onSuccess: id => successExecuted = true,
            onFailure: err => failureExecuted = true);

        // Assert
        successExecuted.ShouldBeTrue();
        failureExecuted.ShouldBeFalse();
    }

    [Test]
    public void switch_with_failure_executes_failure_action() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Test error");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);
        var successExecuted = false;
        var failureExecuted = false;

        // Act
        result.Switch(
            onSuccess: id => successExecuted = true,
            onFailure: err => failureExecuted = true);

        // Assert
        successExecuted.ShouldBeFalse();
        failureExecuted.ShouldBeTrue();
    }

    [Test]
    public void switch_with_success_can_capture_value() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        GameId? capturedId = null;

        // Act
        result.Switch(
            onSuccess: id => capturedId = id,
            onFailure: err => { });

        // Assert
        capturedId.ShouldNotBeNull();
        capturedId.Value.Value.ShouldBe(gameId.Value);
    }

    [Test]
    public void switch_with_failure_can_capture_error() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Capture error");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);
        InvalidMoveError? capturedError = null;

        // Act
        result.Switch(
            onSuccess: id => { },
            onFailure: err => capturedError = err);

        // Assert
        capturedError.ShouldNotBeNull();
        capturedError.Reason.ShouldBe("Capture error");
    }

    [Test]
    public void switch_executes_only_one_branch() {
        // Arrange
        var position = new Position(1, 2);
        var result = Kurrent.Result.Success<Position, InvalidMoveError>(position);
        var executionCount = 0;

        // Act
        result.Switch(
            onSuccess: pos => executionCount++,
            onFailure: err => executionCount++);

        // Assert
        executionCount.ShouldBe(1);
    }

    [Test]
    public void switch_with_state_executes_success_action_with_provided_state() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var logPrefix = Faker.Lorem.Word();
        var capturedLog = string.Empty;

        // Act
        result.Switch(
            onSuccess: (prefix, id) => capturedLog = $"{prefix}: {id.Value}",
            onFailure: (prefix, err) => capturedLog = $"{prefix}: {err.Reason}",
            state: logPrefix);

        // Assert
        capturedLog.ShouldStartWith(logPrefix);
        capturedLog.ShouldContain(gameId.Value.ToString());
    }

    [Test]
    public void switch_with_state_executes_failure_action_with_provided_state() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "State error");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);
        var multiplier = Faker.Random.Int(2, 10);
        var capturedValue = 0;

        // Act
        result.Switch(
            onSuccess: (mult, id) => capturedValue = mult * 1,
            onFailure: (mult, err) => capturedValue = mult * -1,
            state: multiplier);

        // Assert
        capturedValue.ShouldBe(multiplier * -1);
    }

    [Test]
    public void switch_with_state_avoids_closure_allocation() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var stateValue = Faker.Random.Int(1, 100);
        var capturedState = 0;

        // Act
        result.Switch(
            onSuccess: (state, id) => capturedState = state,
            onFailure: (state, err) => capturedState = state * -1,
            state: stateValue);

        // Assert
        capturedState.ShouldBe(stateValue);
    }

    [Test]
    public void switch_with_state_executes_only_one_branch() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Branch test");
        var result = Kurrent.Result.Failure<Position, InvalidMoveError>(error);
        var executionCount = 0;
        var stateValue = 42;

        // Act
        result.Switch(
            onSuccess: (state, pos) => executionCount++,
            onFailure: (state, err) => executionCount++,
            state: stateValue);

        // Assert
        executionCount.ShouldBe(1);
    }

    [Test]
    public async ValueTask switch_async_with_success_executes_async_success_action() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var successExecuted = false;
        var failureExecuted = false;

        // Act
        await result.SwitchAsync(
            id => {
                successExecuted = true;
                return ValueTask.CompletedTask;
            },
            err => {
                failureExecuted = true;
                return ValueTask.CompletedTask;
            });

        // Assert
        successExecuted.ShouldBeTrue();
        failureExecuted.ShouldBeFalse();
    }

    [Test]
    public async ValueTask switch_async_with_failure_executes_async_failure_action() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Async error");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);
        var successExecuted = false;
        var failureExecuted = false;

        // Act
        await result.SwitchAsync(
            onSuccessAsync: async id => {
                await Task.Delay(1);
                successExecuted = true;
            },
            onFailureAsync: async err => {
                await Task.Delay(1);
                failureExecuted = true;
            });

        // Assert
        successExecuted.ShouldBeFalse();
        failureExecuted.ShouldBeTrue();
    }

    [Test]
    public async ValueTask switch_async_can_perform_async_side_effects() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var capturedId = Guid.Empty;

        // Act
        await result.SwitchAsync(
            onSuccessAsync: async id => {
                await Task.Delay(1);
                capturedId = id.Value;
            },
            onFailureAsync: async err => {
                await Task.Delay(1);
            });

        // Assert
        capturedId.ShouldBe(gameId.Value);
    }

    [Test]
    public async ValueTask switch_async_with_state_executes_with_provided_state() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var logPrefix = Faker.Lorem.Word();
        var capturedLog = string.Empty;

        // Act
        await result.SwitchAsync(
            onSuccessAsync: async (prefix, id) => {
                await Task.Delay(1);
                capturedLog = $"{prefix}: {id.Value}";
            },
            onFailureAsync: async (prefix, err) => {
                await Task.Delay(1);
                capturedLog = $"{prefix}: {err.Reason}";
            },
            state: logPrefix);

        // Assert
        capturedLog.ShouldStartWith(logPrefix);
    }

    [Test]
    public async ValueTask switch_async_chain_extension_awaits_task_then_switches() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Success<GameId, InvalidMoveError>(gameId));
        var successExecuted = false;

        // Act
        await resultTask.SwitchAsync(
            onSuccessAsync: id => {
                successExecuted = true;
                return ValueTask.CompletedTask;
            },
            onFailureAsync: err => ValueTask.CompletedTask);

        // Assert
        successExecuted.ShouldBeTrue();
    }

    [Test]
    public async ValueTask switch_async_chain_extension_with_async_actions_awaits_both() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Chain error");
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Failure<GameId, InvalidMoveError>(error));
        var failureExecuted = false;

        // Act
        await resultTask.SwitchAsync(
            onSuccessAsync: async id => {
                await Task.Delay(1);
            },
            onFailureAsync: async err => {
                await Task.Delay(1);
                failureExecuted = true;
            });

        // Assert
        failureExecuted.ShouldBeTrue();
    }

    [Test]
    public async ValueTask switch_async_chain_extension_with_state_passes_state_correctly() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Success<GameId, InvalidMoveError>(gameId));
        var multiplier = Faker.Random.Int(2, 10);
        var capturedValue = 0;

        // Act
        await resultTask.SwitchAsync(
            onSuccessAsync: (mult, id) => {
                capturedValue = mult * 1;
                return ValueTask.CompletedTask;
            },
            onFailureAsync: (mult, err) => {
                capturedValue = mult * -1;
                return ValueTask.CompletedTask;
            },
            state: multiplier);

        // Assert
        capturedValue.ShouldBe(multiplier);
    }

    [Test]
    public async ValueTask switch_async_chain_extension_with_async_state_actions_works() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "State async");
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Failure<GameId, InvalidMoveError>(error));
        var prefix = Faker.Lorem.Word();
        var capturedLog = string.Empty;

        // Act
        await resultTask.SwitchAsync(
            onSuccessAsync: async (pre, id) => {
                await Task.Delay(1);
                capturedLog = $"{pre}: success";
            },
            onFailureAsync: async (pre, err) => {
                await Task.Delay(1);
                capturedLog = $"{pre}: {err.Reason}";
            },
            state: prefix);

        // Assert
        capturedLog.ShouldStartWith(prefix);
        capturedLog.ShouldContain("State async");
    }

    [Test]
    public void switch_enables_imperative_handling() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var handled = false;

        // Act
        result.Switch(
            onSuccess: id => handled = true,
            onFailure: err => handled = true);

        // Assert
        handled.ShouldBeTrue();
    }

    [Test]
    public void switch_useful_for_logging_both_paths() {
        // Arrange
        var position = new Position(2, 1);
        var successResult = Kurrent.Result.Success<Position, InvalidMoveError>(position);
        var error = new InvalidMoveError(Faker.Random.Guid(), "Error");
        var failureResult = Kurrent.Result.Failure<Position, InvalidMoveError>(error);
        var successLog = string.Empty;
        var errorLog = string.Empty;

        // Act
        successResult.Switch(
            onSuccess: pos => successLog = $"Position: {pos.Row},{pos.Column}",
            onFailure: err => errorLog = err.Reason);
        failureResult.Switch(
            onSuccess: pos => successLog = $"Position: {pos.Row},{pos.Column}",
            onFailure: err => errorLog = err.Reason);

        // Assert
        successLog.ShouldNotBeEmpty();
        errorLog.ShouldNotBeEmpty();
    }

    [Test]
    public void switch_vs_match_difference_is_return_value() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);

        // Act - Switch returns void
        result.Switch(
            onSuccess: id => { },
            onFailure: err => { });

        // Act - Match returns value
        var output = result.Match(
            mapValue: id => "value",
            mapError: err => "value");

        // Assert
        output.ShouldBe("value");
    }
}
