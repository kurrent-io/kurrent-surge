using Google.Protobuf.WellKnownTypes;
using Kurrent.Surge.Codecs.Protobuf;

namespace Kurrent.Surge.Tests.Codecs.Protobuf;

[Category("Codecs")]
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
		var result = timestamp.EnsureValueIsProtoMessage();

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
