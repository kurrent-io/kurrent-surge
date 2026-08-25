using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Kurrent.Surge.Codecs.Protobuf;

static class ProtobufExtensions {
    extension(Type messageType) {
        public MessageParser GetProtoMessageParser() =>
            ProtobufMessages.System.GetParser(messageType);

        public MessageDescriptor GetProtoMessageDescriptor() =>
            ProtobufMessages.System.GetDescriptor(messageType);

        public Type EnsureTypeIsProtoMessage() =>
            !typeof(IMessage).IsAssignableFrom(messageType)
                ? throw new InvalidCastException($"Type {messageType.Name} is not a Protocol Buffers message")
                : messageType;

        public bool IsProtoMessage() =>
            typeof(IMessage).IsAssignableFrom(messageType);
    }

    public static IMessage EnsureValueIsProtoMessage(this object? value) =>
	    value as IMessage ?? throw new InvalidOperationException($"Value of type {value!.GetType().Name} is not a Protocol Buffers message");
    
    extension(MessageDescriptor descriptor) {
        public bool TryGetMessageExtensionValue<TValue>(Extension<MessageOptions, TValue> extension, out TValue? value) where TValue : IMessage {
            var options = descriptor.GetOptions();
            
            if (options?.HasExtension(extension) ?? false) {
                value = options.GetExtension(extension);
                return true;
            }

            value = default!;
            return false;
        }

        public TValue GetRequiredMessageExtensionValue<TValue>(Extension<MessageOptions, TValue> extension) where TValue : IMessage =>
            !descriptor.TryGetMessageExtensionValue(extension, out var value) || value is null ? throw new InvalidOperationException($"Type {descriptor.Name} does not have {typeof(TValue)} extension defined.") : value;
    }
}

class ProtobufMessages {
    public static ProtobufMessages System { get; } = new();

    ConcurrentDictionary<Type, (MessageParser Parser, MessageDescriptor Descriptor)> Types { get; } = new();

    ProtobufCodecContext? _context;

    /// <summary>
    /// Configures a <see cref="ProtobufCodecContext"/> for AOT-compatible protobuf type resolution.
    /// Types registered in the context are resolved without reflection.
    /// </summary>
    internal void Configure(ProtobufCodecContext context) => _context = context;

    public MessageParser GetParser(Type messageType) =>
        Types.GetOrAdd(messageType, ResolveContext).Parser;

    public MessageDescriptor GetDescriptor(Type messageType) =>
        Types.GetOrAdd(messageType, ResolveContext).Descriptor;

    public MessageDescriptor GetDescriptor<T>() =>
        GetDescriptor(typeof(T));

    (MessageParser Parser, MessageDescriptor Descriptor) ResolveContext(Type messageType) {
        // check registered context first (AOT-safe path)
        if (_context is not null && _context.TryGet(messageType, out var ctx))
            return ctx;

        // reflection fallback for JIT environments
        if (!RuntimeFeature.IsDynamicCodeSupported)
            throw new InvalidOperationException(
                $"Type '{messageType.Name}' is not registered in ProtoSerializerContext. " +
                "In AOT environments, all protobuf types must be explicitly registered.");

        return ReflectionFallback(messageType);

        static (MessageParser Parser, MessageDescriptor Descriptor) ReflectionFallback(Type messageType) {
            return (GetMessageParser(messageType), GetMessageDescriptor(messageType));

            static MessageParser GetMessageParser(Type messageType) =>
                (MessageParser)messageType
                    .GetProperty("Parser", BindingFlags.Public | BindingFlags.Static)!
                    .GetValue(null)!;

            static MessageDescriptor GetMessageDescriptor(Type messageType) =>
                (MessageDescriptor)messageType
                    .GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static)!
                    .GetValue(null)!;
        }
    }

    static ConcurrentDictionary<Type, MessageOptions?> OptionsCache { get; } = new();

    public static bool TryGetMessageAnnotations<T, TAnnotations>(Extension<MessageOptions, TAnnotations> extension, [MaybeNullWhen(false)] out TAnnotations annotation) where T : IMessage where TAnnotations : IMessage {
        var options = OptionsCache.GetOrAdd(
            typeof(T), static type =>
                System.GetDescriptor(type).GetOptions());

        if (options?.HasExtension(extension) ?? false) {
            annotation = options.GetExtension(extension);
            return true;
        }

        annotation = default!;
        return false;
    }

    public void ClearCache() {
        Types.Clear();
        OptionsCache.Clear();
    }
}
