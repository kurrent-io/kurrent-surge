// ReSharper disable UnusedParameter.Local

using TicTacToe;

namespace Kurrent.Variant.Tests.Result;

[UsedImplicitly]
[Category("Variant")]
public class ResultBindTests {
    Faker Faker { get; } = new();

    [Test]
    public async Task bind_chains_success_to_another_success() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);

        // Act
        var bound = result.Bind(id =>
            Kurrent.Result.Success<Position, InvalidMoveError>(new Position(1, 2)));

        // Assert
        await Assert.That(bound.IsSuccess).IsTrue();
        await Assert.That(bound.Value).IsTypeOf<Position>();
    }

    [Test]
    public async Task bind_chains_success_to_failure() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var error = new InvalidMoveError(Faker.Random.Guid(), "Bind failed");

        // Act
        var bound = result.Bind(id =>
            Kurrent.Result.Failure<Position, InvalidMoveError>(error));

        // Assert
        await Assert.That(bound.IsFailure).IsTrue();
        await Assert.That(bound.Error).IsEqualTo(error);
    }

    [Test]
    public async Task bind_does_not_execute_on_failure() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Original error");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);
        var bindExecuted = false;

        // Act
        var bound = result.Bind(id => {
            bindExecuted = true;
            return Kurrent.Result.Success<Position, InvalidMoveError>(new Position(0, 0));
        });

        // Assert
        await Assert.That(bindExecuted).IsFalse();
        await Assert.That(bound.IsFailure).IsTrue();
        await Assert.That(bound.Error).IsEqualTo(error);
    }

    [Test]
    public async Task bind_with_state_passes_state_to_binder() {
        // Arrange
        var position = new Position(1, 2);
        var result = Kurrent.Result.Success<Position, InvalidMoveError>(position);
        var multiplier = Faker.Random.Int(2, 10);

        // Act
        var bound = result.Bind((mult, pos) =>
            Kurrent.Result.Success<int, InvalidMoveError>(pos.Row * mult), multiplier);

        // Assert
        await Assert.That(bound.IsSuccess).IsTrue();
        await Assert.That(bound.Value).IsEqualTo(position.Row * multiplier);
    }

    [Test]
    public async Task bind_with_state_does_not_execute_on_failure() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "State error");
        var result = Kurrent.Result.Failure<Position, InvalidMoveError>(error);
        var stateValue = Faker.Random.Int();
        var bindExecuted = false;

        // Act
        var bound = result.Bind((state, pos) => {
            bindExecuted = true;
            return Kurrent.Result.Success<int, InvalidMoveError>(pos.Row * state);
        }, stateValue);

        // Assert
        await Assert.That(bindExecuted).IsFalse();
        await Assert.That(bound.IsFailure).IsTrue();
    }

    [Test]
    public async Task bind_async_chain_awaits_task_then_binds() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Success<GameId, InvalidMoveError>(gameId));

        // Act
        var bound = await resultTask.BindAsync(id =>
            Kurrent.Result.Success<Position, InvalidMoveError>(new Position(1, 1)));

        // Assert
        await Assert.That(bound.IsSuccess).IsTrue();
        await Assert.That(bound.Value).IsTypeOf<Position>();
    }

    [Test]
    public async Task bind_async_executes_async_binder_on_success() {
        // Arrange
        var position = new Position(2, 3);
        var result = Kurrent.Result.Success<Position, InvalidMoveError>(position);

        // Act
        var bound = await result.BindAsync(async pos => {
            await Task.Delay(1);
            return Kurrent.Result.Success<GameId, InvalidMoveError>(new GameId(Guid.NewGuid()));
        });

        // Assert
        await Assert.That(bound.IsSuccess).IsTrue();
        await Assert.That(bound.Value).IsTypeOf<GameId>();
    }

    [Test]
    public async Task bind_async_can_return_failure_from_async_binder() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var error = new InvalidMoveError(Faker.Random.Guid(), "Async bind failed");

        // Act
        var bound = await result.BindAsync(async id => {
            await Task.Delay(1);
            return Kurrent.Result.Failure<Position, InvalidMoveError>(error);
        });

        // Assert
        await Assert.That(bound.IsFailure).IsTrue();
        await Assert.That(bound.Error).IsEqualTo(error);
    }

    [Test]
    public async Task bind_async_does_not_execute_async_binder_on_failure() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Original");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);
        var bindExecuted = false;

        // Act
        var bound = await result.BindAsync(async id => {
            await Task.Delay(1);
            bindExecuted = true;
            return Kurrent.Result.Success<Position, InvalidMoveError>(new Position(0, 0));
        });

        // Assert
        await Assert.That(bindExecuted).IsFalse();
        await Assert.That(bound.IsFailure).IsTrue();
    }

    [Test]
    public async Task bind_async_chain_with_async_binder_awaits_both() {
        // Arrange
        var position = new Position(1, 2);
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Success<Position, InvalidMoveError>(position));

        // Act
        var bound = await resultTask.BindAsync(async pos => {
            await Task.Delay(1);
            return Kurrent.Result.Success<string, InvalidMoveError>($"{pos.Row},{pos.Column}");
        });

        // Assert
        await Assert.That(bound.IsSuccess).IsTrue();
        await Assert.That(bound.Value).IsEqualTo("1,2");
    }

    [Test]
    public async Task bind_async_with_state_chain_passes_state() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Success<GameId, InvalidMoveError>(gameId));
        var prefix = Faker.Lorem.Word();

        // Act
        var bound = await resultTask.BindAsync((pre, id) =>
            Kurrent.Result.Success<string, InvalidMoveError>($"{pre}: {id.Value}"), prefix);

        // Assert
        await Assert.That(bound.IsSuccess).IsTrue();
        await Assert.That(bound.Value).StartsWith(prefix);
    }

    [Test]
    public async Task bind_async_with_state_executes_async_binder() {
        // Arrange
        var position = new Position(2, 2);
        var result = Kurrent.Result.Success<Position, InvalidMoveError>(position);
        var multiplier = Faker.Random.Int(2, 10);

        // Act
        var bound = await result.BindAsync(async (mult, pos) => {
            await Task.Delay(1);
            return Kurrent.Result.Success<int, InvalidMoveError>(pos.Row * mult);
        }, multiplier);

        // Assert
        await Assert.That(bound.IsSuccess).IsTrue();
        await Assert.That(bound.Value).IsEqualTo(position.Row * multiplier);
    }

    [Test]
    public async Task bind_async_with_state_does_not_execute_on_failure() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Failure");
        var result = Kurrent.Result.Failure<Position, InvalidMoveError>(error);
        var stateValue = Faker.Random.Int();
        var bindExecuted = false;

        // Act
        var bound = await result.BindAsync(async (state, pos) => {
            await Task.Delay(1);
            bindExecuted = true;
            return Kurrent.Result.Success<int, InvalidMoveError>(pos.Row * state);
        }, stateValue);

        // Assert
        await Assert.That(bindExecuted).IsFalse();
        await Assert.That(bound.IsFailure).IsTrue();
    }

    [Test]
    public async Task bind_async_with_state_chain_with_async_binder_awaits_both() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var resultTask = ValueTask.FromResult(
            Kurrent.Result.Success<GameId, InvalidMoveError>(gameId));
        var suffix = Faker.Lorem.Word();

        // Act
        var bound = await resultTask.BindAsync(async (suf, id) => {
            await Task.Delay(1);
            return Kurrent.Result.Success<string, InvalidMoveError>($"{id.Value}_{suf}");
        }, suffix);

        // Assert
        await Assert.That(bound.IsSuccess).IsTrue();
        await Assert.That(bound.Value).EndsWith(suffix);
    }

    [Test]
    public async Task bind_can_chain_multiple_operations() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);

        // Act
        var bound = result
            .Bind(id => Kurrent.Result.Success<Position, InvalidMoveError>(new Position(1, 2)))
            .Bind(pos => Kurrent.Result.Success<int, InvalidMoveError>(pos.Row))
            .Bind(row => Kurrent.Result.Success<string, InvalidMoveError>(row.ToString()));

        // Assert
        await Assert.That(bound.IsSuccess).IsTrue();
        await Assert.That(bound.Value).IsEqualTo("1");
    }

    [Test]
    public async Task bind_short_circuits_on_first_failure() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);
        var error = new InvalidMoveError(Faker.Random.Guid(), "First failure");
        var secondBindExecuted = false;

        // Act
        var bound = result
            .Bind(id => Kurrent.Result.Failure<Position, InvalidMoveError>(error))
            .Bind(pos => {
                secondBindExecuted = true;
                return Kurrent.Result.Success<int, InvalidMoveError>(pos.Row);
            });

        // Assert
        await Assert.That(secondBindExecuted).IsFalse();
        await Assert.That(bound.IsFailure).IsTrue();
        await Assert.That(bound.Error).IsEqualTo(error);
    }

    [Test]
    public async Task bind_preserves_original_failure_through_chain() {
        // Arrange
        var error = new InvalidMoveError(Faker.Random.Guid(), "Original");
        var result = Kurrent.Result.Failure<GameId, InvalidMoveError>(error);

        // Act
        var bound = result
            .Bind(id => Kurrent.Result.Success<Position, InvalidMoveError>(new Position(0, 0)))
            .Bind(pos => Kurrent.Result.Success<int, InvalidMoveError>(pos.Row));

        // Assert
        await Assert.That(bound.IsFailure).IsTrue();
        await Assert.That(bound.Error).IsEqualTo(error);
    }

    [Test]
    public async Task bind_combined_with_map_enables_complex_pipelines() {
        // Arrange
        var position = new Position(2, 3);
        var result = Kurrent.Result.Success<Position, InvalidMoveError>(position);

        // Act
        var pipeline = result
            .Map(pos => pos.Row)
            .Bind(row => Kurrent.Result.Success<string, InvalidMoveError>(row.ToString()))
            .Map(str => $"Row: {str}");

        // Assert
        await Assert.That(pipeline.IsSuccess).IsTrue();
        await Assert.That(pipeline.Value).IsEqualTo("Row: 2");
    }

    [Test]
    public async Task bind_enables_conditional_logic_in_chain() {
        // Arrange
        var position = new Position(1, 1);
        var result = Kurrent.Result.Success<Position, InvalidMoveError>(position);

        // Act
        var bound = result.Bind(pos => pos.Row == 1 && pos.Column == 1
            ? Kurrent.Result.Success<string, InvalidMoveError>("Center")
            : Kurrent.Result.Failure<string, InvalidMoveError>(
                new InvalidMoveError(Guid.Empty, "Not center")));

        // Assert
        await Assert.That(bound.IsSuccess).IsTrue();
        await Assert.That(bound.Value).IsEqualTo("Center");
    }

    [Test]
    public async Task bind_with_conditional_can_fail() {
        // Arrange
        var position = new Position(0, 2);
        var result = Kurrent.Result.Success<Position, InvalidMoveError>(position);

        // Act
        var bound = result.Bind(pos => pos.Row == 1 && pos.Column == 1
            ? Kurrent.Result.Success<string, InvalidMoveError>("Center")
            : Kurrent.Result.Failure<string, InvalidMoveError>(
                new InvalidMoveError(Guid.Empty, "Not center")));

        // Assert
        await Assert.That(bound.IsFailure).IsTrue();
        await Assert.That(bound.Error.Reason).IsEqualTo("Not center");
    }

    [Test]
    public async Task bind_vs_map_difference_demonstration() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);

        // Act - Map returns TValue, Bind returns Result<TValue>
        var mapped = result.Map(id => new Position(1, 2));
        var bound = result.Bind(id =>
            Kurrent.Result.Success<Position, InvalidMoveError>(new Position(1, 2)));

        // Assert - Both succeed but Bind could have returned failure
        await Assert.That(mapped.IsSuccess).IsTrue();
        await Assert.That(bound.IsSuccess).IsTrue();
        await Assert.That(mapped.Value).IsEquivalentTo(bound.Value);
    }

    [Test]
    public async Task bind_enables_railway_oriented_programming() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);

        // Act - Simulate validation pipeline
        var pipeline = result
            .Bind(id => id.Value != Guid.Empty
                ? Kurrent.Result.Success<GameId, InvalidMoveError>(id)
                : Kurrent.Result.Failure<GameId, InvalidMoveError>(
                    new InvalidMoveError(Guid.Empty, "Invalid ID")))
            .Bind(id => Kurrent.Result.Success<Position, InvalidMoveError>(new Position(1, 2)))
            .Map(pos => $"Valid position: {pos.Row},{pos.Column}");

        // Assert
        await Assert.That(pipeline.IsSuccess).IsTrue();
        await Assert.That(pipeline.Value).StartsWith("Valid position");
    }

    [Test]
    public async Task bind_async_enables_complex_async_workflows() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);

        // Act - Simulate async validation and transformation pipeline
        var pipeline = await result
            .BindAsync(async id => {
                await Task.Delay(1);
                return Kurrent.Result.Success<Position, InvalidMoveError>(new Position(2, 2));
            })
            .BindAsync(async pos => {
                await Task.Delay(1);
                return pos.Row == pos.Column
                    ? Kurrent.Result.Success<string, InvalidMoveError>("Diagonal")
                    : Kurrent.Result.Failure<string, InvalidMoveError>(
                        new InvalidMoveError(Guid.Empty, "Not diagonal"));
            });

        // Assert
        await Assert.That(pipeline.IsSuccess).IsTrue();
        await Assert.That(pipeline.Value).IsEqualTo("Diagonal");
    }

    [Test]
    public async Task bind_error_type_remains_consistent_through_chain() {
        // Arrange
        var gameId = new GameId(Faker.Random.Guid());
        var result = Kurrent.Result.Success<GameId, InvalidMoveError>(gameId);

        // Act
        var bound = result
            .Bind(id => Kurrent.Result.Success<Position, InvalidMoveError>(new Position(1, 1)))
            .Bind(pos => Kurrent.Result.Success<int, InvalidMoveError>(pos.Row));

        // Assert - Error type is InvalidMoveError throughout
        await Assert.That(bound.IsSuccess).IsTrue();
        // If it were a failure, Error would be InvalidMoveError type
    }
}
