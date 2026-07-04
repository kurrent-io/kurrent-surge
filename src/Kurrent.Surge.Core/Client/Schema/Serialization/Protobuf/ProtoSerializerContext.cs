using System.Collections.Concurrent;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Kurrent.Client.Schema.Serialization.Protobuf;

/// <summary>
/// Provides AOT-compatible registration of protobuf message types, replacing
/// reflection-based discovery of <c>Parser</c> and <c>Descriptor</c> static properties.
/// </summary>
/// <remarks>
/// <para>
/// Register protobuf message types at startup to enable deserialization without reflection:
/// </para>
/// <code>
/// var protoContext = new ProtoSerializerContext()
///     .Register(OrderPlaced.Parser, OrderPlaced.Descriptor)
///     .Register(OrderConfirmed.Parser, OrderConfirmed.Descriptor);
/// </code>
/// </remarks>
[PublicAPI]
public class ProtoSerializerContext {
	readonly ConcurrentDictionary<Type, (MessageParser Parser, MessageDescriptor Descriptor)> _types = new();

	/// <summary>
	/// Registers a protobuf message type with its parser and descriptor.
	/// </summary>
	/// <typeparam name="T">The protobuf message type.</typeparam>
	/// <param name="parser">The message parser (typically <c>T.Parser</c>).</param>
	/// <param name="descriptor">The message descriptor (typically <c>T.Descriptor</c>).</param>
	/// <returns>This context instance for method chaining.</returns>
	public ProtoSerializerContext Register<T>(MessageParser<T> parser, MessageDescriptor descriptor) where T : IMessage<T> {
		_types.TryAdd(typeof(T), (parser, descriptor));
		return this;
	}

	/// <summary>
	/// Attempts to retrieve the parser and descriptor for a registered protobuf message type.
	/// </summary>
	internal bool TryGet(Type type, out (MessageParser Parser, MessageDescriptor Descriptor) ctx) =>
		_types.TryGetValue(type, out ctx);
}
