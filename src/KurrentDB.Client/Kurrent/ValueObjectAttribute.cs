namespace Kurrent;

/// <summary>
/// Generates a value object struct that wraps a primitive value.
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class ValueObjectAttribute<TValue> : Attribute where TValue : notnull;
