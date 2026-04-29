namespace Kurrent.CodeAnalysis;

/// <summary>
/// Kind of a type surfaced to generator code. Extends the C# declaration kinds with defensive
/// entries for the cases an incremental generator must tolerate (error types while the user is
/// mid-keystroke, array/pointer/type-parameter references inside other types' graphs).
/// </summary>
public enum TypeKind {
    Class,
    Record,
    Struct,
    RecordStruct,
    Interface,
    Enum,
    Delegate,
    TypeParameter,
    Array,
    Pointer,
    Dynamic,
    Error
}
