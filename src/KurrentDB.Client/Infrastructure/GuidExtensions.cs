using System.Numerics;

namespace Kurrent.Client;

public static class GuidExtensions {
    extension(Guid guid) {
        public BigInteger ToBigInteger() =>
            new(guid.ToByteArray(), isUnsigned: true, isBigEndian: true);
    }
}
