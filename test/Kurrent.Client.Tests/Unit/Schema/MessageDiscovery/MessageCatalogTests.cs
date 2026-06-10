using Kurrent.Client.Schema;
using Kurrent.Client.Schema.NameStrategies;
using Kurrent.Client.Tests.Schema.Catalogs.Ambient;
using Kurrent.Client.Tests.Schema.Catalogs.Custom;
using Kurrent.Client.Tests.Schema.Catalogs.Explicit;
using Kurrent.Client.Tests.Schema.Catalogs.External;
using Kurrent.Client.Tests.Schema.Catalogs.Filtered;
using Kurrent.Client.Tests.Schema.Catalogs.Scanned;
using GeneratedCatalogs = Kurrent.Client.Tests.MessageCatalogs;

namespace Kurrent.Client.Tests.Schema;

[Category("Schema")]
public class MessageCatalogTests {
    #region ->> Generated catalogs <<-

    [Test]
    public async ValueTask scanned_catalog_contains_types_matching_namespace_prefix() {
        // Arrange
        var types = GeneratedCatalogs.Scanned.Types;

        // Assert
        await Assert.That(types).Contains(typeof(ScannedOrderPlaced));
        await Assert.That(types).Contains(typeof(ScannedOrderShipped));
    }

    [Test]
    public async ValueTask scanned_catalog_excludes_abstract_static_and_open_generic_types() {
        // Arrange
        var types = GeneratedCatalogs.Scanned.Types;

        // Assert
        await Assert.That(types).DoesNotContain(typeof(AbstractMessage));
        await Assert.That(types).DoesNotContain(typeof(StaticHolder));
        await Assert.That(types).DoesNotContain(typeof(OpenGenericMessage<>));
    }

    [Test]
    public async ValueTask filtered_catalog_honors_must_implement_generic() {
        // Arrange
        var types = GeneratedCatalogs.Filtered.Types;

        // Assert
        await Assert.That(types).Contains(typeof(MarkedMessage));
        await Assert.That(types).DoesNotContain(typeof(UnmarkedMessage));
    }

    [Test]
    public async ValueTask record_type_attributes_group_into_their_catalog() {
        // Arrange
        var pinned = GeneratedCatalogs.Get("pinned");

        // Assert
        await Assert.That(pinned).IsNotNull();
        await Assert.That(pinned!.Types).Contains(typeof(PinnedOrderPlaced));
        await Assert.That(pinned.Types).Contains(typeof(KebabOrderArchived));
    }

    [Test]
    public async ValueTask catalog_less_registrations_land_in_default_catalog() {
        // Arrange
        var defaultCatalog = GeneratedCatalogs.Get("Default");

        // Assert
        await Assert.That(defaultCatalog).IsNotNull();
        await Assert.That(defaultCatalog!.Types).Contains(typeof(ExternalInvoiceIssued));
    }

    [Test]
    public async ValueTask get_finds_custom_catalog_by_runtime_key() {
        // Arrange — the key is a runtime ctor argument the generator never sees;
        // Get resolves it against All instead of a generated switch.
        var catalog = GeneratedCatalogs.Get("hand-rolled");

        // Assert — same instance the generated property exposes.
        await Assert.That(catalog).IsEqualTo(GeneratedCatalogs.HandRolled);
    }

    [Test]
    public async ValueTask get_returns_null_for_unknown_key() {
        // Arrange
        var missing = GeneratedCatalogs.Get("nope");

        // Assert — null on miss enables pattern matching at call sites.
        await Assert.That(missing).IsNull();
    }

    [Test]
    public async ValueTask custom_catalog_is_exposed_as_property_and_shares_its_instance_with_all() {
        // Arrange — property name derives from the type name: HandRolledCatalog → HandRolled.
        var catalog = GeneratedCatalogs.HandRolled;

        // Assert — All reuses the property's instance, so there is a single template.
        await Assert.That(catalog.Key).IsEqualTo("hand-rolled");
        await Assert.That(GeneratedCatalogs.All).Contains(catalog);
    }

    [Test]
    public async ValueTask custom_catalog_generates_use_extension() {
        // Arrange
        var options = new KurrentClientMessageOptions();
        options.UseHandRolledCatalog();

        // Act
        var resolver = options.BuildResolver();

        // Assert
        await Assert.That(resolver.ResolveSchemaName(typeof(HandRolledPing)).Value).IsEqualTo("hand-rolled.ping.v1");
    }

    [Test]
    public async ValueTask all_exposes_every_declared_catalog() {
        // Arrange
        var keys = GeneratedCatalogs.All.Select(catalog => catalog.Key).ToArray();

        // Assert
        await Assert.That(keys).Contains("scanned");
        await Assert.That(keys).Contains("filtered");
        await Assert.That(keys).Contains("pinned");
        await Assert.That(keys).Contains("Default");
    }

    #endregion

    #region ->> Resolver: mapped or not, no in-between <<-

