using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;

namespace Kurrent.Surge.AI.HuggingFace;

/// <summary>
/// Pure hardware detection: reports which CPU SIMD/ISA levels the current host supports, most-capable first,
/// as the arch tags Optimum uses in quantized ONNX filenames (<c>avx512_vnni</c>, <c>avx512</c>, <c>avx2</c>,
/// <c>arm64</c>). No configuration, no overrides, no policy.
/// </summary>
public static class HardwareProbe {
    /// <summary>The host's supported arch tags, best-first.</summary>
    public static IReadOnlyList<string> PreferredArchTags() {
        // ARM64 ships its own quantized build; the x86 SIMD checks below never apply there.
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            return ["arm64"];
        
        // IsSupported are JIT/AOT intrinsics (compile to constants), not reflection — AOT-safe.
        // avx512_vnni = AVX-512 + VNNI (Vector Neural Network Instructions — the fused int8 dot-product that
        // makes quantized matmul fastest). AvxVnniInt8 is a conservative proxy for VNNI: guarded by Avx512F it
        // never over-claims; it may miss some AVX-512-VNNI CPUs, which then safely fall back to plain avx512.
        var tags = new List<string>(3);
        if (Avx512F.IsSupported && AvxVnniInt8.IsSupported) tags.Add("avx512_vnni");
        if (Avx512F.IsSupported)                            tags.Add("avx512");
        if (Avx2.IsSupported)                               tags.Add("avx2");
        return tags;
    }
}
