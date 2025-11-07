// using System.Diagnostics.CodeAnalysis;
// using Kurrent.Client;
// using Kurrent.Validation;
//
// namespace Kurrent;
//
// /// <summary>
// /// Base record for creating strongly-typed value objects that wrap primitive values with domain-specific validation and behavior.
// /// </summary>
// /// <typeparam name="TValue">The underlying primitive type being wrapped (string, int, etc.).</typeparam>
// /// <typeparam name="TThis">The concrete value object type inheriting from this base record.</typeparam>
// /// <remarks>
// /// <para>
// /// Value objects are immutable domain primitives that encapsulate validation logic and provide type safety
// /// for business concepts. They prevent primitive obsession by creating meaningful types for domain values
// /// like CustomerID, EmailAddress, or MoneyAmount.
// /// </para>
// /// <para>
// /// <b>Usage:</b> Create a derived record that inherits from ValueObject and optionally override Validate()
// /// for custom validation logic. Use From() to create instances or TryFrom() for safe creation.
// /// </para>
// /// </remarks>
// public abstract record ValueObject<TValue, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TThis>
//     where TThis : ValueObject<TValue, TThis>, new()
//     where TValue : notnull {
//
//     static readonly Func<TThis> Factory = static () => new();
//
//     int?      _cachedHashCode;
//     object[]? _cachedComponents;
//
//     /// <summary>The wrapped primitive value.</summary>
//     /// <remarks>
//     /// <para><b>Immutability:</b> Value is set during construction and cannot be changed afterward.</para>
//     /// <para>Records provide natural immutability and value-based equality for the wrapped primitive.</para>
//     /// </remarks>
//     public TValue Value { get; init; } = default!;
//
//     /// <summary>
//     /// Creates a value object instance from the provided primitive value with validation.
//     /// </summary>
//     /// <param name="value">The primitive value to wrap in the value object.</param>
//     /// <returns>A new instance of the value object containing the validated value.</returns>
//     /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is null.</exception>
//     /// <exception cref="Exception">Thrown when validation fails in the derived class Validate() method.</exception>
//     /// <remarks>
//     /// <para>
//     /// This is the primary factory method for creating value object instances. It ensures validation
//     /// is performed and throws exceptions for invalid values, making it suitable for scenarios where
//     /// validation failure should halt execution.
//     /// </para>
//     /// </remarks>
//     public static TThis From(TValue value) {
//         ArgumentNullException.ThrowIfNull(value);
//         return (Factory() with { Value = value }).EnsureValid();
//     }
//
//     /// <summary>
//     /// Validates the wrapped value according to domain rules and returns a Result containing success or detailed error information.
//     /// </summary>
//     /// <returns>ValidationSuccess if valid; ValidationError with detailed failure information if invalid.</returns>
//     /// <remarks>
//     /// <para>
//     /// Override this method in derived classes to implement custom validation logic using the Result pattern.
//     /// Return ValidationSuccess.Instance for valid values, or ValidationError.Create() for invalid values.
//     /// This provides rich error information without throwing exceptions.
//     /// </para>
//     /// <para>
//     /// Use ValidationFailure.CreateForProperty() for property-specific validation errors or
//     /// ValidationFailure.Create() for general validation errors.
//     /// </para>
//     /// <para>
//     /// Example: <c>return ValidationError.Create("INVALID_PERSON", ValidationFailure.CreateForProperty("Age", "Age must be positive"));</c>
//     /// </para>
//     /// </remarks>
//     public virtual Result<Success, ValidationError> Validate() =>
//         Results.Success;
//
//     /// <summary>
//     /// Ensures the wrapped value is valid according to domain rules and throws exceptions for invalid values.
//     /// </summary>
//     /// <returns>Returns this instance for fluent method chaining.</returns>
//     /// <remarks>
//     /// <para>
//     /// This method internally calls Validate() and throws the validation error as an exception if validation fails.
//     /// For non-throwing validation, use the Validate() method directly.
//     /// </para>
//     /// <para>
//     /// The method returns this instance to enable fluent method chaining patterns.
//     /// Example: <c>var person = PersonValue.From(data).EnsureValid().ToString();</c>
//     /// </para>
//     /// </remarks>
//     public TThis EnsureValid() {
//         Validate().Unwrap();
//         return (TThis)this;
//     }
//
//     /// <summary>
//     /// Gets the display string representation of the wrapped value.
//     /// </summary>
//     /// <returns>The display string for the wrapped value.</returns>
//     /// <remarks>
//     /// <para>
//     /// Override this method in derived classes to provide custom string formatting.
//     /// The default implementation returns the wrapped value's ToString() representation.
//     /// </para>
//     /// </remarks>
//     protected virtual string GetDisplayString() => Value.ToString() ?? "";
//
//     /// <summary>
//     /// Returns the string representation of the wrapped value.
//     /// </summary>
//     /// <returns>The string representation of the wrapped primitive value.</returns>
//     /// <remarks>
//     /// <para>
//     /// The ToString() method is sealed to prevent records from overriding it with their default
//     /// implementation. Override GetDisplayString() instead for custom string formatting.
//     /// </para>
//     /// </remarks>
//     public sealed override string ToString() => GetDisplayString();
//
//     /// <summary>
//     /// Gets the components used for equality comparison in the case that the Value is a complex type.
//     /// </summary>
//     /// <returns>The components to use for equality comparison.</returns>
//     protected virtual IEnumerable<object> EqualityComponents() {
//         yield return Value.GetHashCode();
//     }
//
//     ///<inheritdoc />
//     public virtual bool Equals(ValueObject<TValue, TThis>? other) {
//         if (other is null) return false;
//         if (ReferenceEquals(this, other)) return true;
//         if (other.GetType() != GetType()) return false;
//         if (GetHashCode() != other.GetHashCode()) return false;
//
//         // Slow verification (if hashes match, values MIGHT match)
//         return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
//     }
//
//     /// <summary>
//     /// Gets the components used for equality comparison while caching them to avoid recomputing them multiple times.
//     /// </summary>
//     object[] GetEqualityComponents() {
//         return _cachedComponents ??= EqualityComponents().ToArray()
//             // just a safe fallback to ensure we always have at least one component, the Value itself
//             .With(components => components.Length == 0 ? [Value] : components);
//     }
//
//     ///<inheritdoc />
//     public override int GetHashCode() {
//         // ReSharper disable once NonReadonlyMemberInGetHashCode
//         return _cachedHashCode ??= GenerateHashCode(GetEqualityComponents());
//
//         static int GenerateHashCode(object[] components) {
//             return components.Length switch {
//                 1 => components[0].GetHashCode(),
//                 2 => HashCode.Combine(components[0], components[1]),
//                 3 => HashCode.Combine(components[0], components[1], components[2]),
//                 4 => HashCode.Combine(components[0], components[1], components[2], components[3]),
//                 5 => HashCode.Combine(components[0], components[1], components[2], components[3], components[4]),
//                 6 => HashCode.Combine(components[0], components[1], components[2], components[3], components[4], components[5]),
//                 7 => HashCode.Combine(components[0], components[1], components[2], components[3], components[4], components[5], components[6]),
//                 8 => HashCode.Combine(components[0], components[1], components[2], components[3], components[4], components[5], components[6], components[7]),
//                 _ => components.Aggregate(0, (hash, component) => HashCode.Combine(hash, component.GetHashCode()))
//             };
//         }
//     }
// }
