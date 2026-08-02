using Kurrent.Projectors;
using Kurrent.Validation;

namespace Kurrent.Surge.Streaming.Tests.Projections;

[Category("Projectors")]
public class KurrentProjectorTests {
    const string TestConnectionString = "kurrentdb://localhost:2113";

    [Test]
    public async ValueTask create_throws_validation_exception_when_client_provider_not_set() {
        // Arrange — everything else is set, so the client provider is the only rule left to break.
        var projection = new TestProjection();

        var options = new ProjectorOptions<FakeProjectionClient>();
        options.ProjectionId = "projector-validation";
        options.Connection(TestConnectionString);

        // Act / Assert
        await Assert.That(() => KurrentProjector.Create(projection, options)).Throws<ValidationException>();
    }

    sealed class TestProjection : Projection<FakeProjectionClient>;
}
