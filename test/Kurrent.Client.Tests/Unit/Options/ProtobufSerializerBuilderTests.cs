using Kurrent.Client.Schema.Serialization.Protobuf;

namespace Kurrent.Client.Tests.Options;

[Category("Options")]
public class ProtobufSerializerBuilderTests {
    #region ->> Default Values <<-

    [Test]
    public async ValueTask default_builder_produces_options_with_null_context() {
        // Arrange & Act
        var options = new KurrentClientOptions.ProtobufSerializerBuilder().Build();

        // Assert
        await Assert.That(options.Schema.Protobuf.Context).IsNull();
    }

    #endregion

    #region ->> WithContext <<-

    [Test]
    public async ValueTask with_context_sets_proto_serializer_context() {
        // Arrange
        var context = new ProtoSerializerContext();

        // Act
        var options = new KurrentClientOptions.ProtobufSerializerBuilder()
            .WithContext(context)
            .Build();

        // Assert
        await Assert.That(options.Schema.Protobuf.Context).IsEqualTo(context);
    }

    [Test]
    public async ValueTask with_context_replaces_previous_context() {
        // Arrange
        var firstContext  = new ProtoSerializerContext();
        var secondContext = new ProtoSerializerContext();

        // Act
        var options = new KurrentClientOptions.ProtobufSerializerBuilder()
            .WithContext(firstContext)
            .WithContext(secondContext)
            .Build();

        // Assert
        await Assert.That(options.Schema.Protobuf.Context).IsEqualTo(secondContext);
        await Assert.That(options.Schema.Protobuf.Context).IsNotEqualTo(firstContext);
    }

    #endregion

    #region ->> Method Chaining <<-

    [Test]
    public async ValueTask method_chaining_returns_configured_options() {
        // Arrange
        var context = new ProtoSerializerContext();

        // Act
        var options = new KurrentClientOptions.ProtobufSerializerBuilder()
            .WithContext(context)
            .Build();

        // Assert
        await Assert.That(options.Schema.Protobuf.Context).IsNotNull();
        await Assert.That(options.Schema.Protobuf.Context).IsEqualTo(context);
    }

    #endregion
}
