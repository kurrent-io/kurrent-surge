using System.Buffers;
using System.Diagnostics;

namespace Kurrent;

static class SystemTypes {
    static readonly Type MissingType = Type.Missing.GetType();

    extension(Type) {
        public static Type MissingType => MissingType;
    }

    extension(object value) {
        public bool IsBytes() => value.GetType().IsBytes();
    }

    /// <param name="type">
    /// The <see cref="Type"/> to check.
    /// </param>
    extension(Type type) {
        bool IsBytes() =>
            type == typeof(byte[]) ||
            type == typeof(ReadOnlyMemory<byte>) ||
            type == typeof(Memory<byte>) ||
            type == typeof(ArraySegment<byte>) ||
            type == typeof(ReadOnlySequence<byte>);

        /// <summary>
        /// Checks if the specified type is the placeholder type representing a missing type.
        /// </summary>
        /// <returns>
        /// <c>true</c> if the type is the placeholder for a missing type; otherwise, <c>false</c>.
        /// </returns>
        [DebuggerStepThrough]
        public bool IsMissing() => type == MissingType;
        
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
