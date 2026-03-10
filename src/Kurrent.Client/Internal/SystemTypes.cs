using System.Diagnostics;

namespace Kurrent.Client;

/// <summary>
/// Provides type utilities for checking type characteristics such as byte types,
/// missing type placeholders, and instantiability.
/// </summary>
static class SystemTypes {
    public static readonly Type MissingType = Type.Missing.GetType();

    public static bool IsBytes(this object value) => value.GetType().IsBytes();

    /// <param name="type">
    /// The <see cref="Type"/> to check.
    /// </param>
    extension(Type type) {
        public bool IsBytes() =>
            type == typeof(byte[]) ||
            type == typeof(ReadOnlyMemory<byte>) ||
            type == typeof(Memory<byte>) ||
            type == typeof(ArraySegment<byte>);

        /// <summary>
        /// Checks if the specified type is the placeholder type representing a missing type.
        /// </summary>
        /// <returns>
        /// <c>true</c> if the type is the placeholder for a missing type; otherwise, <c>false</c>.
        /// </returns>
        [DebuggerStepThrough]
        public bool IsMissing() => type == MissingType;

        /// <summary>
        /// Determines whether the specified type is an instantiable class.
        /// </summary>
        /// <returns><c>true</c> if the type is a non-abstract class; otherwise, <c>false</c>.</returns>
        public bool IsInstantiableClass() => type is { IsClass: true, IsAbstract: false };

        /// <summary>
        /// Determines whether the type's full name matches the specified name.
        /// </summary>
        /// <param name="name">The full name to compare against.</param>
        /// <returns><c>true</c> if the type's full name matches the specified name; otherwise, <c>false</c>.</returns>
        public bool MatchesFullName(string name) => type.FullName?.Equals(name, StringComparison.OrdinalIgnoreCase) ?? false;

        /// <summary>
        /// Determines whether the namespace of the specified <see cref="Type"/> matches the given namespace prefix.
        /// </summary>
        /// <param name="namespacePrefix">
        /// The namespace prefix to compare with the namespace of the specified type.
        /// </param>
        /// <returns>
        /// <c>true</c> if the namespace of the specified type matches the namespace prefix; otherwise, <c>false</c>.
        /// </returns>
        public bool MatchesNamespace(string namespacePrefix) =>
            type.Namespace?.Equals(namespacePrefix, StringComparison.OrdinalIgnoreCase) ?? false;
    }
}
