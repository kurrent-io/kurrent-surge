using TicTacToe;

namespace Kurrent.Variant.Tests.Result;

public class ResultMapTests {
    Faker Faker { get; } = new();

    [Test]
    public void map_transforms_success_value_to_new_type() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);

        // Act
        var mapped = result.Map(id => id.Value.ToString());

        // Assert
        mapped.IsSuccess.ShouldBeTrue();
        mapped.Value.ShouldBe(gameId.Value.ToString());
    }

    [Test]
    public void map_does_not_execute_on_failure() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Map error");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);
        var mapExecuted = false;

        // Act
        var mapped = result.Map(id => {
            mapExecuted = true;
            return id.Value.ToString();
        });

        // Assert
        mapExecuted.ShouldBeFalse();
        mapped.IsFailure.ShouldBeTrue();
        mapped.Error.ShouldBe(error);
    }

    [Test]
    public void map_can_transform_to_completely_different_type() {
        // Arrange
        var position = new Position(1, 2);
        var result = Kurrent.Result.Success<Position, InvalidMoveError>(position);

        // Act
        var mapped = result.Map(pos => new GameId(Guid.NewGuid()));

        // Assert
        mapped.IsSuccess.ShouldBeTrue();
        mapped.Value.ShouldBeOfType<GameId>();
    }

    [Test]
    public void map_error_transforms_failure_value_to_new_error_type() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Original");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);

        // Act
        var mapped = result.MapError(err => new GameEndedError(err.GameId, GameStatus.Draw));

        // Assert
        mapped.IsFailure.ShouldBeTrue();
        mapped.Error.ShouldBeOfType<GameEndedError>();
        mapped.Error.GameId.ShouldBe(error.GameId);
    }

    [Test]
    public void map_error_does_not_execute_on_success() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var mapExecuted = false;

        // Act
        var mapped = result.MapError(err => {
            mapExecuted = true;
            return new GameEndedError(err.GameId, GameStatus.Draw);
        });

        // Assert
        mapExecuted.ShouldBeFalse();
        mapped.IsSuccess.ShouldBeTrue();
        mapped.Value.ShouldBe(gameId);
    }

    [Test]
    public void map_with_state_passes_state_to_mapper() {
        // Arrange
        var position = new Position(1, 2);
        var result = Kurrent.Result.Success<Position, InvalidMoveError>(position);
        var multiplier = Faker.Random.Int(2, 10);

        // Act
        var mapped = result.Map((mult, pos) => pos.Row * mult, multiplier);

        // Assert
        mapped.IsSuccess.ShouldBeTrue();
        mapped.Value.ShouldBe(position.Row * multiplier);
    }

    [Test]
    public void map_with_state_does_not_execute_on_failure() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "State error");
        var result = Kurrent.Result.Failure<Position, InvalidMoveError>(error);
        var stateValue = Faker.Random.Int();
        var mapExecuted = false;

        // Act
        var mapped = result.Map((state, pos) => {
            mapExecuted = true;
            return pos.Row * state;
        }, stateValue);

        // Assert
        mapExecuted.ShouldBeFalse();
        mapped.IsFailure.ShouldBeTrue();
    }

    [Test]
    public void map_error_with_state_passes_state_to_mapper() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Error");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);
        var prefix = Faker.Lorem.Word();

        // Act
        var mapped = result.MapError((pre, err) =>
            new InvalidMoveError(err.GameId, $"{pre}: {err.Reason}"), prefix);

        // Assert
        mapped.IsFailure.ShouldBeTrue();
        mapped.Error.Reason.ShouldStartWith(prefix);
    }

    [Test]
    public void map_error_with_state_does_not_execute_on_success() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var stateValue = Faker.Random.Int();
        var mapExecuted = false;

        // Act
        var mapped = result.MapError((state, err) => {
            mapExecuted = true;
            return new GameEndedError(err.GameId, GameStatus.Draw);
        }, stateValue);

        // Assert
        mapExecuted.ShouldBeFalse();
        mapped.IsSuccess.ShouldBeTrue();
    }

    [Test]
    public async ValueTask map_async_chain_awaits_task_then_maps() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Success<GameId, InvalidMoveError>(gameId));

        // Act
        var mapped = await resultTask.MapAsync(id => id.Value.ToString());

        // Assert
        mapped.IsSuccess.ShouldBeTrue();
        mapped.Value.ShouldBe(gameId.Value.ToString());
    }

    [Test]
    public async ValueTask map_async_executes_async_mapper_on_success() {
        // Arrange
        var position = new Position(1, 2);
        var result = Kurrent.Result.Success<Position, InvalidMoveError>(position);

        // Act
        var mapped = await result.MapAsync(async pos => {
            await Task.Delay(1);
            return new GameId(Guid.NewGuid());
        });

        // Assert
        mapped.IsSuccess.ShouldBeTrue();
        mapped.Value.ShouldBeOfType<GameId>();
    }

    [Test]
    public async ValueTask map_async_does_not_execute_async_mapper_on_failure() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Async error");
        var result = Kurrent.Result.Failure<Position, InvalidMoveError>(error);
        var mapExecuted = false;

        // Act
        var mapped = await result.MapAsync(async pos => {
            await Task.Delay(1);
            mapExecuted = true;
            return new GameId(Guid.NewGuid());
        });

        // Assert
        mapExecuted.ShouldBeFalse();
        mapped.IsFailure.ShouldBeTrue();
    }

    [Test]
    public async ValueTask map_async_chain_with_async_mapper_awaits_both() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Success<GameId, InvalidMoveError>(gameId));

        // Act
        var mapped = await resultTask.MapAsync(async id => {
            await Task.Delay(1);
            return id.Value.ToString();
        });

        // Assert
        mapped.IsSuccess.ShouldBeTrue();
        mapped.Value.ShouldBe(gameId.Value.ToString());
    }

    [Test]
    public async ValueTask map_error_async_chain_awaits_task_then_maps() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Error");
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Failure<GameId, InvalidMoveError>(error));

        // Act
        var mapped = await resultTask.MapErrorAsync(err =>
            new GameEndedError(err.GameId, GameStatus.Draw));

        // Assert
        mapped.IsFailure.ShouldBeTrue();
        mapped.Error.ShouldBeOfType<GameEndedError>();
    }

    [Test]
    public async ValueTask map_error_async_executes_async_mapper_on_failure() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Async failure");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);

        // Act
        var mapped = await result.MapErrorAsync(async err => {
            await Task.Delay(1);
            return new GameEndedError(err.GameId, GameStatus.Won);
        });

        // Assert
        mapped.IsFailure.ShouldBeTrue();
        mapped.Error.ShouldBeOfType<GameEndedError>();
        mapped.Error.Status.ShouldBe(GameStatus.Won);
    }

    [Test]
    public async ValueTask map_error_async_does_not_execute_async_mapper_on_success() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var mapExecuted = false;

        // Act
        var mapped = await result.MapErrorAsync(async err => {
            await Task.Delay(1);
            mapExecuted = true;
            return new GameEndedError(err.GameId, GameStatus.Draw);
        });

        // Assert
        mapExecuted.ShouldBeFalse();
        mapped.IsSuccess.ShouldBeTrue();
    }

    [Test]
    public async ValueTask map_error_async_chain_with_async_mapper_awaits_both() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Chain error");
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Failure<GameId, InvalidMoveError>(error));

        // Act
        var mapped = await resultTask.MapErrorAsync(async err => {
            await Task.Delay(1);
            return new GameEndedError(err.GameId, GameStatus.Won);
        });

        // Assert
        mapped.IsFailure.ShouldBeTrue();
        mapped.Error.Status.ShouldBe(GameStatus.Won);
    }

    [Test]
    public async ValueTask map_async_with_state_chain_passes_state() {
        // Arrange
        var position = new Position(2, 3);
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Success<Position, InvalidMoveError>(position));
        var multiplier = Faker.Random.Int(2, 10);

        // Act
        var mapped = await resultTask.MapAsync((mult, pos) => pos.Row * mult, multiplier);

        // Assert
        mapped.IsSuccess.ShouldBeTrue();
        mapped.Value.ShouldBe(position.Row * multiplier);
    }

    [Test]
    public async ValueTask map_async_with_state_executes_async_mapper() {
        // Arrange
        var position = new Position(1, 1);
        var result = Kurrent.Result.Success<Position, InvalidMoveError>(position);
        var prefix = Faker.Lorem.Word();

        // Act
        var mapped = await result.MapAsync(async (pre, pos) => {
            await Task.Delay(1);
            return $"{pre}: {pos.Row},{pos.Column}";
        }, prefix);

        // Assert
        mapped.IsSuccess.ShouldBeTrue();
        mapped.Value.ShouldStartWith(prefix);
    }

    [Test]
    public async ValueTask map_async_with_state_chain_with_async_mapper_awaits_both() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Success<GameId, InvalidMoveError>(gameId));
        var suffix = Faker.Lorem.Word();

        // Act
        var mapped = await resultTask.MapAsync(async (suf, id) => {
            await Task.Delay(1);
            return $"{id.Value}_{suf}";
        }, suffix);

        // Assert
        mapped.IsSuccess.ShouldBeTrue();
        mapped.Value.ShouldEndWith(suffix);
    }

    [Test]
    public async ValueTask map_error_async_with_state_chain_passes_state() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Error");
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Failure<GameId, InvalidMoveError>(error));
        var status = GameStatus.Draw;

        // Act
        var mapped = await resultTask.MapErrorAsync((st, err) =>
            new GameEndedError(err.GameId, st), status);

        // Assert
        mapped.IsFailure.ShouldBeTrue();
        mapped.Error.Status.ShouldBe(status);
    }

    [Test]
    public async ValueTask map_error_async_with_state_executes_async_mapper() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Original");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);
        var prefix = Faker.Lorem.Word();

        // Act
        var mapped = await result.MapErrorAsync(async (pre, err) => {
            await Task.Delay(1);
            return new InvalidMoveError(err.GameId, $"{pre}: {err.Reason}");
        }, prefix);

        // Assert
        mapped.IsFailure.ShouldBeTrue();
        mapped.Error.Reason.ShouldStartWith(prefix);
    }

    [Test]
    public async ValueTask map_error_async_with_state_chain_with_async_mapper_awaits_both() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Base");
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Failure<GameId, InvalidMoveError>(error));
        var suffix = Faker.Lorem.Word();

        // Act
        var mapped = await resultTask.MapErrorAsync(async (suf, err) => {
            await Task.Delay(1);
            return new InvalidMoveError(err.GameId, $"{err.Reason}_{suf}");
        }, suffix);

        // Assert
        mapped.IsFailure.ShouldBeTrue();
        mapped.Error.Reason.ShouldEndWith(suffix);
    }

    [Test]
    public void map_can_chain_multiple_transformations() {
        // Arrange
        var position = new Position(2, 3);
        var result = Kurrent.Result.Success<Position, InvalidMoveError>(position);

        // Act
        var mapped = result
            .Map(pos => pos.Row)
            .Map(row => row * 2)
            .Map(val => val.ToString());

        // Assert
        mapped.IsSuccess.ShouldBeTrue();
        mapped.Value.ShouldBe("4");
    }

    [Test]
    public void map_preserves_failure_across_chain() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Chain error");
        var result = Kurrent.Result.Failure<Position, InvalidMoveError>(error);

        // Act
        var mapped = result
            .Map(pos => pos.Row)
            .Map(row => row * 2)
            .Map(val => val.ToString());

        // Assert
        mapped.IsFailure.ShouldBeTrue();
        mapped.Error.ShouldBe(error);
    }

    [Test]
    public void map_error_preserves_success_across_chain() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);

        // Act
        var mapped = result
            .MapError(err => new GameEndedError(err.GameId, GameStatus.Draw))
            .MapError(err => new InvalidMoveError(err.GameId, "New"));

        // Assert
        mapped.IsSuccess.ShouldBeTrue();
        mapped.Value.ShouldBe(gameId);
    }

    [Test]
    public void map_and_map_error_can_be_combined() {
        // Arrange
        var position = new Position(1, 2);
        var result = Kurrent.Result.Success<Position, InvalidMoveError>(position);

        // Act
        var mapped = result
            .Map(pos => new GameId(Guid.NewGuid()))
            .MapError(err => new GameEndedError(err.GameId, GameStatus.Won));

        // Assert
        mapped.IsSuccess.ShouldBeTrue();
        mapped.Value.ShouldBeOfType<GameId>();
    }

    [Test]
    public void map_transforms_value_type_correctly() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);

        // Act
        var toInt = result.Map(id => id.Value.GetHashCode());
        var toString = toInt.Map(hash => hash.ToString());
        var toBool = result.Map(id => id.Value != Guid.Empty);

        // Assert
        toInt.Value.ShouldBeOfType<int>();
        toString.Value.ShouldBeOfType<string>();
        toBool.Value.ShouldBeOfType<bool>();
    }
}
