using System.Runtime.InteropServices;

namespace Kurrent.Surge;

/// <summary>
/// Marker result for operations that succeed without producing a value.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 1)]
public readonly struct Success;
