// ReSharper disable UnusedParameter.Local

using TicTacToe;

namespace Kurrent.Variant.Tests.Result;

public class ResultMatchTests {
    Faker Faker { get; } = new();

    [Test]
    public async Task match_with_success_executes_success_handler() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);

        // Act
        var output = result.Match(
            mapValue: id => $"Success: {id.Value}",
            mapError: err => $"Error: {err.Reason}");

        // Assert
        await Assert.That(output).StartsWith("Success:");
        await Assert.That(output).Contains(gameId.Value.ToString());
    }

    [Test]
    public async Task match_with_failure_executes_failure_handler() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Game over");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);

        // Act
        var output = result.Match(
            mapValue: id => $"Success: {id.Value}",
            mapError: err => $"Error: {err.Reason}");

        // Assert
        await Assert.That(output).StartsWith("Error:");
        await Assert.That(output).Contains("Game over");
    }

    [Test]
    public async Task match_with_success_does_not_execute_failure_handler() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var failureHandlerExecuted = false;

        // Act
        var output = result.Match(
            mapValue: id => "Success",
            mapError: err => {
                failureHandlerExecuted = true;
                return "Failure";
            });

        // Assert
        await Assert.That(output).IsEqualTo("Success");
        await Assert.That(failureHandlerExecuted).IsFalse();
    }

    [Test]
    public async Task match_with_failure_does_not_execute_success_handler() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Error");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);
        var successHandlerExecuted = false;

        // Act
        var output = result.Match(
            mapValue: id => {
                successHandlerExecuted = true;
                return "Success";
            },
            mapError: err => "Failure");

        // Assert
        await Assert.That(output).IsEqualTo("Failure");
        await Assert.That(successHandlerExecuted).IsFalse();
    }

    [Test]
    public async Task match_transforms_to_different_type() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);

        // Act
        var number = result.Match(
            mapValue: id => id.Value.GetHashCode(),
            mapError: err => -1);

        // Assert
        await Assert.That(number).IsNotEqualTo(-1);
        await Assert.That(number).IsEqualTo(gameId.Value.GetHashCode());
    }

    [Test]
    public async Task match_can_return_same_type_for_both_branches() {
        // Arrange
        var position = new Position(1, 2);
        var result = Kurrent.Result.Success<Position, InvalidMoveError>(position);

        // Act
        var coordinate = result.Match(
            mapValue: pos => pos.Row,
            mapError: err => -1);

        // Assert
        await Assert.That(coordinate).IsEqualTo(1);
    }

    [Test]
    public async Task match_with_state_executes_success_handler_with_provided_state() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var prefix = Faker.Lorem.Word();

        // Act
        var output = result.Match(
            mapValue: (pre, id) => $"{pre}: {id.Value}",
            mapError: (pre, err) => $"{pre}: {err.Reason}",
            state: prefix);

        // Assert
        await Assert.That(output).StartsWith(prefix);
        await Assert.That(output).Contains(gameId.Value.ToString());
    }

    [Test]
    public async Task match_with_state_executes_failure_handler_with_provided_state() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "State error");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);
        var multiplier = Faker.Random.Int(2, 10);

        // Act
        var output = result.Match(
            mapValue: (mult, id) => mult * id.Value.GetHashCode(),
            mapError: (mult, err) => mult * -1,
            state: multiplier);

        // Assert
        await Assert.That(output).IsEqualTo(multiplier * -1);
    }

    [Test]
    public async Task match_with_state_avoids_closure_allocation() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var stateValue = Faker.Random.Int(1, 100);
        var capturedState = 0;

        // Act
        var output = result.Match(
            mapValue: (state, id) => {
                capturedState = state;
                return state * 2;
            },
            mapError: (state, err) => state * -1,
            state: stateValue);

        // Assert
        await Assert.That(capturedState).IsEqualTo(stateValue);
        await Assert.That(output).IsEqualTo(stateValue * 2);
    }

    [Test]
    public async Task match_async_with_success_executes_async_success_handler() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);

        // Act
        var output = await result.MatchAsync(
            mapValueAsync: async id => {
                await Task.Delay(1);
                return $"Success: {id.Value}";
            },
            mapErrorAsync: async err => {
                await Task.Delay(1);
                return $"Error: {err.Reason}";
            });

        // Assert
        await Assert.That(output).StartsWith("Success:");
    }

    [Test]
    public async Task match_async_with_failure_executes_async_failure_handler() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Async error");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);

        // Act
        var output = await result.MatchAsync(
            mapValueAsync: async id => {
                await Task.Delay(1);
                return $"Success: {id.Value}";
            },
            mapErrorAsync: async err => {
                await Task.Delay(1);
                return $"Error: {err.Reason}";
            });

        // Assert
        await Assert.That(output).StartsWith("Error:");
        await Assert.That(output).Contains("Async error");
    }

    [Test]
    public async Task match_async_with_success_does_not_execute_failure_handler() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var failureHandlerExecuted = false;

        // Act
        var output = await result.MatchAsync(
            mapValueAsync: async id => {
                await Task.Delay(1);
                return "Success";
            },
            mapErrorAsync: async err => {
                failureHandlerExecuted = true;
                await Task.Delay(1);
                return "Failure";
            });

        // Assert
        await Assert.That(output).IsEqualTo("Success");
        await Assert.That(failureHandlerExecuted).IsFalse();
    }

    [Test]
    public async Task match_async_with_state_executes_handler_with_provided_state() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var prefix = Faker.Lorem.Word();

        // Act
        var output = await result.MatchAsync(
            mapValueAsync: async (pre, id) => {
                await Task.Delay(1);
                return $"{pre}: {id.Value}";
            },
            mapErrorAsync: async (pre, err) => {
                await Task.Delay(1);
                return $"{pre}: {err.Reason}";
            },
            state: prefix);

        // Assert
        await Assert.That(output).StartsWith(prefix);
    }

    [Test]
    public async Task match_async_chain_extension_awaits_task_then_matches() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Success<GameId, InvalidMoveError>(gameId));

        // Act
        var output = await resultTask.MatchAsync(
            mapValue: id => $"Success: {id.Value}",
            mapError: err => $"Error: {err.Reason}");

        // Assert
        await Assert.That(output).StartsWith("Success:");
    }

    [Test]
    public async Task match_async_chain_extension_with_async_handlers_awaits_both() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Success<GameId, InvalidMoveError>(gameId));

        // Act
        var output = await resultTask.MatchAsync(
            mapValueAsync: async id => {
                await Task.Delay(1);
                return $"Success: {id.Value}";
            },
            mapErrorAsync: async err => {
                await Task.Delay(1);
                return $"Error: {err.Reason}";
            });

        // Assert
        await Assert.That(output).StartsWith("Success:");
    }

    [Test]
    public async Task match_async_chain_extension_with_state_passes_state_correctly() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Success<GameId, InvalidMoveError>(gameId));
        var multiplier = Faker.Random.Int(2, 10);

        // Act
        var output = await resultTask.MatchAsync(
            mapValue: (mult, id) => mult * id.Value.GetHashCode(),
            mapError: (mult, err) => mult * -1,
            state: multiplier);

        // Assert
        await Assert.That(output).IsEqualTo(multiplier * gameId.Value.GetHashCode());
    }

    [Test]
    public async Task match_async_chain_extension_with_async_state_handlers_works() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "State async");
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Failure<GameId, InvalidMoveError>(error));
        var prefix = Faker.Lorem.Word();

        // Act
        var output = await resultTask.MatchAsync(
            mapValueAsync: async (pre, id) => {
                await Task.Delay(1);
                return $"{pre}: {id.Value}";
            },
            mapErrorAsync: async (pre, err) => {
                await Task.Delay(1);
                return $"{pre}: {err.Reason}";
            },
            state: prefix);

        // Assert
        await Assert.That(output).StartsWith(prefix);
        await Assert.That(output).Contains("State async");
    }

    [Test]
    public async Task match_can_be_used_for_pattern_matching_logic() {
        // Arrange
        var position = new Position(1, 1);
        var result = Kurrent.Result.Success<Position, InvalidMoveError>(position);

        // Act
        var isCenter = result.Match(
            mapValue: pos => pos.Row == 1 && pos.Column == 1,
            mapError: err => false);

        // Assert
        await Assert.That(isCenter).IsTrue();
    }

    [Test]
    public async Task match_enables_exhaustive_handling() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var successResult = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var error = new InvalidMoveError(Faker.Random.Guid(), "Error");
        var failureResult = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);

        // Act
        var successOutput = successResult.Match(
            mapValue: id => "handled",
            mapError: err => "handled");
        var failureOutput = failureResult.Match(
            mapValue: id => "handled",
            mapError: err => "handled");

        // Assert
        await Assert.That(successOutput).IsEqualTo("handled");
        await Assert.That(failureOutput).IsEqualTo("handled");
    }

    [Test]
    public async Task match_async_propagates_task_result() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Task error");
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Failure<GameId, InvalidMoveError>(error));
        var successHandlerExecuted = false;

        // Act
        var output = await resultTask.MatchAsync(
            mapValue: id => {
                successHandlerExecuted = true;
                return "Success";
            },
            mapError: err => $"Error: {err.Reason}");

        // Assert
        await Assert.That(output).StartsWith("Error:");
        await Assert.That(successHandlerExecuted).IsFalse();
    }
}
