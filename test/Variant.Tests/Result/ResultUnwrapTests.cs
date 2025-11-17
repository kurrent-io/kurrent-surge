// ReSharper disable UnusedParameter.Local

using TicTacToe;

namespace Kurrent.Variant.Tests.Result;

public class ResultUnwrapTests {
    Faker Faker { get; } = new();

    [Test]
    public void unwrap_returns_value_on_success() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);

        // Act
        var value = result.Unwrap();

        // Assert
        value.ShouldBe(gameId);
    }

    [Test]
    public void unwrap_throws_exception_on_failure() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Test error");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);

        // Act & Assert
        var exception = Should.Throw<InvalidOperationException>(() => result.Unwrap());
        exception.Message.ShouldContain(error.GameId.ToString());
        exception.Message.ShouldContain("Test error");
    }

    [Test]
    public void unwrap_throws_correct_exception_type_for_different_errors() {
        // Arrange
        var invalidMoveError = new InvalidMoveError(Faker.Random.Guid(), "Invalid");
        var invalidMoveResult = Kurrent.Result.Failure<Position, InvalidMoveError>(invalidMoveError);

        var gameEndedError = new GameEndedError(Faker.Random.Guid(), GameStatus.Won);
        var gameEndedResult = Kurrent.Result.Failure<Position, GameEndedError>(gameEndedError);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => invalidMoveResult.Unwrap());
        Should.Throw<InvalidOperationException>(() => gameEndedResult.Unwrap());
    }

    [Test]
    public void unwrap_preserves_exception_message_from_error() {
        // Arrange
        var errorMessage = Faker.Lorem.Sentence();
        var error = new SimpleError(errorMessage);
        var result = Kurrent.Result.Failure<GameId, SimpleError>(error);

        // Act & Assert
        var exception = Should.Throw<InvalidOperationException>(() => result.Unwrap());
        exception.Message.ShouldContain(errorMessage);
    }

    [Test]
    public async ValueTask unwrap_async_returns_value_on_success() {
        // Arrange
        var position = new Position(1, 2);
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Success<Position, InvalidMoveError>(position));

        // Act
        var value = await resultTask.UnwrapAsync();

        // Assert
        value.ShouldBe(position);
    }

    [Test]
    public async ValueTask unwrap_async_throws_exception_on_failure() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Async error");
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Failure<GameId, InvalidMoveError>(error));

        // Act & Assert
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await resultTask.UnwrapAsync());
        exception.Message.ShouldContain("Async error");
    }

    [Test]
    public void unwrap_can_be_used_after_successful_pipeline() {
        // Arrange
        var position = new Position(1, 2);
        var result = Kurrent.Result.Success<Position, InvalidMoveError>(position);

        // Act
        var value = result
            .Map(pos => pos.Row)
            .Map(row => row * 2)
            .Unwrap();

        // Assert
        value.ShouldBe(2); // position.Row (1) * 2 = 2
    }

    [Test]
    public void unwrap_throws_at_end_of_failed_pipeline() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Pipeline error");
        var result = Kurrent.Result.Failure<Position, InvalidMoveError>(error);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => result
            .Map(pos => pos.Row)
            .Map(row => row * 2)
            .Unwrap());
    }

    [Test]
    public void unwrap_enables_direct_value_extraction() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);

        // Act - Direct assignment without pattern matching
        GameId extractedId = result.Unwrap();

        // Assert
        extractedId.ShouldBe(gameId);
    }

    [Test]
    public async ValueTask unwrap_async_can_be_used_after_async_pipeline() {
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
        value.ShouldBe(gameId.Value.ToString());
    }

    [Test]
    public async ValueTask unwrap_async_throws_at_end_of_failed_async_pipeline() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Async pipeline");
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Failure<GameId, InvalidMoveError>(error));

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await resultTask
                .MapAsync(async id => {
                    await Task.Delay(1);
                    return id.Value.ToString();
                })
                .UnwrapAsync());
    }

    [Test]
    public void unwrap_vs_match_for_exception_handling() {
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
        var unwrapFailureThrows = Should.Throw<InvalidOperationException>(() => failureResult.Unwrap());

        // Assert
        matchedSuccess.ShouldBe(42);
        matchedFailure.ShouldBe(-1);
        unwrappedSuccess.ShouldBe(42);
        unwrapFailureThrows.ShouldNotBeNull();
    }
}