    [Test]
    public async ValueTask resolver_resolves_pinned_schema_name() {
        // Arrange
        var resolver = new MessageCatalog("test")
            .Map<PinnedOrderPlaced>("pinned.order-placed.v9")
            .GetResolver();

        // Act
        var schemaName = resolver.ResolveSchemaName(typeof(PinnedOrderPlaced));

        // Assert
        await Assert.That(schemaName.Value).IsEqualTo("pinned.order-placed.v9");
    }

    [Test]
    public async ValueTask resolver_throws_on_unmapped_append() {
        // Arrange — no auto-mapping: if it's not mapped, appending throws.
        var resolver = new MessageCatalog("test").GetResolver();

        // Act
        var resolving = () => resolver.ResolveSchemaName(typeof(ScannedOrderPlaced));

        // Assert
        await Assert.That(resolving).Throws<SchemaNameNotMappedException>();
    }

    [Test]
    public async ValueTask resolver_returns_null_on_unresolved_read_by_default() {
        // Arrange — forward-unknown types are data for fallback handlers, not errors.
        var resolver = new MessageCatalog("test").GetResolver();

        // Act
        var resolved = resolver.ResolveType("unknown.schema.v1");

        // Assert
        await Assert.That(resolved).IsNull();
    }

    [Test]
    public async ValueTask resolver_snapshot_ignores_catalog_changes_after_creation() {
        // Arrange
        var catalog  = new MessageCatalog("test").Map<PinnedOrderPlaced>("pinned.v1");
        var resolver = catalog.GetResolver();

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

    #region ->> Options: selection + freeze <<-

    [Test]
    public async ValueTask use_catalog_extension_methods_are_generated() {
        // Arrange — the generated ASP.NET-style extensions compose catalogs fluently.
        var options = new KurrentClientMessageOptions();

        // Act
        options.UseScannedCatalog().UsePinnedCatalog();
        var resolver = options.BuildResolver();

        // Assert
        await Assert.That(resolver.ResolveSchemaName(typeof(ScannedOrderPlaced)).Value).IsNotNull();
        await Assert.That(resolver.ResolveSchemaName(typeof(PinnedOrderPlaced)).Value).IsEqualTo("pinned.order-placed.v9");
    }

    [Test]
    public async ValueTask declared_catalogs_are_not_registered_until_selected() {
        // Arrange — discovery finds every provider-exported catalog, but by default only
        // always-active catalogs register; declared-but-inactive catalogs stay opt-in.
        var options = new KurrentClientMessageOptions();

        // Act
        var resolver = options.BuildResolver();
        var scanned  = () => resolver.ResolveSchemaName(typeof(ScannedOrderPlaced));
        var external = () => resolver.ResolveSchemaName(typeof(ExternalInvoiceIssued));

        // Assert — neither resolves without an explicit UseCatalog/UseXCatalog.
        await Assert.That(scanned).Throws<SchemaNameNotMappedException>();
        await Assert.That(external).Throws<SchemaNameNotMappedException>();
    }

    [Test]
    public async ValueTask selecting_a_catalog_excludes_unselected_ones() {
        // Arrange
        var options = new KurrentClientMessageOptions();
        options.UseScannedCatalog();

        var resolver = options.BuildResolver();

        // Act — PinnedOrderPlaced lives in the unselected "pinned" catalog.
        var resolving = () => resolver.ResolveSchemaName(typeof(PinnedOrderPlaced));

        // Assert
        await Assert.That(resolving).Throws<SchemaNameNotMappedException>();
    }

    [Test]
    public async ValueTask map_message_lands_in_default_catalog_and_resolves() {
        // Arrange
        var options = new KurrentClientMessageOptions();
        options.MapMessage<UnmarkedMessage>("unmarked.v1");

        // Act
        var resolver = options.BuildResolver();

        // Assert
        await Assert.That(resolver.ResolveType("unmarked.v1")).IsEqualTo(typeof(UnmarkedMessage));
    }

    [Test]
    public async ValueTask always_active_attribute_catalog_is_registered_without_opt_in() {
        // Arrange — [assembly: MessageCatalog(..., AlwaysActive = true)] needs no UseCatalog.
        var options = new KurrentClientMessageOptions();

        // Act
        var resolver = options.BuildResolver();

        // Assert
        await Assert.That(resolver.ResolveSchemaName(typeof(AmbientSignal)).Value).IsNotNull();
    }

    [Test]
    public async ValueTask always_active_catalogs_are_included_by_default() {
        // Arrange — $metadata, $>, scavenge events register without any opt-in.
        var options = new KurrentClientMessageOptions();

        // Act
        var resolver = options.BuildResolver();

        // Assert
        await Assert.That(resolver.ResolveType("$metadata")).IsNotNull();
        await Assert.That(resolver.ResolveType("$>")).IsEqualTo(typeof(string));
    }

    #endregion
}
