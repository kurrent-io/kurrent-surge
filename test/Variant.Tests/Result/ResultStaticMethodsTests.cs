// ReSharper disable ConvertToLocalFunction

namespace Kurrent.Variant.Tests.Result;

public class ResultStaticMethodsTests {
    Faker Faker { get; } = new();

    #region . Sync .

    [Test]
    public void try_returns_success_when_function_succeeds() {
        // Arrange
        var expectedValue = new GameId(Faker.Random.Guid());
        var func          = () => expectedValue;

        // Act
        var result = Kurrent.Result.Try<GameId, SimpleError>(func, ex => new SimpleError(ex.Message));

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(expectedValue);
    }

    [Test]
    public void try_returns_error_when_function_throws_exception() {
        // Arrange
        var          errorMessage = "Function failed";
        Func<GameId> func         = () => throw new InvalidOperationException(errorMessage);

        // Act
        var result = Kurrent.Result.Try<GameId, SimpleError>(func, ex => new SimpleError(ex.Message));

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Message.ShouldBe(errorMessage);
    }

    #endregion

    #region . Async .

    [Test]
    public async ValueTask try_async_returns_success_when_async_function_succeeds() {
        // Arrange
        var expectedValue = new GameId(Faker.Random.Guid());
        var func          = () => ValueTask.FromResult(expectedValue);

        // Act
        var result = await Kurrent.Result.TryAsync<GameId, SimpleError>(func, ex => new SimpleError(ex.Message));

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(expectedValue);
    }

    [Test]
    public async ValueTask try_async_returns_error_when_async_function_throws_exception() {
        // Arrange
        var errorMessage = "Async function failed";
        var func         = () => ValueTask.FromException<GameId>(new InvalidOperationException(errorMessage));

        // Act
        var result = await Kurrent.Result.TryAsync<GameId, SimpleError>(func, ex => new SimpleError(ex.Message));

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Message.ShouldBe(errorMessage);
    }

    #endregion
}
