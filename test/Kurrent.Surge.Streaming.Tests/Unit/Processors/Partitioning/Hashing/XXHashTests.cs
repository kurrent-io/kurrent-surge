using System.Text;
using Kurrent.Streaming.Partitioning.Hashing;

namespace Kurrent.Streaming.Tests.Partitioning.Hashing;

[Category("Processors")]
public class XXHashTests {
    public record XXHashVector(string Text, uint Seed, uint Hash32);

    // xxHash32 reference vectors
    public static IEnumerable<XXHashVector> TestVectors() {
        yield return new("",                                            0U,   0x02cc5d05U);
        yield return new("",                                            123U, 0x3930C86EU);
        yield return new("a",                                           0U,   0x550d7456U);
        yield return new("a",                                           123U, 0xA2BCDA53U);
        yield return new("123",                                         0U,   0xb6855437U);
        yield return new("1234",                                        0U,   0x01543429U);
        yield return new("123456789012345",                             0U,   0xda7b17e8U);
        yield return new("1234567890123456123456789012345",             0U,   0xf3556ecfU);
        yield return new("Nobody inspects the spammish repetition",     0U,   0xe2293b2fU);
        yield return new("Nobody inspects the spammish repetition",     123U, 0xfa7f6052U);
        yield return new("The quick brown fox jumps over the lazy dog", 0U,   0xe85ea4deU);
    }

    [Test]
    [MethodDataSource(nameof(TestVectors))]
    public async ValueTask hash32_matches_reference_vectors(XXHashVector vector) {
        var buffer = Encoding.UTF8.GetBytes(vector.Text);
        var result = XxHash.Hash32(buffer, vector.Seed);
        await Assert.That(result).IsEqualTo(vector.Hash32);
    }
}
