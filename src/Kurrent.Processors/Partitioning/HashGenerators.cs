// ReSharper disable InconsistentNaming

using Kurrent.Processors.Partitioning.Hashing;
using static System.Text.Encoding;

namespace Kurrent.Client.Processors.Partitioning;

public delegate uint GenerateHash(string input);

public delegate uint GenerateHashFromBytes(ReadOnlySpan<byte> input);

/// <summary>
/// Provides a collection of hash generation utilities for both string and byte array inputs.
/// </summary>
[PublicAPI]
public static class HashGenerators {
    [PublicAPI]
    public static class FromBytes {
        public static GenerateHashFromBytes XXHash =>
            static input => XxHash.Hash32(input);
    }

    [PublicAPI]
    public static class FromString {
        public static GenerateHash XXHash =>
            static input => FromBytes.XXHash(UTF8.GetBytes(input));
    }
}
