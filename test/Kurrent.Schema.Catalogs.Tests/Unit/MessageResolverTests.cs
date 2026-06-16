using Kurrent.Schema.Catalogs;
using Kurrent.Schema.Naming;

namespace Kurrent.Schema.Catalogs.Tests.Unit;

[Category("Schema")]
public class MessageResolverTests {
    #region ->> Resolver: mapped or not, no in-between <<-

    [Test]
    public async ValueTask resolver_resolves_pinned_schema_name() {
        // Arrange
        var resolver = MessageResolver.From(new MessageCatalog("test")
            .Map<PinnedOrderPlaced>("pinned.order-placed.v9"));

        // Act
        var schemaName = resolver.ResolveSchemaName(typeof(PinnedOrderPlaced));

        // Assert
        await Assert.That(schemaName.Value).IsEqualTo("pinned.order-placed.v9");
    }

    [Test]
    public async ValueTask resolver_throws_on_unmapped_append() {
        // Arrange — no auto-mapping: if it's not mapped, appending throws.
        var resolver = MessageResolver.From(new MessageCatalog("test"));

        // Act
        var resolving = () => resolver.ResolveSchemaName(typeof(ScannedOrderPlaced));

        // Assert
        await Assert.That(resolving).Throws<SchemaNameNotMappedException>();
    }

    [Test]
    public async ValueTask resolver_returns_null_on_unresolved_read_by_default() {
        // Arrange — forward-unknown types are data for fallback handlers, not errors.
        var resolver = MessageResolver.From(new MessageCatalog("test"));

        // Act
        var resolved = resolver.ResolveType("unknown.schema.v1");

        // Assert
        await Assert.That(resolved).IsNull();
    }

    [Test]
    public async ValueTask resolver_snapshot_ignores_catalog_changes_after_creation() {
        // Arrange
        var catalog  = new MessageCatalog("test").Map<PinnedOrderPlaced>("pinned.v1");
        var resolver = MessageResolver.From(catalog);

        // Act — mutate the catalog AFTER the snapshot.
        catalog.Map<ScannedOrderPlaced>("late.v1");

        // Assert — the resolver is a copy; the late mapping is invisible to it.
        await Assert.That(resolver.ResolveType("late.v1")).IsNull();
        await Assert.That(resolver.ResolveType("pinned.v1")).IsEqualTo(typeof(PinnedOrderPlaced));
    }

    #endregion

    #region ->> Registration resolution <<-

    [Test]
    public async ValueTask registration_resolution_prefers_pinned_name_over_strategy() {
        // Arrange
        var registration = new MessageTypeDescriptor(typeof(ScannedOrderPlaced), "pinned.wins.v1", new KebabNamesStrategy());

        // Act
        var schemaName = registration.ResolveSchemaName();

        // Assert
        await Assert.That(schemaName.Value).IsEqualTo("pinned.wins.v1");
    }

    [Test]
    public async ValueTask registration_resolution_uses_strategy_then_default_convention() {
        // Arrange
        var withStrategy = new MessageTypeDescriptor(typeof(ScannedOrderPlaced), Strategy: new KebabNamesStrategy());
        var bare         = new MessageTypeDescriptor(typeof(ScannedOrderPlaced));

        var expectedKebab   = new KebabNamesStrategy().GenerateSchemaName(typeof(ScannedOrderPlaced));
        var expectedDefault = new MessageSchemaNameStrategy().GenerateSchemaName(typeof(ScannedOrderPlaced));

        // Assert
        await Assert.That(withStrategy.ResolveSchemaName()).IsEqualTo(expectedKebab);
        await Assert.That(bare.ResolveSchemaName()).IsEqualTo(expectedDefault);
    }

    #endregion

    #region ->> Sample contracts <<-

    // Minimal record contracts the resolver tests map. Declared locally so this assembly stays
    // free of the Kurrent.Client.Tests sample types and proves the package resolver in isolation.
    record ScannedOrderPlaced(string OrderId);

    record PinnedOrderPlaced(string Id);

    // Kebab-case naming strategy, mirroring the form used across the catalog samples.
    sealed class KebabNamesStrategy() : MessageSchemaNameStrategy(SchemaNameOutputFormat.KebabCase);

    #endregion
}
