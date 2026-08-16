using Google.Protobuf.WellKnownTypes;
using Kurrent.Surge.Codecs.Protobuf;

namespace Kurrent.Surge.Tests.Schema.Serialization.Protobuf;

[Category("SerDe")]
public class ProtobufCodecContextTests {
	[After(Test)]
	public void cleanup() => ProtobufMessages.System.ClearCache();

	[Test]
	public async ValueTask register_returns_self_for_chaining() {
		// Arrange
		var context = new ProtobufCodecContext();

		// Act
		var result = context.Register(Timestamp.Parser, Timestamp.Descriptor);

		// Assert
		await Assert.That(result).IsSameReferenceAs(context);
	}

	[Test]
	public async ValueTask try_get_returns_true_for_registered_type() {
		// Arrange
		var context = new ProtobufCodecContext();
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
		var context = new ProtobufCodecContext();

		// Act
		var found = context.TryGet(typeof(Timestamp), out _);

		// Assert
		await Assert.That(found).IsFalse();
	}

	[Test]
	public async ValueTask register_multiple_types_stores_all() {
		// Arrange
		var context = new ProtobufCodecContext();

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
