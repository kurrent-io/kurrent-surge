namespace Kurrent.Surge;

/// <summary>
/// Marks a <c>readonly partial record struct</c> as a unique identifier. A source generator
/// emits the full identifier surface: minting, dual-face parsing (the 26-character sortable
/// form or any GUID format), span and UTF-8 formatting, byte and Guid conversions, and
/// time-ordered comparison. The type stays a flat 16-byte struct with zero-allocation
/// operations.
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class UniqueIdentifierAttribute : Attribute;
