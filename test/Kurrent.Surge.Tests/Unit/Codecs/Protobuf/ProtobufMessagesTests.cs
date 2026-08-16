using Google.Protobuf.WellKnownTypes;
using Kurrent.Surge.Codecs.Protobuf;

namespace Kurrent.Surge.Tests.Schema.Serialization.Protobuf;

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
		var context = new ProtobufCodecContext()
			.Register(Timestamp.Parser, Timestamp.Descriptor);

		ProtobufMessages.System.Configure(context);

		// Act
		var parser = ProtobufMessages.System.GetParser(typeof(Timestamp));

		// Assert
		await Assert.That(parser).IsSameReferenceAs(Timestamp.Parser);
	}

	[Test]
	public async ValueTask clear_cache_removes_cached_types() {
		// Arrange
		var parserBefore = ProtobufMessages.System.GetParser(typeof(Timestamp));
		await Assert.That(parserBefore).IsNotNull();

		// Act
		ProtobufMessages.System.ClearCache();

		// Assert
		var parserAfter = ProtobufMessages.System.GetParser(typeof(Timestamp));
		await Assert.That(parserAfter).IsNotNull();
		await Assert.That(parserAfter).IsSameReferenceAs(Timestamp.Parser);
	}
}
