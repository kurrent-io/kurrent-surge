using Kurrent.Client.Schema;
using Kurrent.Client.Schema.NameStrategies;

namespace Kurrent.Client.Tests.Schema;

[Category("Schema")]
public class MessageTypeMappingBuilderTests {
    #region ->> Default Values <<-

    [Test]
    public async ValueTask default_builder_builds_empty_mapper() {
        // Arrange & Act
        var mapper = new MessageTypeMapper.Builder().Build();

        // Assert
        await Assert.That(mapper).IsNotNull();
        await Assert.That(mapper.TryGetSchemaName(typeof(SampleMessage), out _)).IsFalse();
    }

    #endregion

    #region ->> Map<T> <<-

    [Test]
    public async ValueTask map_with_explicit_schema_name_registers_type() {
        // Arrange
        const string schemaName = "sample-message-v1";

        // Act
        var mapper = new MessageTypeMapper.Builder()
            .Map<SampleMessage>(schemaName)
            .Build();

        // Assert
        await Assert.That(mapper.TryGetSchemaName(typeof(SampleMessage), out var name)).IsTrue();
        await Assert.That(name.Value).IsEqualTo(schemaName);
    }

    [Test]
    public async ValueTask map_without_schema_name_uses_default_strategy() {
        // Arrange & Act
        var mapper = new MessageTypeMapper.Builder()
            .Map<SampleMessage>()
            .Build();

        // Assert
        await Assert.That(mapper.TryGetSchemaName(typeof(SampleMessage), out _)).IsTrue();
    }

    [Test]
    public async ValueTask map_registers_multiple_types() {
        // Arrange & Act
        var mapper = new MessageTypeMapper.Builder()
            .Map<SampleMessage>("message-a")
            .Map<AnotherMessage>("message-b")
            .Build();

        // Assert
        await Assert.That(mapper.TryGetSchemaName(typeof(SampleMessage), out var nameA)).IsTrue();
        await Assert.That(mapper.TryGetSchemaName(typeof(AnotherMessage), out var nameB)).IsTrue();
        await Assert.That(nameA.Value).IsEqualTo("message-a");
        await Assert.That(nameB.Value).IsEqualTo("message-b");
    }

    #endregion

    #region ->> TryMap<T> <<-

    [Test]
    public async ValueTask try_map_with_explicit_schema_name_registers_type() {
        // Arrange
        const string schemaName = "try-mapped-message";

        // Act
        var mapper = new MessageTypeMapper.Builder()
            .TryMap<SampleMessage>(schemaName)
            .Build();

        // Assert
        await Assert.That(mapper.TryGetSchemaName(typeof(SampleMessage), out var name)).IsTrue();
        await Assert.That(name.Value).IsEqualTo(schemaName);
    }

    [Test]
    public async ValueTask try_map_without_schema_name_uses_default_strategy() {
        // Arrange & Act
        var mapper = new MessageTypeMapper.Builder()
            .TryMap<SampleMessage>()
            .Build();

        // Assert
        await Assert.That(mapper.TryGetSchemaName(typeof(SampleMessage), out _)).IsTrue();
    }

    #endregion

    #region ->> WithStrategy <<-

    [Test]
    public async ValueTask with_strategy_uses_custom_naming_strategy() {
        // Arrange
        var strategy = new NamespaceSchemaNameStrategy("com.example");

        // Act
        var mapper = new MessageTypeMapper.Builder()
            .WithStrategy(strategy)
            .Map<SampleMessage>()
            .Build();

        // Assert — strategy generates a namespace-based name
        await Assert.That(mapper.TryGetSchemaName(typeof(SampleMessage), out var name)).IsTrue();
        await Assert.That(name.Value).IsNotNullOrEmpty();
    }

    [Test]
    public void with_strategy_throws_when_null() {
        // Arrange
        var builder = new MessageTypeMapper.Builder();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.WithStrategy(null!));
    }

    #endregion

    #region ->> Build Returns Working Mapper <<-

    [Test]
    public async ValueTask build_returns_mapper_that_resolves_type_by_schema_name() {
        // Arrange
        const string schemaName = "my-schema";

        // Act
        var mapper = new MessageTypeMapper.Builder()
            .Map<SampleMessage>(schemaName)
            .Build();

        var found = mapper.TryGetMessageType(schemaName, out var resolvedType);

        // Assert
        await Assert.That(found).IsTrue();
        await Assert.That(resolvedType).IsEqualTo(typeof(SampleMessage));
    }

    [Test]
    public async ValueTask build_returns_mapper_that_confirms_type_is_mapped() {
        // Arrange & Act
        var mapper = new MessageTypeMapper.Builder()
            .Map<SampleMessage>("schema-name")
            .Build();

        // Assert
        await Assert.That(mapper.ContainsMapTo(typeof(SampleMessage))).IsTrue();
        await Assert.That(mapper.ContainsMapTo(typeof(AnotherMessage))).IsFalse();
    }

    #endregion

    #region ->> Method Chaining <<-

    [Test]
    public async ValueTask method_chaining_maps_all_types() {
        // Arrange & Act
        var mapper = new MessageTypeMapper.Builder()
            .WithStrategy(new MessageSchemaNameStrategy())
            .Map<SampleMessage>("chained-a")
            .Map<AnotherMessage>("chained-b")
            .Build();

        // Assert
        await Assert.That(mapper.TryGetSchemaName(typeof(SampleMessage), out var a)).IsTrue();
        await Assert.That(mapper.TryGetSchemaName(typeof(AnotherMessage), out var b)).IsTrue();
        await Assert.That(a.Value).IsEqualTo("chained-a");
        await Assert.That(b.Value).IsEqualTo("chained-b");
    }

    #endregion

    // Test message types
    record SampleMessage(string Value);
    record AnotherMessage(int Count);
}
