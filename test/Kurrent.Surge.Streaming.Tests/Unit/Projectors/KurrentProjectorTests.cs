using Kurrent.Projectors;
using Kurrent.Validation;

namespace Kurrent.Surge.Streaming.Tests.Projections;

[Category("Projectors")]
public class KurrentProjectorTests {
    const string TestConnectionString = "kurrentdb://localhost:2113";

    [Test]
    public async ValueTask create_throws_validation_exception_when_resolve_client_not_set() {
        // Arrange
        var projection = new TestProjection();

        // Act / Assert
        await Assert.That(() =>
            KurrentProjector.Create(projection, opts => opts.Connection(TestConnectionString))
        ).Throws<ValidationException>();
    }

    sealed class TestProjection : Projection<object>;
}
