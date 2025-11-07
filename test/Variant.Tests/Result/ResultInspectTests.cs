using Kurrent;
using TicTacToe;

namespace Kurrent.Variant.Tests.Result;

public class ResultInspectTests {
    Faker Faker { get; } = new();

    [Test]
    public void inspect_executes_action_when_result_is_success() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var actionExecuted = false;

        // Act
        var returned = result.Inspect(id => actionExecuted = true);

        // Assert
        actionExecuted.ShouldBeTrue();
        returned.ShouldBe(result);
        returned.IsSuccess.ShouldBeTrue();
    }

    [Test]
    public void inspect_does_not_execute_action_when_result_is_failure() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Error");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);
        var actionExecuted = false;

        // Act
        var returned = result.Inspect(id => actionExecuted = true);

        // Assert
        actionExecuted.ShouldBeFalse();
        returned.ShouldBe(result);
        returned.IsFailure.ShouldBeTrue();
    }

    [Test]
    public void inspect_returns_same_result_for_chaining() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);

        // Act
        var returned = result.Inspect(id => { });

        // Assert
        returned.Value.ShouldBe(gameId);
        returned.IsSuccess.ShouldBeTrue();
    }

    [Test]
    public void inspect_can_capture_side_effects() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        GameId? capturedId = null;

        // Act
        result.Inspect(id => capturedId = id);

        // Assert
        capturedId.ShouldNotBeNull();
        capturedId.Value.Value.ShouldBe(gameId.Value);
    }

    [Test]
    public void inspect_chains_multiple_actions() {
        // Arrange
        var position = new Position(1, 2);
        var result = Kurrent.Result.Success<Position, InvalidMoveError>(position);
        var firstExecuted = false;
        var secondExecuted = false;

        // Act
        var final = result
            .Inspect(pos => firstExecuted = true)
            .Inspect(pos => secondExecuted = true);

        // Assert
        firstExecuted.ShouldBeTrue();
        secondExecuted.ShouldBeTrue();
        final.IsSuccess.ShouldBeTrue();
    }

    [Test]
    public void inspect_with_state_executes_action_with_provided_state() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var logMessage = Faker.Lorem.Sentence();
        var capturedLog = string.Empty;

        // Act
        var returned = result.Inspect((log, id) => capturedLog = log, logMessage);

        // Assert
        capturedLog.ShouldBe(logMessage);
        returned.IsSuccess.ShouldBeTrue();
    }

    [Test]
    public void inspect_with_state_does_not_execute_when_failure() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "State error");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);
        var stateValue = Faker.Random.Int();
        var actionExecuted = false;

        // Act
        var returned = result.Inspect((id, state) => actionExecuted = true, stateValue);

        // Assert
        actionExecuted.ShouldBeFalse();
        returned.IsFailure.ShouldBeTrue();
    }

    [Test]
    public void inspect_with_state_avoids_closure_allocation() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var stateValue = Faker.Random.Int(1, 100);
        var capturedState = 0;

        // Act
        result.Inspect((state, id) => capturedState = state, stateValue);

        // Assert
        capturedState.ShouldBe(stateValue);
    }

    [Test]
    public void inspect_error_executes_action_when_result_is_failure() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Test error");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);
        var actionExecuted = false;

        // Act
        var returned = result.InspectError(err => actionExecuted = true);

        // Assert
        actionExecuted.ShouldBeTrue();
        returned.ShouldBe(result);
        returned.IsFailure.ShouldBeTrue();
    }

    [Test]
    public void inspect_error_does_not_execute_action_when_result_is_success() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var actionExecuted = false;

        // Act
        var returned = result.InspectError(err => actionExecuted = true);

        // Assert
        actionExecuted.ShouldBeFalse();
        returned.ShouldBe(result);
        returned.IsSuccess.ShouldBeTrue();
    }

    [Test]
    public void inspect_error_can_capture_error_details() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Capture this");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);
        InvalidMoveError? capturedError = null;

        // Act
        result.InspectError(err => capturedError = err);

        // Assert
        capturedError.ShouldNotBeNull();
        capturedError.Reason.ShouldBe("Capture this");
    }

    [Test]
    public void inspect_error_returns_same_result_for_chaining() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Error");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);

        // Act
        var returned = result.InspectError(err => { });

        // Assert
        returned.Error.ShouldBe(error);
        returned.IsFailure.ShouldBeTrue();
    }

    [Test]
    public void inspect_error_chains_multiple_actions() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Chain error");
        var result = Kurrent.Result.Failure<Position, InvalidMoveError>(error);
        var firstExecuted = false;
        var secondExecuted = false;

        // Act
        var final = result
            .InspectError(err => firstExecuted = true)
            .InspectError(err => secondExecuted = true);

        // Assert
        firstExecuted.ShouldBeTrue();
        secondExecuted.ShouldBeTrue();
        final.IsFailure.ShouldBeTrue();
    }

    [Test]
    public void inspect_error_with_state_executes_action_with_provided_state() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "State failure");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);
        var errorCount = Faker.Random.Int(1, 10);
        var capturedCount = 0;

        // Act
        var returned = result.InspectError((count, err) => capturedCount = count, errorCount);

        // Assert
        capturedCount.ShouldBe(errorCount);
        returned.IsFailure.ShouldBeTrue();
    }

    [Test]
    public void inspect_error_with_state_does_not_execute_when_success() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var stateValue = Faker.Random.Int();
        var actionExecuted = false;

        // Act
        var returned = result.InspectError((state, err) => actionExecuted = true, stateValue);

        // Assert
        actionExecuted.ShouldBeFalse();
        returned.IsSuccess.ShouldBeTrue();
    }

    [Test]
    public void inspect_and_inspect_error_can_be_combined() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var successExecuted = false;
        var failureExecuted = false;

        // Act
        var returned = result
            .Inspect(id => successExecuted = true)
            .InspectError(err => failureExecuted = true);

        // Assert
        successExecuted.ShouldBeTrue();
        failureExecuted.ShouldBeFalse();
        returned.IsSuccess.ShouldBeTrue();
    }

    [Test]
    public void inspect_error_and_inspect_can_be_combined() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Combined");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);
        var successExecuted = false;
        var failureExecuted = false;

        // Act
        var returned = result
            .InspectError(err => failureExecuted = true)
            .Inspect(id => successExecuted = true);

        // Assert
        successExecuted.ShouldBeFalse();
        failureExecuted.ShouldBeTrue();
        returned.IsFailure.ShouldBeTrue();
    }

    [Test]
    public async ValueTask inspect_async_executes_async_action_when_success() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var actionExecuted = false;

        // Act
        var returned = await result.InspectAsync(async id => {
            await Task.Delay(1);
            actionExecuted = true;
        });

        // Assert
        actionExecuted.ShouldBeTrue();
        returned.IsSuccess.ShouldBeTrue();
    }

    [Test]
    public async ValueTask inspect_async_does_not_execute_when_failure() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Async error");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);
        var actionExecuted = false;

        // Act
        var returned = await result.InspectAsync(async id => {
            await Task.Delay(1);
            actionExecuted = true;
        });

        // Assert
        actionExecuted.ShouldBeFalse();
        returned.IsFailure.ShouldBeTrue();
    }

    [Test]
    public async ValueTask inspect_async_with_state_executes_with_provided_state() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var logMessage = Faker.Lorem.Sentence();
        var capturedLog = string.Empty;

        // Act
        await result.InspectAsync(async (log, id) => {
            await Task.Delay(1);
            capturedLog = log;
        }, logMessage);

        // Assert
        capturedLog.ShouldBe(logMessage);
    }

    [Test]
    public async ValueTask inspect_error_async_executes_async_action_when_failure() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Async failure");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);
        var actionExecuted = false;

        // Act
        var returned = await result.InspectErrorAsync(async err => {
            await Task.Delay(1);
            actionExecuted = true;
        });

        // Assert
        actionExecuted.ShouldBeTrue();
        returned.IsFailure.ShouldBeTrue();
    }

    [Test]
    public async ValueTask inspect_error_async_does_not_execute_when_success() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var actionExecuted = false;

        // Act
        var returned = await result.InspectErrorAsync(async err => {
            await Task.Delay(1);
            actionExecuted = true;
        });

        // Assert
        actionExecuted.ShouldBeFalse();
        returned.IsSuccess.ShouldBeTrue();
    }

    [Test]
    public async ValueTask inspect_error_async_with_state_executes_with_provided_state() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "State async");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);
        var errorCount = Faker.Random.Int(1, 10);
        var capturedCount = 0;

        // Act
        await result.InspectErrorAsync(async (count, err) => {
            await Task.Delay(1);
            capturedCount = count;
        }, errorCount);

        // Assert
        capturedCount.ShouldBe(errorCount);
    }

    [Test]
    public async ValueTask inspect_async_chain_extension_awaits_task_then_executes() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Success<GameId, InvalidMoveError>(gameId));
        var actionExecuted = false;

        // Act
        var returned = await resultTask.InspectAsync(id => {
            actionExecuted = true;
            return ValueTask.CompletedTask;
        });

        // Assert
        actionExecuted.ShouldBeTrue();
        returned.IsSuccess.ShouldBeTrue();
    }

    [Test]
    public async ValueTask inspect_error_async_chain_extension_awaits_task_then_executes() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Chain error");
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Failure<GameId, InvalidMoveError>(error));
        var actionExecuted = false;

        // Act
        var returned = await resultTask.InspectErrorAsync(err => {
            actionExecuted = true;
            return ValueTask.CompletedTask;
        });

        // Assert
        actionExecuted.ShouldBeTrue();
        returned.IsFailure.ShouldBeTrue();
    }

    [Test]
    public void inspect_executes_action_and_returns_same_instance_when_called_on_success_result() {
        // Arrange
        GameStarted successValue = new GameStarted(Faker.Random.Guid(), Faker.PickRandom<Player>());
        Result<GameStarted, InvalidMoveError> originalResult = Kurrent.Result.Success<GameStarted, InvalidMoveError>(successValue);
        bool actionExecuted = false;

        // Act
        Result<GameStarted, InvalidMoveError> returnedResult = originalResult.Inspect(_ => actionExecuted = true);

        // Assert
        actionExecuted.ShouldBeTrue();
        returnedResult.ShouldBeEquivalentTo(originalResult);
    }

    [Test]
    public void inspect_does_not_execute_action_and_returns_same_instance_when_called_on_error_result() {
        // Arrange
        InvalidMoveError errorValue = new InvalidMoveError(Faker.Random.Guid(), Faker.Lorem.Sentence());
        Result<GameStarted, InvalidMoveError> originalResult = Kurrent.Result.Failure<GameStarted, InvalidMoveError>(errorValue);
        bool actionExecuted = false;

        // Act
        Result<GameStarted, InvalidMoveError> returnedResult = originalResult.Inspect(_ => actionExecuted = true);

        // Assert
        actionExecuted.ShouldBeFalse();
        returnedResult.ShouldBeEquivalentTo(originalResult);
    }

    [Test]
    public void inspect_error_executes_action_and_returns_same_instance_when_called_on_error_result() {
        // Arrange
        InvalidMoveError errorValue = new InvalidMoveError(Faker.Random.Guid(), Faker.Lorem.Sentence());
        Result<GameStarted, InvalidMoveError> originalResult = Kurrent.Result.Failure<GameStarted, InvalidMoveError>(errorValue);
        bool actionExecuted = false;

        // Act
        Result<GameStarted, InvalidMoveError> returnedResult = originalResult.InspectError(_ => actionExecuted = true);

        // Assert
        actionExecuted.ShouldBeTrue();
        returnedResult.ShouldBeEquivalentTo(originalResult);
    }

    [Test]
    public void inspect_error_does_not_execute_action_and_returns_same_instance_when_called_on_success_result() {
        // Arrange
        GameStarted successValue = new GameStarted(Faker.Random.Guid(), Faker.PickRandom<Player>());
        Result<GameStarted, InvalidMoveError> originalResult = Kurrent.Result.Success<GameStarted, InvalidMoveError>(successValue);
        bool actionExecuted = false;

        // Act
        Result<GameStarted, InvalidMoveError> returnedResult = originalResult.InspectError(_ => actionExecuted = true);

        // Assert
        actionExecuted.ShouldBeFalse();
        returnedResult.ShouldBeEquivalentTo(originalResult);
    }

    [Test]
    public void inspect_passes_state_to_action_when_using_stateful_variant() {
        // Arrange
        GameStarted successValue = new GameStarted(Faker.Random.Guid(), Faker.PickRandom<Player>());
        Result<GameStarted, InvalidMoveError> originalResult = Kurrent.Result.Success<GameStarted, InvalidMoveError>(successValue);
        string contextState = "Audit trail";
        string? capturedState = null;

        // Act
        originalResult.Inspect((state, gs) => capturedState = $"{state}: {gs.GameId}", contextState);

        // Assert
        capturedState.ShouldBe($"{contextState}: {successValue.GameId}");
    }

    [Test]
    public void inspect_error_passes_state_to_action_when_using_stateful_variant() {
        // Arrange
        InvalidMoveError errorValue = new InvalidMoveError(Faker.Random.Guid(), Faker.Lorem.Sentence());
        Result<GameStarted, InvalidMoveError> originalResult = Kurrent.Result.Failure<GameStarted, InvalidMoveError>(errorValue);
        string contextState = "Error context";
        string? capturedState = null;

        // Act
        originalResult.InspectError((state, err) => capturedState = $"{state}: {err.Reason}", contextState);

        // Assert
        capturedState.ShouldBe($"{contextState}: {errorValue.Reason}");
    }

    [Test]
    public async ValueTask inspect_async_executes_action_with_value_task() {
        // Arrange
        GameStarted successValue = new GameStarted(Faker.Random.Guid(), Faker.PickRandom<Player>());
        Result<GameStarted, InvalidMoveError> originalResult = Kurrent.Result.Success<GameStarted, InvalidMoveError>(successValue);
        bool actionExecuted = false;

        // Act
        Result<GameStarted, InvalidMoveError> returnedResult = await originalResult.InspectAsync(async _ => {
            await Task.Delay(1);
            actionExecuted = true;
        });

        // Assert
        actionExecuted.ShouldBeTrue();
        returnedResult.ShouldBeEquivalentTo(originalResult);
    }

    [Test]
    public async ValueTask inspect_error_async_executes_action_with_value_task() {
        // Arrange
        InvalidMoveError errorValue = new InvalidMoveError(Faker.Random.Guid(), Faker.Lorem.Sentence());
        Result<GameStarted, InvalidMoveError> originalResult = Kurrent.Result.Failure<GameStarted, InvalidMoveError>(errorValue);
        bool actionExecuted = false;

        // Act
        Result<GameStarted, InvalidMoveError> returnedResult = await originalResult.InspectErrorAsync(async _ => {
            await Task.Delay(1);
            actionExecuted = true;
        });

        // Assert
        actionExecuted.ShouldBeTrue();
        returnedResult.ShouldBeEquivalentTo(originalResult);
    }

    [Test]
    public async ValueTask inspect_async_passes_state_to_action_when_using_stateful_variant() {
        // Arrange
        GameStarted successValue = new GameStarted(Faker.Random.Guid(), Faker.PickRandom<Player>());
        Result<GameStarted, InvalidMoveError> originalResult = Kurrent.Result.Success<GameStarted, InvalidMoveError>(successValue);
        string contextState = "Async audit trail";
        string? capturedState = null;

        // Act
        await originalResult.InspectAsync(async (state, gs) => {
            await Task.Delay(1);
            capturedState = $"{state}: {gs.GameId}";
        }, contextState);

        // Assert
        capturedState.ShouldBe($"{contextState}: {successValue.GameId}");
    }

    [Test]
    public async ValueTask inspect_error_async_passes_state_to_action_when_using_stateful_variant() {
        // Arrange
        InvalidMoveError errorValue = new InvalidMoveError(Faker.Random.Guid(), Faker.Lorem.Sentence());
        Result<GameStarted, InvalidMoveError> originalResult = Kurrent.Result.Failure<GameStarted, InvalidMoveError>(errorValue);
        string contextState = "Async error context";
        string? capturedState = null;

        // Act
        await originalResult.InspectErrorAsync(async (state, err) => {
            await Task.Delay(1);
            capturedState = $"{state}: {err.Reason}";
        }, contextState);

        // Assert
        capturedState.ShouldBe($"{contextState}: {errorValue.Reason}");
    }
}
