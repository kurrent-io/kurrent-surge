using System.Runtime.InteropServices;

namespace Kurrent;

public static class Results {
    public static Success Success => default;
    public static Created Created => default;
    public static Deleted Deleted => default;
    public static Updated Updated => default;
}

[StructLayout(LayoutKind.Sequential, Size = 1)]
public readonly struct Success;

[StructLayout(LayoutKind.Sequential, Size = 1)]
public readonly struct Created;

[StructLayout(LayoutKind.Sequential, Size = 1)]
public readonly struct Deleted;

[StructLayout(LayoutKind.Sequential, Size = 1)]
public readonly struct Updated;
