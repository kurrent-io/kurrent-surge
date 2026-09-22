using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Tests.Fixtures;

namespace Kurrent.Surge.Tests.Connectors;

[Category("Connectors")]
[Category("Integration")]
[Timeout(10_000)]
public class ConnectorErrorTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask create_throws_ConnectorAlreadyExistsException_when_connector_already_exists(CancellationToken ct) {
        // Arrange
        var connectorId = KurrentDBFixture.NewConnectorId();
        var name        = KurrentDBFixture.NewConnectorName();
        var settings    = KurrentDBFixture.NewConnectorSettings();

        await Fixture.Client.Connectors.Create(connectorId, name, settings, ct);

        // Act & Assert
        await Assert.ThrowsAsync<ConnectorAlreadyExistsException>(
            async () => await Fixture.Client.Connectors.Create(connectorId, name, settings, ct));
    }

    [Test]
    public async ValueTask get_settings_throws_ConnectorNotFoundException_when_connector_does_not_exist(CancellationToken ct) {
        // Arrange
        var connectorId = KurrentDBFixture.NewConnectorId();

        // Act & Assert — query path: NotFound = EntityNotFound
        await Assert.ThrowsAsync<ConnectorNotFoundException>(
            async () => await Fixture.Client.Connectors.GetSettings(connectorId, ct));
    }

    [Test]
    public async ValueTask delete_throws_ConnectorNotFoundException_when_connector_does_not_exist(CancellationToken ct) {
        // Arrange
        var connectorId = KurrentDBFixture.NewConnectorId();

        // Act & Assert — command path: not found surfaces as FailedPrecondition, classified by message
        await Assert.ThrowsAsync<ConnectorNotFoundException>(
            async () => await Fixture.Client.Connectors.Delete(connectorId, ct));
    }

    [Test]
    public async ValueTask starting_a_deleted_connector_throws_ConnectorDeletedException(CancellationToken ct) {
        // Arrange
        var connectorId = KurrentDBFixture.NewConnectorId();
        var name        = KurrentDBFixture.NewConnectorName();
        var settings    = KurrentDBFixture.NewConnectorSettings();

        await Fixture.Client.Connectors.Create(connectorId, name, settings, ct);
        await Fixture.Client.Connectors.Delete(connectorId, ct);

        // Act & Assert — command path: NotFound = EntityDeleted
        await Assert.ThrowsAsync<ConnectorDeletedException>(
            async () => await Fixture.Client.Connectors.Start(connectorId, ct));
    }
}
