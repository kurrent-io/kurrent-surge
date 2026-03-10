using TicTacToe;

namespace Kurrent.Variant.Tests.Result;

[UsedImplicitly]
[Category("Variant")]
public class ResultCoreTests {
    Faker Faker { get; } = new();

    [Test]
    public async Task creates_success_result_when_using_success_factory_method() {
        // Arrange
        var gameId           = new GameId(Faker.Random.Guid());
        var gameStartedEvent = new GameStarted(Faker.Random.Guid(), Faker.PickRandom<Player>());

        // Act
        var resultGameId    = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var resultTicTacToe = Kurrent.Result.Success<GameStarted, InvalidMoveError>(gameStartedEvent);

        // Assert
        await Assert.That(resultGameId.IsSuccess).IsTrue();
        await Assert.That(resultGameId.Value).IsEqualTo(gameId);

        await Assert.That(resultTicTacToe.IsSuccess).IsTrue();
        await Assert.That(resultTicTacToe.Value).IsEqualTo(gameStartedEvent);
    }

    [Test]
    public async Task creates_error_result_when_using_error_factory_method() {
        // Arrange
        var gameEndedError = new GameEndedError(Faker.Random.Guid(), Faker.PickRandom<GameStatus>());
        var ticTacToeError = new InvalidMoveError(Faker.Random.Guid(), "Invalid board position");

        // Act
        var resultGameEnded = Kurrent.Result.Failure<GameStarted, GameEndedError>(gameEndedError);
        var resultTicTacToe = Kurrent.Result.Failure<GameStarted, InvalidMoveError>(ticTacToeError);

        // Assert
        await Assert.That(resultGameEnded.IsFailure).IsTrue();
        await Assert.That(resultGameEnded.Error).IsEqualTo(gameEndedError);

        await Assert.That(resultTicTacToe.IsFailure).IsTrue();
        await Assert.That(resultTicTacToe.Error).IsEqualTo(ticTacToeError);
    }

    [Test]
    public async Task returns_true_for_is_success_when_result_contains_success_value() {
        // Arrange
        var successResult = Kurrent.Result.Success<GameStarted, InvalidMoveError>(new GameStarted(Faker.Random.Guid(), Faker.PickRandom<Player>()));

        // Act & Assert
        await Assert.That(successResult.IsSuccess).IsTrue();
    }

    [Test]
    public async Task returns_false_for_is_success_when_result_contains_error_value() {
        // Arrange
        var errorResult = Kurrent.Result.Failure<GameStarted, InvalidMoveError>(new InvalidMoveError(Faker.Random.Guid(), "Test reason"));

        // Act & Assert
        await Assert.That(errorResult.IsSuccess).IsFalse();
    }

    [Test]
    public async Task returns_true_for_is_failure_when_result_contains_error_value() {
        // Arrange
        var errorResult = Kurrent.Result.Failure<GameStarted, InvalidMoveError>(new InvalidMoveError(Faker.Random.Guid(), "Test reason"));

        // Act & Assert
        await Assert.That(errorResult.IsFailure).IsTrue();
    }

    [Test]
    public async Task returns_false_for_is_failure_when_result_contains_success_value() {
        // Arrange
        var successResult = Kurrent.Result.Success<GameStarted, InvalidMoveError>(new GameStarted(Faker.Random.Guid(), Faker.PickRandom<Player>()));

        // Act & Assert
        await Assert.That(successResult.IsFailure).IsFalse();
    }

    [Test]
    public async Task returns_success_value_when_accessing_as_success_on_success_result() {
        // Arrange
        var successValue  = new GameId(Faker.Random.Guid());
        var successResult = Kurrent.Result.Success<GameId, InvalidMoveError>(successValue);

        // Act & Assert
        await Assert.That(successResult.Value).IsEqualTo(successValue);
    }

    [Test]
    public async Task throws_invalid_operation_exception_when_accessing_as_success_on_error_result() {
        // Arrange
        var errorResult = Kurrent.Result.Failure<GameId, InvalidMoveError>(new InvalidMoveError(Faker.Random.Guid(), Faker.Lorem.Sentence()));

        // Act & Assert
        await Assert.That(() => errorResult.Value).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task returns_error_value_when_accessing_as_error_on_error_result() {
        // Arrange
        var errorValue  = new InvalidMoveError(Faker.Random.Guid(), "Test error");
        var errorResult = Kurrent.Result.Failure<GameStarted, InvalidMoveError>(errorValue);

        // Act & Assert
        await Assert.That(errorResult.Error).IsEqualTo(errorValue);
    }

    [Test]
    public async Task throws_invalid_operation_exception_when_accessing_as_error_on_success_result() {
        // Arrange
        var successResult = Kurrent.Result.Success<GameStarted, InvalidMoveError>(new GameStarted(Faker.Random.Guid(), Faker.PickRandom<Player>()));

        // Act & Assert
        await Assert.That(() => successResult.Error).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task implicitly_converts_success_value_to_result() {
        // Arrange
        var successValue = new GameStarted(Faker.Random.Guid(), Faker.PickRandom<Player>());

        // Act
        Result<GameStarted, InvalidMoveError> result = successValue;

        // Assert
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value).IsEqualTo(successValue);
    }

    [Test]
    public async Task implicitly_converts_error_value_to_result() {
        // Arrange
        var errorValue = new InvalidMoveError(Faker.Random.Guid(), "Cell occupied");

        // Act
        Result<GameStarted, InvalidMoveError> result = errorValue;

        // Assert
        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error).IsEqualTo(errorValue);
    }

    [Test]
    public async Task explicitly_converts_result_to_success_value_when_success() {
        // Arrange
        var successValue  = new GameId(Faker.Random.Guid());
        var successResult = Kurrent.Result.Success<GameId, InvalidMoveError>(successValue);

        // Act
        var extractedValue = (GameId)successResult;

        // Assert
        await Assert.That(extractedValue).IsEqualTo(successValue);
    }

    [Test]
    public async Task throws_invalid_operation_exception_when_explicitly_converting_error_result_to_success_value() {
        // Arrange
        var errorResult = Kurrent.Result.Failure<GameId, InvalidMoveError>(new InvalidMoveError(Faker.Random.Guid(), Faker.Lorem.Sentence()));

        // Act & Assert
        await Assert.That(() => (GameId)errorResult).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task explicitly_converts_result_to_error_value_when_error() {
        // Arrange
        var errorValue  = new InvalidMoveError(Faker.Random.Guid(), Faker.Lorem.Sentence());
        var errorResult = Kurrent.Result.Failure<GameStarted, InvalidMoveError>(errorValue);

        // Act
        var extractedValue = (InvalidMoveError)errorResult;

        // Assert
        await Assert.That(extractedValue).IsEqualTo(errorValue);
    }

    [Test]
    public async Task throws_invalid_operation_exception_when_explicitly_converting_success_result_to_error_value() {
        // Arrange
        var successResult = Kurrent.Result.Success<GameStarted, InvalidMoveError>(new GameStarted(Faker.Random.Guid(), Faker.PickRandom<Player>()));

        // Act & Assert
        await Assert.That(() => (InvalidMoveError)successResult).ThrowsExactly<InvalidOperationException>();
    }
}
