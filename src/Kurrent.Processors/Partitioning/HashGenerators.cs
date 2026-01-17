// ReSharper disable InconsistentNaming

using static System.Text.Encoding;

namespace Kurrent.Client.Processors.Partitioning;

public delegate uint GenerateHash(string input);

public delegate uint GenerateHashFromBytes(ReadOnlySpan<byte> input);

/// <summary>
/// Provides a collection of hash generation utilities for both string and byte array inputs.
/// </summary>
[PublicAPI]
public static class HashGenerators {
    const uint DefaultSeed = 0;

    [PublicAPI]
    public static class FromBytes {
        public static GenerateHashFromBytes MurmurHash3 =>
            static input => HashDepot.MurmurHash3.Hash32(input, DefaultSeed);

        public static GenerateHashFromBytes XXHash =>
            static input => HashDepot.XXHash.Hash32(input);
    }

    [PublicAPI]
    public static class FromString {
        public static GenerateHash MurmurHash3 =>
            static input => FromBytes.MurmurHash3(UTF8.GetBytes(input));

        public static GenerateHash XXHash =>
            static input => FromBytes.XXHash(UTF8.GetBytes(input));
    }
}
