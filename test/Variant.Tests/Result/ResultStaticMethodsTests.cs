// ReSharper disable ConvertToLocalFunction

namespace Kurrent.Variant.Tests.Result;

[Category("Variant")]
public class ResultStaticMethodsTests {
    Faker Faker { get; } = new();

    #region . Sync .

    [Test]
    public async Task try_returns_success_when_function_succeeds() {
        // Arrange
        var expectedValue = new GameId(Faker.Random.Guid());
        var func          = () => expectedValue;

        // Act
        var result = Kurrent.Result.Try<GameId, SimpleError>(func, ex => new SimpleError(ex.Message));

        // Assert
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value).IsEqualTo(expectedValue);
    }

    [Test]
    public async Task try_returns_error_when_function_throws_exception() {
        // Arrange
        var          errorMessage = "Function failed";
        Func<GameId> func         = () => throw new InvalidOperationException(errorMessage);

        // Act
        var result = Kurrent.Result.Try<GameId, SimpleError>(func, ex => new SimpleError(ex.Message));

        // Assert
        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error.Message).IsEqualTo(errorMessage);
    }

    #endregion

    #region . Async .

    [Test]
    public async Task try_async_returns_success_when_async_function_succeeds() {
        // Arrange
        var expectedValue = new GameId(Faker.Random.Guid());
        var func          = () => ValueTask.FromResult(expectedValue);

        // Act
        var result = await Kurrent.Result.TryAsync<GameId, SimpleError>(func, ex => new SimpleError(ex.Message));

        // Assert
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value).IsEqualTo(expectedValue);
    }

    [Test]
    public async Task try_async_returns_error_when_async_function_throws_exception() {
        // Arrange
        var errorMessage = "Async function failed";
        var func         = () => ValueTask.FromException<GameId>(new InvalidOperationException(errorMessage));

        // Act
        var result = await Kurrent.Result.TryAsync<GameId, SimpleError>(func, ex => new SimpleError(ex.Message));

        // Assert
        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error.Message).IsEqualTo(errorMessage);
    }

    #endregion
}
