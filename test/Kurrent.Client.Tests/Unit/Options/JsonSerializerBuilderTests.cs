using System.Text.Json.Serialization;

namespace Kurrent.Client.Tests.Options;

[Category("Options")]
public class JsonSerializerBuilderTests {
    #region ->> Default Values <<-

    [Test]
    public async ValueTask default_builder_produces_options_with_json_defaults() {
        // Arrange & Act
        var options = new KurrentClientOptions.JsonSerializerBuilder().Build();

        // Assert
        await Assert.That(options.Schema.Json.UserJsonContexts).IsEmpty();
        await Assert.That(options.Schema.Json.UseProtobufFormatter).IsTrue();
    }

    #endregion

    #region ->> WithContexts <<-

    [Test]
    public async ValueTask with_contexts_sets_user_json_contexts() {
        // Arrange
        var context = new JsonBuilderTestContext();

        // Act
        var options = new KurrentClientOptions.JsonSerializerBuilder()
            .WithContexts(context)
            .Build();

        // Assert
        await Assert.That(options.Schema.Json.UserJsonContexts).HasSingleItem();
        await Assert.That(options.Schema.Json.UserJsonContexts[0]).IsEqualTo(context);
    }

    [Test]
    public async ValueTask with_contexts_multiple_sets_all_contexts() {
        // Arrange
        var ctx1 = new JsonBuilderTestContext();
        var ctx2 = new JsonBuilderTestContext();

        // Act
        var options = new KurrentClientOptions.JsonSerializerBuilder()
            .WithContexts(ctx1, ctx2)
            .Build();

        // Assert
        await Assert.That(options.Schema.Json.UserJsonContexts.Count).IsEqualTo(2);
    }

    #endregion

    #region ->> WithProtobufFormatter <<-

    [Test]
    public async ValueTask with_protobuf_formatter_true_enables_formatter() {
        // Arrange & Act
        var options = new KurrentClientOptions.JsonSerializerBuilder()
            .WithProtobufFormatter(true)
            .Build();

        // Assert
        await Assert.That(options.Schema.Json.UseProtobufFormatter).IsTrue();
    }

    [Test]
    public async ValueTask with_protobuf_formatter_false_disables_formatter() {
        // Arrange & Act
        var options = new KurrentClientOptions.JsonSerializerBuilder()
            .WithProtobufFormatter(false)
            .Build();

        // Assert
        await Assert.That(options.Schema.Json.UseProtobufFormatter).IsFalse();
    }

    #endregion

    #region ->> Method Chaining <<-

    [Test]
    public async ValueTask method_chaining_applies_all_configurations() {
        // Arrange
        var context = new JsonBuilderTestContext();

        // Act
        var options = new KurrentClientOptions.JsonSerializerBuilder()
            .WithContexts(context)
            .WithProtobufFormatter(false)
            .Build();

        // Assert
        await Assert.That(options.Schema.Json.UserJsonContexts).HasSingleItem();
        await Assert.That(options.Schema.Json.UseProtobufFormatter).IsFalse();
    }

    #endregion
}

// Minimal stub for testing — no actual types need to be registered
[JsonSerializable(typeof(object))]
partial class JsonBuilderTestContext : JsonSerializerContext;
