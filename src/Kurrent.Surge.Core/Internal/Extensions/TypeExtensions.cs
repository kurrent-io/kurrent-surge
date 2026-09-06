using System.Buffers;
using System.Diagnostics;

namespace Kurrent.Toolkit;

static class TypeExtensions {
    static readonly Type MissingType = Type.Missing.GetType();

    extension(Type type) {
        public bool IsBytes() =>
            type == typeof(byte[])               ||
            type == typeof(ReadOnlyMemory<byte>) ||
            type == typeof(Memory<byte>)         ||
            type == typeof(ArraySegment<byte>)   ||
            type == typeof(ReadOnlySequence<byte>);

        /// <summary>
        /// Checks if the specified type is the placeholder type representing a missing type.
        /// </summary>
        [DebuggerStepThrough]
        public bool IsMissing() => type == MissingType;
       
        public static Type MissingType => MissingType;
        
        public string GetFriendlyTypeName() {
            if (!type.IsGenericType)
                return type.FullName!;

            var genericTypeName = type.GetGenericTypeDefinition().FullName;
            genericTypeName = genericTypeName?[..genericTypeName.IndexOf('`')];

            var genericArgs = string.Join(",", type.GetGenericArguments().Select(GetFriendlyTypeName));

            return $"{genericTypeName}<{genericArgs}>";
        }
    }
}
