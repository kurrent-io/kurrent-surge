using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Client.Schema.Serialization.Protobuf;

namespace Kurrent.Client.Tests.Schema.Serialization.Protobuf;

public class ProtoSerializerContextTests {
	[After(Test)]
	public void cleanup() => ProtobufMessages.System.ClearCache();

	[Test]
	public async ValueTask register_returns_self_for_chaining() {
		// Arrange
		var context = new ProtoSerializerContext();

		// Act
		var result = context.Register(Timestamp.Parser, Timestamp.Descriptor);

		// Assert
		await Assert.That(result).IsSameReferenceAs(context);
	}

	[Test]
	public async ValueTask try_get_returns_true_for_registered_type() {
		// Arrange
		var context = new ProtoSerializerContext();
		context.Register(Timestamp.Parser, Timestamp.Descriptor);

		// Act
		var found = context.TryGet(typeof(Timestamp), out var ctx);

		// Assert
		await Assert.That(found).IsTrue();
		await Assert.That(ctx.Parser).IsSameReferenceAs(Timestamp.Parser);
		await Assert.That(ctx.Descriptor).IsSameReferenceAs(Timestamp.Descriptor);
	}

	[Test]
	public async ValueTask try_get_returns_false_for_unregistered_type() {
		// Arrange
		var context = new ProtoSerializerContext();

		// Act
		var found = context.TryGet(typeof(Timestamp), out _);

		// Assert
		await Assert.That(found).IsFalse();
	}

	[Test]
	public async ValueTask register_multiple_types_stores_all() {
		// Arrange
		var context = new ProtoSerializerContext();

		// Act
		context
			.Register(Timestamp.Parser, Timestamp.Descriptor)
			.Register(Empty.Parser, Empty.Descriptor);

		// Assert
		var foundTimestamp = context.TryGet(typeof(Timestamp), out var tsCtx);
		var foundEmpty    = context.TryGet(typeof(Empty), out var emptyCtx);

		await Assert.That(foundTimestamp).IsTrue();
		await Assert.That(tsCtx.Parser).IsSameReferenceAs(Timestamp.Parser);

		await Assert.That(foundEmpty).IsTrue();
		await Assert.That(emptyCtx.Parser).IsSameReferenceAs(Empty.Parser);
	}
}

public class ProtobufExtensionsTests {
	[After(Test)]
	public void cleanup() => ProtobufMessages.System.ClearCache();

	[Test]
	public async ValueTask is_proto_message_returns_true_for_imessage_type() {
		// Act
		var result = typeof(Timestamp).IsProtoMessage();

		// Assert
		await Assert.That(result).IsTrue();
	}

	[Test]
	public async ValueTask is_proto_message_returns_false_for_non_imessage_type() {
		// Act
		var result = typeof(string).IsProtoMessage();

		// Assert
		await Assert.That(result).IsFalse();
	}

	[Test]
	public async ValueTask ensure_type_is_proto_message_returns_type_for_valid() {
		// Act
		var result = typeof(Timestamp).EnsureTypeIsProtoMessage();

		// Assert
		await Assert.That(result).IsEqualTo(typeof(Timestamp));
	}

	[Test]
	public async ValueTask ensure_type_is_proto_message_throws_for_invalid() {
		// Act & Assert
		await Assert.That(() => typeof(string).EnsureTypeIsProtoMessage())
			.ThrowsExactly<InvalidCastException>();
	}

	[Test]
	public async ValueTask ensure_value_is_proto_message_returns_imessage_for_valid() {
		// Arrange
		var timestamp = Timestamp.FromDateTime(DateTime.UtcNow);

		// Act
		var result = ((object)timestamp).EnsureValueIsProtoMessage();

		// Assert
		await Assert.That(result).IsSameReferenceAs(timestamp);
	}

	[Test]
	public async ValueTask ensure_value_is_proto_message_throws_for_non_imessage() {
		// Arrange
		object value = "hello";

		// Act & Assert
		await Assert.That(() => value.EnsureValueIsProtoMessage())
			.ThrowsExactly<InvalidOperationException>();
	}
}

public class ProtobufMessagesTests {
	[After(Test)]
	public void cleanup() => ProtobufMessages.System.ClearCache();

	[Test]
	public async ValueTask get_parser_returns_parser_via_reflection_fallback() {
		// Act
		var parser = ProtobufMessages.System.GetParser(typeof(Timestamp));

		// Assert
		await Assert.That(parser).IsNotNull();
		await Assert.That(parser).IsSameReferenceAs(Timestamp.Parser);
	}

	[Test]
	public async ValueTask get_descriptor_returns_descriptor_via_reflection_fallback() {
		// Act
		var descriptor = ProtobufMessages.System.GetDescriptor(typeof(Timestamp));

		// Assert
		await Assert.That(descriptor).IsNotNull();
		await Assert.That(descriptor).IsSameReferenceAs(Timestamp.Descriptor);
	}

	[Test]
	public async ValueTask get_parser_uses_context_when_configured() {
		// Arrange
		var context = new ProtoSerializerContext()
			.Register(Timestamp.Parser, Timestamp.Descriptor);

		ProtobufMessages.System.Configure(context);

		// Act
		var parser = ProtobufMessages.System.GetParser(typeof(Timestamp));

		// Assert
		await Assert.That(parser).IsSameReferenceAs(Timestamp.Parser);
	}

	[Test]
	public async ValueTask clear_cache_removes_cached_types() {
		// Arrange - populate cache via reflection fallback
		var parserBefore = ProtobufMessages.System.GetParser(typeof(Timestamp));
		await Assert.That(parserBefore).IsNotNull();

		// Act
		ProtobufMessages.System.ClearCache();

		// Assert - resolves again without error after cache clear
		var parserAfter = ProtobufMessages.System.GetParser(typeof(Timestamp));
		await Assert.That(parserAfter).IsNotNull();
		await Assert.That(parserAfter).IsSameReferenceAs(Timestamp.Parser);
	}
}
