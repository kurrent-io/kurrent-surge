using Kurrent.Projections;
using Kurrent.Projections.DuckDB;
using Kurrent.Validation;

namespace Kurrent.Processors.Tests.Projections;

[Category("Projections")]
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

    [Test]
    public async ValueTask create_with_duckdb_overload_throws_validation_exception_when_resolve_client_not_set() {
        // Arrange
        var projection = new TestDuckDBProjection();

        // Act / Assert
        await Assert.That(() =>
            KurrentProjector.Create(projection, opts => opts.Connection(TestConnectionString))
        ).Throws<ValidationException>();
    }

    sealed class TestProjection : Projection<object>;

    sealed class TestDuckDBProjection : DuckDBProjection;
}
