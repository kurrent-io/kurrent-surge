using System.Numerics;

namespace Kurrent.Client;

static class GuidExtensions {
    extension(Guid source) {
        public BigInteger ToBigInteger() =>
            new(source.ToByteArray(), isUnsigned: true, isBigEndian: true);
    }
}
