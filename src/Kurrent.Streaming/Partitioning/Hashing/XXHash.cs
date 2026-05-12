using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Kurrent.Streaming.Partitioning.Hashing;

/// <summary>
/// XXHash32 — fast non-cryptographic hash used by the partitioner.
///
/// Uses <see cref="Vector128{T}"/> of <c>uint</c> SIMD when the runtime has hardware
/// acceleration (all modern x64 and Arm64), with a scalar fallback otherwise.
/// </summary>
internal static class XxHash {
    const uint Prime32V1 = 0x9E3779B1u;
    const uint Prime32V2 = 0x85EBCA77u;
    const uint Prime32V3 = 0xC2B2AE3Du;
    const uint Prime32V4 = 0x27D4EB2Fu;
    const uint Prime32V5 = 0x165667B1u;

    public static uint Hash32(ReadOnlySpan<byte> buffer, uint seed = 0) =>
        Vector128.IsHardwareAccelerated
            ? Hash32Simd(buffer, seed)
            : Hash32Scalar(buffer, seed);

    static uint Hash32Simd(ReadOnlySpan<byte> buffer, uint seed) {
        const int stripeLength = 16;

        var length = buffer.Length;
        ref var ptr = ref MemoryMarshal.GetReference(buffer);

        nint offset = 0;
        uint acc;

        if (length >= stripeLength) {
            var accs = Vector128.Create(
                seed + Prime32V1 + Prime32V2,
                seed + Prime32V2,
                seed,
                seed - Prime32V1);

            var prime1 = Vector128.Create(Prime32V1);
            var prime2 = Vector128.Create(Prime32V2);

            var aligned = (nint)(length & ~(stripeLength - 1));

            while (offset < aligned) {
                var lanes = Unsafe.ReadUnaligned<Vector128<uint>>(ref Unsafe.Add(ref ptr, offset));

                accs += lanes * prime2;
                accs  = RotateLeft(accs, 13) * prime1;

                offset += stripeLength;
            }

            acc = BitOperations.RotateLeft(accs.GetElement(0), 1)
                + BitOperations.RotateLeft(accs.GetElement(1), 7)
                + BitOperations.RotateLeft(accs.GetElement(2), 12)
                + BitOperations.RotateLeft(accs.GetElement(3), 18);
        }
        else {
            acc = seed + Prime32V5;
        }

        acc += (uint)length;

        return FinalizeTail(ref ptr, offset, (nint)length, acc);
    }

    static uint Hash32Scalar(ReadOnlySpan<byte> buffer, uint seed) {
        const int stripeLength = 16;

        var length = buffer.Length;
        ref var ptr = ref MemoryMarshal.GetReference(buffer);

        nint offset = 0;
        uint acc;

        if (length >= stripeLength) {
            var acc1 = seed + Prime32V1 + Prime32V2;
            var acc2 = seed + Prime32V2;
            var acc3 = seed;
            var acc4 = seed - Prime32V1;

            var aligned = (nint)(length & ~(stripeLength - 1));

            while (offset < aligned) {
                acc1 = Round(acc1, Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref ptr, offset)));
                acc2 = Round(acc2, Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref ptr, offset + 4)));
                acc3 = Round(acc3, Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref ptr, offset + 8)));
                acc4 = Round(acc4, Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref ptr, offset + 12)));
                offset += stripeLength;
            }

            acc = BitOperations.RotateLeft(acc1, 1)
                + BitOperations.RotateLeft(acc2, 7)
                + BitOperations.RotateLeft(acc3, 12)
                + BitOperations.RotateLeft(acc4, 18);
        }
        else {
            acc = seed + Prime32V5;
        }

        acc += (uint)length;

        return FinalizeTail(ref ptr, offset, (nint)length, acc);
    }

    static uint FinalizeTail(ref byte ptr, nint offset, nint tailEnd, uint acc) {
        while (offset + 4 <= tailEnd) {
            var lane = Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref ptr, offset));
            acc += lane * Prime32V3;
            acc  = BitOperations.RotateLeft(acc, 17) * Prime32V4;
            offset += 4;
        }

        while (offset < tailEnd) {
            var b = Unsafe.Add(ref ptr, offset);
            acc += b * Prime32V5;
            acc  = BitOperations.RotateLeft(acc, 11) * Prime32V1;
            offset++;
        }

        return Avalanche(acc);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static uint Round(uint acc, uint lane) {
        acc += lane * Prime32V2;
        acc  = BitOperations.RotateLeft(acc, 13);
        return acc * Prime32V1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static Vector128<uint> RotateLeft(Vector128<uint> v, int count) =>
        Vector128.ShiftLeft(v, count) | Vector128.ShiftRightLogical(v, 32 - count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static uint Avalanche(uint acc) {
        acc ^= acc >> 15;
        acc *= Prime32V2;
        acc ^= acc >> 13;
        acc *= Prime32V3;
        acc ^= acc >> 16;
        return acc;
    }
}
