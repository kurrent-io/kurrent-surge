// ReSharper disable UnusedParameter.Local

using TicTacToe;

namespace Kurrent.Variant.Tests.Result;

[Category("Variant")]
public class ResultInspectTests {
    Faker Faker { get; } = new();

    [Test]
    public async Task inspect_executes_action_when_result_is_success() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var actionExecuted = false;

        // Act
        var returned = result.Inspect(id => actionExecuted = true);

        // Assert
        await Assert.That(actionExecuted).IsTrue();
        await Assert.That(returned).IsEqualTo(result);
        await Assert.That(returned.IsSuccess).IsTrue();
    }

    [Test]
    public async Task inspect_does_not_execute_action_when_result_is_failure() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Error");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);
        var actionExecuted = false;

        // Act
        var returned = result.Inspect(id => actionExecuted = true);

        // Assert
        await Assert.That(actionExecuted).IsFalse();
        await Assert.That(returned).IsEqualTo(result);
        await Assert.That(returned.IsFailure).IsTrue();
    }

    [Test]
    public async Task inspect_returns_same_result_for_chaining() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);

        // Act
        var returned = result.Inspect(id => { });

        // Assert
        await Assert.That(returned.Value).IsEqualTo(gameId);
        await Assert.That(returned.IsSuccess).IsTrue();
    }

    [Test]
    public async Task inspect_can_capture_side_effects() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        GameId? capturedId = null;

        // Act
        result.Inspect(id => capturedId = id);

        // Assert
        await Assert.That(capturedId).IsNotNull();
        await Assert.That(capturedId.Value.Value).IsEqualTo(gameId.Value);
    }

    [Test]
    public async Task inspect_chains_multiple_actions() {
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
        await Assert.That(firstExecuted).IsTrue();
        await Assert.That(secondExecuted).IsTrue();
        await Assert.That(final.IsSuccess).IsTrue();
    }

    [Test]
    public async Task inspect_with_state_executes_action_with_provided_state() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var logMessage = Faker.Lorem.Sentence();
        var capturedLog = string.Empty;

        // Act
        var returned = result.Inspect((log, id) => capturedLog = log, logMessage);

        // Assert
        await Assert.That(capturedLog).IsEqualTo(logMessage);
        await Assert.That(returned.IsSuccess).IsTrue();
    }

    [Test]
    public async Task inspect_with_state_does_not_execute_when_failure() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "State error");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);
        var stateValue = Faker.Random.Int();
        var actionExecuted = false;

        // Act
        var returned = result.Inspect((id, state) => actionExecuted = true, stateValue);

        // Assert
        await Assert.That(actionExecuted).IsFalse();
        await Assert.That(returned.IsFailure).IsTrue();
    }

    [Test]
    public async Task inspect_with_state_avoids_closure_allocation() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var stateValue = Faker.Random.Int(1, 100);
        var capturedState = 0;

        // Act
        result.Inspect((state, id) => capturedState = state, stateValue);

        // Assert
        await Assert.That(capturedState).IsEqualTo(stateValue);
    }

    [Test]
    public async Task inspect_error_executes_action_when_result_is_failure() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Test error");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);
        var actionExecuted = false;

        // Act
        var returned = result.InspectError(err => actionExecuted = true);

        // Assert
        await Assert.That(actionExecuted).IsTrue();
        await Assert.That(returned).IsEqualTo(result);
        await Assert.That(returned.IsFailure).IsTrue();
    }

    [Test]
    public async Task inspect_error_does_not_execute_action_when_result_is_success() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var actionExecuted = false;

        // Act
        var returned = result.InspectError(err => actionExecuted = true);

        // Assert
        await Assert.That(actionExecuted).IsFalse();
        await Assert.That(returned).IsEqualTo(result);
        await Assert.That(returned.IsSuccess).IsTrue();
    }

    [Test]
    public async Task inspect_error_can_capture_error_details() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Capture this");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);
        InvalidMoveError? capturedError = null;

        // Act
        result.InspectError(err => capturedError = err);

        // Assert
        await Assert.That(capturedError).IsNotNull();
        await Assert.That(capturedError.Reason).IsEqualTo("Capture this");
    }

    [Test]
    public async Task inspect_error_returns_same_result_for_chaining() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Error");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);

        // Act
        var returned = result.InspectError(err => { });

        // Assert
        await Assert.That(returned.Error).IsEqualTo(error);
        await Assert.That(returned.IsFailure).IsTrue();
    }

    [Test]
    public async Task inspect_error_chains_multiple_actions() {
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
        await Assert.That(firstExecuted).IsTrue();
        await Assert.That(secondExecuted).IsTrue();
        await Assert.That(final.IsFailure).IsTrue();
    }

    [Test]
    public async Task inspect_error_with_state_executes_action_with_provided_state() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "State failure");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);
        var errorCount = Faker.Random.Int(1, 10);
        var capturedCount = 0;

        // Act
        var returned = result.InspectError((count, err) => capturedCount = count, errorCount);

        // Assert
        await Assert.That(capturedCount).IsEqualTo(errorCount);
        await Assert.That(returned.IsFailure).IsTrue();
    }

    [Test]
    public async Task inspect_error_with_state_does_not_execute_when_success() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var stateValue = Faker.Random.Int();
        var actionExecuted = false;

        // Act
        var returned = result.InspectError((state, err) => actionExecuted = true, stateValue);

        // Assert
        await Assert.That(actionExecuted).IsFalse();
        await Assert.That(returned.IsSuccess).IsTrue();
    }

    [Test]
    public async Task inspect_and_inspect_error_can_be_combined() {
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
        await Assert.That(successExecuted).IsTrue();
        await Assert.That(failureExecuted).IsFalse();
        await Assert.That(returned.IsSuccess).IsTrue();
    }

    [Test]
    public async Task inspect_error_and_inspect_can_be_combined() {
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
        await Assert.That(successExecuted).IsFalse();
        await Assert.That(failureExecuted).IsTrue();
        await Assert.That(returned.IsFailure).IsTrue();
    }

    [Test]
    public async Task inspect_async_executes_async_action_when_success() {
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
        await Assert.That(actionExecuted).IsTrue();
        await Assert.That(returned.IsSuccess).IsTrue();
    }

    [Test]
    public async Task inspect_async_does_not_execute_when_failure() {
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
        await Assert.That(actionExecuted).IsFalse();
        await Assert.That(returned.IsFailure).IsTrue();
    }

    [Test]
    public async Task inspect_async_with_state_executes_with_provided_state() {
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
        await Assert.That(capturedLog).IsEqualTo(logMessage);
    }

    [Test]
    public async Task inspect_error_async_executes_async_action_when_failure() {
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
        await Assert.That(actionExecuted).IsTrue();
        await Assert.That(returned.IsFailure).IsTrue();
    }

    [Test]
    public async Task inspect_error_async_does_not_execute_when_success() {
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
        await Assert.That(actionExecuted).IsFalse();
        await Assert.That(returned.IsSuccess).IsTrue();
    }

    [Test]
    public async Task inspect_error_async_with_state_executes_with_provided_state() {
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
        await Assert.That(capturedCount).IsEqualTo(errorCount);
    }

    [Test]
    public async Task inspect_async_chain_extension_awaits_task_then_executes() {
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
        await Assert.That(actionExecuted).IsTrue();
        await Assert.That(returned.IsSuccess).IsTrue();
    }

    [Test]
    public async Task inspect_error_async_chain_extension_awaits_task_then_executes() {
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
        await Assert.That(actionExecuted).IsTrue();
        await Assert.That(returned.IsFailure).IsTrue();
    }

    [Test]
    public async Task inspect_executes_action_and_returns_same_instance_when_called_on_success_result() {
        // Arrange
        var successValue = new GameStarted(Faker.Random.Guid(), Faker.PickRandom<Player>());
        var originalResult = Kurrent.Result.Success<GameStarted, InvalidMoveError>(successValue);
        var actionExecuted = false;

        // Act
        var returnedResult = originalResult.Inspect(_ => actionExecuted = true);

        // Assert
        await Assert.That(actionExecuted).IsTrue();
        await Assert.That(returnedResult).IsDeepEquivalentTo(originalResult);
    }

    [Test]
    public async Task inspect_does_not_execute_action_and_returns_same_instance_when_called_on_error_result() {
        // Arrange
        var errorValue = new InvalidMoveError(Faker.Random.Guid(), Faker.Lorem.Sentence());
        var originalResult = Kurrent.Result.Failure<GameStarted, InvalidMoveError>(errorValue);
        var actionExecuted = false;

        // Act
        var returnedResult = originalResult.Inspect(_ => actionExecuted = true);

        // Assert
        await Assert.That(actionExecuted).IsFalse();
        await Assert.That(returnedResult).IsDeepEquivalentTo(originalResult);
    }

    [Test]
    public async Task inspect_error_executes_action_and_returns_same_instance_when_called_on_error_result() {
        // Arrange
        var errorValue = new InvalidMoveError(Faker.Random.Guid(), Faker.Lorem.Sentence());
        var originalResult = Kurrent.Result.Failure<GameStarted, InvalidMoveError>(errorValue);
        var actionExecuted = false;

        // Act
        var returnedResult = originalResult.InspectError(_ => actionExecuted = true);

        // Assert
        await Assert.That(actionExecuted).IsTrue();
        await Assert.That(returnedResult).IsDeepEquivalentTo(originalResult);
    }

    [Test]
    public async Task inspect_error_does_not_execute_action_and_returns_same_instance_when_called_on_success_result() {
        // Arrange
        var successValue = new GameStarted(Faker.Random.Guid(), Faker.PickRandom<Player>());
        var originalResult = Kurrent.Result.Success<GameStarted, InvalidMoveError>(successValue);
        var actionExecuted = false;

        // Act
        var returnedResult = originalResult.InspectError(_ => actionExecuted = true);

        // Assert
        await Assert.That(actionExecuted).IsFalse();
        await Assert.That(returnedResult).IsDeepEquivalentTo(originalResult);
    }

    [Test]
    public async Task inspect_passes_state_to_action_when_using_stateful_variant() {
        // Arrange
        var successValue = new GameStarted(Faker.Random.Guid(), Faker.PickRandom<Player>());
        var originalResult = Kurrent.Result.Success<GameStarted, InvalidMoveError>(successValue);
        var contextState = "Audit trail";
        string? capturedState = null;

        // Act
        originalResult.Inspect((state, gs) => capturedState = $"{state}: {gs.GameId}", contextState);

        // Assert
        await Assert.That(capturedState).IsEqualTo($"{contextState}: {successValue.GameId}");
    }

    [Test]
    public async Task inspect_error_passes_state_to_action_when_using_stateful_variant() {
        // Arrange
        var errorValue = new InvalidMoveError(Faker.Random.Guid(), Faker.Lorem.Sentence());
        var originalResult = Kurrent.Result.Failure<GameStarted, InvalidMoveError>(errorValue);
        var contextState = "Error context";
        string? capturedState = null;

        // Act
        originalResult.InspectError((state, err) => capturedState = $"{state}: {err.Reason}", contextState);

        // Assert
        await Assert.That(capturedState).IsEqualTo($"{contextState}: {errorValue.Reason}");
    }

    [Test]
    public async Task inspect_async_executes_action_with_value_task() {
        // Arrange
        var successValue = new GameStarted(Faker.Random.Guid(), Faker.PickRandom<Player>());
        var originalResult = Kurrent.Result.Success<GameStarted, InvalidMoveError>(successValue);
        var actionExecuted = false;

        // Act
        var returnedResult = await originalResult.InspectAsync(async _ => {
            await Task.Delay(1);
            actionExecuted = true;
        });

        // Assert
        await Assert.That(actionExecuted).IsTrue();
        await Assert.That(returnedResult).IsDeepEquivalentTo(originalResult);
    }

    [Test]
    public async Task inspect_error_async_executes_action_with_value_task() {
        // Arrange
        var errorValue = new InvalidMoveError(Faker.Random.Guid(), Faker.Lorem.Sentence());
        var originalResult = Kurrent.Result.Failure<GameStarted, InvalidMoveError>(errorValue);
        var actionExecuted = false;

        // Act
        var returnedResult = await originalResult.InspectErrorAsync(async _ => {
            await Task.Delay(1);
            actionExecuted = true;
        });

        // Assert
        await Assert.That(actionExecuted).IsTrue();
        await Assert.That(returnedResult).IsDeepEquivalentTo(originalResult);
    }

    [Test]
    public async Task inspect_async_passes_state_to_action_when_using_stateful_variant() {
        // Arrange
        var successValue = new GameStarted(Faker.Random.Guid(), Faker.PickRandom<Player>());
        var originalResult = Kurrent.Result.Success<GameStarted, InvalidMoveError>(successValue);
        var contextState = "Async audit trail";
        string? capturedState = null;

        // Act
        await originalResult.InspectAsync(async (state, gs) => {
            await Task.Delay(1);
            capturedState = $"{state}: {gs.GameId}";
        }, contextState);

        // Assert
        await Assert.That(capturedState).IsEqualTo($"{contextState}: {successValue.GameId}");
    }

    [Test]
    public async Task inspect_error_async_passes_state_to_action_when_using_stateful_variant() {
        // Arrange
        var errorValue = new InvalidMoveError(Faker.Random.Guid(), Faker.Lorem.Sentence());
        var originalResult = Kurrent.Result.Failure<GameStarted, InvalidMoveError>(errorValue);
        var contextState = "Async error context";
        string? capturedState = null;

        // Act
        await originalResult.InspectErrorAsync(async (state, err) => {
            await Task.Delay(1);
            capturedState = $"{state}: {err.Reason}";
        }, contextState);

        // Assert
        await Assert.That(capturedState).IsEqualTo($"{contextState}: {errorValue.Reason}");
    }
}
