namespace Kurrent.CodeAnalysis;

/// <summary>
/// Combined flags describing modifiers and accessibility of a type. One enum instead of two
/// so filter expressions and header emission can operate on a single bitmask.
/// </summary>
[Flags]
public enum TypeTraits {
    None      = 0,
    Partial   = 1 << 0,
    Abstract  = 1 << 1,
    Sealed    = 1 << 2,
    Static    = 1 << 3,
    ReadOnly  = 1 << 4,
    Public    = 1 << 5,
    Internal  = 1 << 6,
    Protected = 1 << 7,
    Private   = 1 << 8,
    File      = 1 << 9
}
