using Kurrent.Client.Schema;
using Kurrent.Client.Schema.NameStrategies;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Options;

[Category("Options")]
public class SchemaBuilderTests {
    #region ->> Default Values <<-

    [Test]
    public async ValueTask default_builder_produces_options_with_schema_defaults() {
        // Arrange & Act
        var options = new KurrentClientOptions.SchemaBuilder().Build();

        // Assert
        await Assert.That(options.Schema.AutoRegister).IsTrue();
        await Assert.That(options.Schema.PreValidate).IsTrue();
        await Assert.That(options.Schema.Mapper).IsNotNull();
    }

    #endregion

    #region ->> WithAutoRegister <<-

    [Test]
    public async ValueTask with_auto_register_true_enables_auto_registration() {
        // Arrange & Act
        var options = new KurrentClientOptions.SchemaBuilder()
            .WithAutoRegister(true)
            .Build();

        // Assert
        await Assert.That(options.Schema.AutoRegister).IsTrue();
    }

    [Test]
    public async ValueTask with_auto_register_false_disables_auto_registration() {
        // Arrange & Act
        var options = new KurrentClientOptions.SchemaBuilder()
            .WithAutoRegister(false)
            .Build();

        // Assert
        await Assert.That(options.Schema.AutoRegister).IsFalse();
    }

    #endregion

    #region ->> WithValidation <<-

    [Test]
    public async ValueTask with_validation_true_enables_pre_validation() {
        // Arrange & Act
        var options = new KurrentClientOptions.SchemaBuilder()
            .WithValidation(true)
            .Build();

        // Assert
        await Assert.That(options.Schema.PreValidate).IsTrue();
    }

    [Test]
    public async ValueTask with_validation_false_disables_pre_validation() {
        // Arrange & Act
        var options = new KurrentClientOptions.SchemaBuilder()
            .WithValidation(false)
            .Build();

        // Assert
        await Assert.That(options.Schema.PreValidate).IsFalse();
    }

    #endregion

    #region ->> WithNameStrategy <<-

    [Test]
    public async ValueTask with_name_strategy_sets_strategy() {
        // Arrange
        var strategy = new NamespaceSchemaNameStrategy("com.example");

        // Act
        var options = new KurrentClientOptions.SchemaBuilder()
            .WithNameStrategy(strategy)
            .Build();

        // Assert
        await Assert.That(options.Schema.NameStrategy).IsEqualTo(strategy);
    }

    #endregion

    #region ->> ConfigureMessages <<-

    [Test]
    public async ValueTask configure_messages_with_action_creates_new_mapper() {
        // Arrange
        var originalOptions = new KurrentClientOptions();

        // Act
        var options = new KurrentClientOptions.SchemaBuilder(originalOptions)
            .ConfigureMessages(mapper => mapper.Map<StreamMetadata>("$metadata"))
            .Build();

        // Assert
        await Assert.That(options.Schema.Mapper).IsNotEqualTo(originalOptions.Schema.Mapper);
        await Assert.That(options.Schema.Mapper.TryGetSchemaName(typeof(StreamMetadata), out var schemaName)).IsTrue();
        await Assert.That(schemaName.Value).IsEqualTo("$metadata");
    }

    [Test]
    public async ValueTask configure_messages_with_mapper_instance_replaces_mapper() {
        // Arrange
        var mapper = new MessageTypeMapper();
        mapper.Map<StreamMetadata>("custom-schema");

        // Act
        var options = new KurrentClientOptions.SchemaBuilder()
            .ConfigureMessages(mapper)
            .Build();

        // Assert
        await Assert.That(options.Schema.Mapper).IsEqualTo(mapper);
        await Assert.That(options.Schema.Mapper.TryGetSchemaName(typeof(StreamMetadata), out _)).IsTrue();
    }

    [Test]
    public void configure_messages_with_null_mapper_throws() {
        // Arrange
        var builder = new KurrentClientOptions.SchemaBuilder();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.ConfigureMessages((MessageTypeMapper)null!).Build());
    }

    #endregion

    #region ->> Method Chaining <<-

    [Test]
    public async ValueTask method_chaining_applies_all_configurations() {
        // Arrange & Act
        var options = new KurrentClientOptions.SchemaBuilder()
            .WithAutoRegister(false)
            .WithValidation(false)
            .ConfigureMessages(mapper => mapper.Map<StreamMetadata>())
            .Build();

        // Assert
        await Assert.That(options.Schema.AutoRegister).IsFalse();
        await Assert.That(options.Schema.PreValidate).IsFalse();
        await Assert.That(options.Schema.Mapper.TryGetSchemaName(typeof(StreamMetadata), out _)).IsTrue();
    }

    #endregion
}
