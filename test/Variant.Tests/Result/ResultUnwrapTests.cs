// ReSharper disable UnusedParameter.Local

using TicTacToe;

namespace Kurrent.Variant.Tests.Result;

[Category("Variant")]
public class ResultUnwrapTests {
    Faker Faker { get; } = new();

    [Test]
    public async Task unwrap_returns_value_on_success() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);

        // Act
        var value = result.Unwrap();

        // Assert
        await Assert.That(value).IsEqualTo(gameId);
    }

    [Test]
    public async Task unwrap_throws_exception_on_failure() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Test error");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);

        // Act & Assert
        var exception = await Assert.That(result.Unwrap).ThrowsExactly<InvalidOperationException>();
        await Assert.That(exception.Message).Contains(error.GameId.ToString());
        await Assert.That(exception.Message).Contains("Test error");
    }

    [Test]
    public async Task unwrap_throws_correct_exception_type_for_different_errors() {
        // Arrange
        var invalidMoveError = new InvalidMoveError(Faker.Random.Guid(), "Invalid");
        var invalidMoveResult = Kurrent.Result.Failure<Position, InvalidMoveError>(invalidMoveError);

        var gameEndedError = new GameEndedError(Faker.Random.Guid(), GameStatus.Won);
        var gameEndedResult = Kurrent.Result.Failure<Position, GameEndedError>(gameEndedError);

        // Act & Assert
        await Assert.That(() => invalidMoveResult.Unwrap()).ThrowsExactly<InvalidOperationException>();
        await Assert.That(() => gameEndedResult.Unwrap()).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task unwrap_preserves_exception_message_from_error() {
        // Arrange
        var errorMessage = Faker.Lorem.Sentence();
        var error = new SimpleError(errorMessage);
        var result = Kurrent.Result.Failure<GameId, SimpleError>(error);

        // Act & Assert
        var exception = await Assert.That(() => result.Unwrap()).ThrowsExactly<InvalidOperationException>();
        await Assert.That(exception.Message).Contains(errorMessage);
    }

    [Test]
    public async Task unwrap_async_returns_value_on_success() {
        // Arrange
        var position = new Position(1, 2);
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Success<Position, InvalidMoveError>(position));

        // Act
        var value = await resultTask.UnwrapAsync();

        // Assert
        await Assert.That(value).IsEqualTo(position);
    }

    [Test]
    public async Task unwrap_async_throws_exception_on_failure() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Async error");
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Failure<GameId, InvalidMoveError>(error));

        // Act & Assert
        var exception = await Assert.That(async () => await resultTask.UnwrapAsync()).ThrowsExactly<InvalidOperationException>();
        await Assert.That(exception.Message).Contains("Async error");
    }

    [Test]
    public async Task unwrap_can_be_used_after_successful_pipeline() {
        // Arrange
        var position = new Position(1, 2);
        var result = Kurrent.Result.Success<Position, InvalidMoveError>(position);

        // Act
        var value = result
            .Map(pos => pos.Row)
            .Map(row => row * 2)
            .Unwrap();

        // Assert
        await Assert.That(value).IsEqualTo(2); // position.Row (1) * 2 = 2
    }

    [Test]
    public async Task unwrap_throws_at_end_of_failed_pipeline() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Pipeline error");
        var result = Kurrent.Result.Failure<Position, InvalidMoveError>(error);

        // Act & Assert
        await Assert.That(() => result
            .Map(pos => pos.Row)
            .Map(row => row * 2)
            .Unwrap()).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task unwrap_enables_direct_value_extraction() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);

        // Act - Direct assignment without pattern matching
        GameId extractedId = result.Unwrap();

        // Assert
        await Assert.That(extractedId).IsEqualTo(gameId);
    }

    [Test]
    public async Task unwrap_async_can_be_used_after_async_pipeline() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Success<GameId, InvalidMoveError>(gameId));

        // Act
        var value = await resultTask
            .MapAsync(async id => {
                await Task.Delay(1);
                return id.Value.ToString();
            })
            .UnwrapAsync();

        // Assert
        await Assert.That(value).IsEqualTo(gameId.Value.ToString());
    }

    [Test]
    public async Task unwrap_async_throws_at_end_of_failed_async_pipeline() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Async pipeline");
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Failure<GameId, InvalidMoveError>(error));

        // Act & Assert
        await Assert.That(async () =>
            await resultTask
                .MapAsync(async id => {
                    await Task.Delay(1);
                    return id.Value.ToString();
                })
                .UnwrapAsync()).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task unwrap_vs_match_for_exception_handling() {
        // Arrange
        var successResult = Kurrent.Result.Success<int, SimpleError>(42);
        var failureResult = Kurrent.Result.Failure<int, SimpleError>(new SimpleError("Error"));

        // Act - Match handles both cases without exception
        var matchedSuccess = successResult.Match(
            mapValue: val => val,
            mapError: err => -1);
        var matchedFailure = failureResult.Match(
            mapValue: val => val,
            mapError: err => -1);

        // Act - Unwrap returns value or throws
        var unwrappedSuccess = successResult.Unwrap();
        var unwrapFailureThrows = await Assert.That(() => failureResult.Unwrap()).ThrowsExactly<InvalidOperationException>();

        // Assert
        await Assert.That(matchedSuccess).IsEqualTo(42);
        await Assert.That(matchedFailure).IsEqualTo(-1);
        await Assert.That(unwrappedSuccess).IsEqualTo(42);
        await Assert.That(unwrapFailureThrows).IsNotNull();
    }
}
