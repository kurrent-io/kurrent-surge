using System.Collections.Frozen;
using System.Text;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Filters;

[Category("Filters")]
[Category("QueryExpressionNormalizer")]
public class AsciiCaseInsensitiveByteComparerTests {
    static readonly AsciiCaseInsensitiveByteComparer Comparer = new();

    #region Equality

    [Test]
    [Arguments("FROM", "from")]
    [Arguments("from", "FROM")]
    [Arguments("Select", "SELECT")]
    [Arguments("JOIN", "join")]
    [Arguments("hello_world", "HELLO_WORLD")]
    [Arguments("123", "123")]
    [Arguments("_test", "_TEST")]
    public async Task equals_case_insensitive(string a, string b) {
        var bytesA = Encoding.UTF8.GetBytes(a);
        var bytesB = Encoding.UTF8.GetBytes(b);

        await Assert.That(Comparer.Equals(bytesA, bytesB)).IsTrue();
        await Assert.That(Comparer.Equals((ReadOnlySpan<byte>)bytesA, bytesB)).IsTrue();
    }

    [Test]
    [Arguments("FROM", "JOIN")]
    [Arguments("abc", "abd")]
    [Arguments("short", "shorter")]
    public async Task not_equals_different_content(string a, string b) {
        var bytesA = Encoding.UTF8.GetBytes(a);
        var bytesB = Encoding.UTF8.GetBytes(b);

        await Assert.That(Comparer.Equals(bytesA, bytesB)).IsFalse();
        await Assert.That(Comparer.Equals((ReadOnlySpan<byte>)bytesA, bytesB)).IsFalse();
    }

    #endregion

    #region Hash Code Consistency

    [Test]
    [Arguments("FROM", "from")]
    [Arguments("Select", "SELECT")]
    [Arguments("hello_world", "HELLO_WORLD")]
    public async Task hash_codes_match_for_equal_values(string a, string b) {
        var bytesA = Encoding.UTF8.GetBytes(a);
        var bytesB = Encoding.UTF8.GetBytes(b);

        var hashArray = Comparer.GetHashCode(bytesA);
        var hashSpan  = Comparer.GetHashCode((ReadOnlySpan<byte>)bytesB);

        await Assert.That(hashArray).IsEqualTo(hashSpan);
    }

    #endregion

    #region FrozenDictionary Integration

    [Test]
    public async Task frozen_dictionary_alternate_lookup_works() {
        var dict = new Dictionary<byte[], string>(Comparer) {
            [Encoding.UTF8.GetBytes("FROM")]   = "table_introducer",
            [Encoding.UTF8.GetBytes("SELECT")] = "context_reset",
        }.ToFrozenDictionary(Comparer)
         .GetAlternateLookup<ReadOnlySpan<byte>>();

        var found = dict.TryGetValue("from"u8, out var value);

        await Assert.That(found).IsTrue();
        await Assert.That(value).IsEqualTo("table_introducer");
    }

    #endregion

    #region Create

    [Test]
    public async Task create_returns_copy_of_span() {
        ReadOnlySpan<byte> span = "hello"u8;
        var created = Comparer.Create(span);

        await Assert.That(created).IsEquivalentTo(new byte[] { 104, 101, 108, 108, 111 });
    }

    #endregion
}
