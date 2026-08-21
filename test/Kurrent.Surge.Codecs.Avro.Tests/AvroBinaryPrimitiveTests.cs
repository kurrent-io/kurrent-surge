using Avro.IO;
using Kurrent.Surge.Codecs.Avro.Binary;

namespace Kurrent.Surge.Codecs.Avro.Tests;

/// <summary>
/// Asserts that our binary primitives and Apache.Avro's produce and consume the same bytes.
/// </summary>
/// <remarks>
/// This is what makes owning the encoding safe. The format is small and frozen, but "we implemented the
/// specification correctly" is a claim, and Apache is the independent implementation that can check it.
/// Both directions are covered: bytes we write must be readable by Apache, and bytes Apache writes must
/// be readable by us. One direction alone would miss a symmetric mistake.
/// </remarks>
[Category("Codecs")]
[Timeout(10_000)]
public class AvroBinaryPrimitiveTests {
    static readonly Faker Faker = new();

    static byte[] Ours(Action<AvroBinaryEncoder> write) {
        var encoder = new AvroBinaryEncoder();

        write(encoder);

        return encoder.WrittenSpan.ToArray();
    }

    static byte[] Apache(Action<BinaryEncoder> write) {
        using var stream = new MemoryStream();

        write(new BinaryEncoder(stream));

        return stream.ToArray();
    }

    static T ReadOurs<T>(byte[] wire, Func<AvroBinaryDecoder, T> read) => read(new AvroBinaryDecoder(wire));

    static T ReadApache<T>(byte[] wire, Func<BinaryDecoder, T> read) {
        using var stream = new MemoryStream(wire, writable: false);

        return read(new BinaryDecoder(stream));
    }

    #region ->> Test Case Generators <<-

    /// <summary>
    /// The values where a zig-zag varint changes shape: sign handling, and every seven-bit boundary
    /// where the continuation bit turns on.
    /// </summary>
    public sealed class VarintCases : TestCaseDataGenerator<long> {
        protected override IEnumerable<TestCase<long>> Data() {
            yield return new(0L, DisplayName: "zero");
            yield return new(-1L, DisplayName: "minus one — every bit set after zig-zag");
            yield return new(1L, DisplayName: "one");
            yield return new(63L, DisplayName: "last one-byte positive");
            yield return new(64L, DisplayName: "first two-byte positive");
            yield return new(-64L, DisplayName: "last one-byte negative");
            yield return new(-65L, DisplayName: "first two-byte negative");
            yield return new(8191L, DisplayName: "last two-byte");
            yield return new(8192L, DisplayName: "first three-byte");
            yield return new(int.MaxValue, DisplayName: "int.MaxValue");
            yield return new(int.MinValue, DisplayName: "int.MinValue");
            yield return new(long.MaxValue, DisplayName: "long.MaxValue — ten bytes");
            yield return new(long.MinValue, DisplayName: "long.MinValue — ten bytes");
        }
    }

    #endregion // Test Case Generators

    #region ->> Varints <<-

    [Test]
    [VarintCases]
    public async ValueTask writes_a_long_identically_to_apache(long value) {
        // Arrange & Act
        var ours   = Ours(e => e.WriteLong(value));
        var apache = Apache(e => e.WriteLong(value));

        // Assert
        await Assert.That(ours).IsDeepEquivalentTo(apache);
    }

    [Test]
    [VarintCases]
    public async ValueTask reads_a_long_apache_wrote(long value) {
        // Arrange
        var wire = Apache(e => e.WriteLong(value));

        // Act
        var actual = ReadOurs(wire, d => d.ReadLong());

        // Assert
        await Assert.That(actual).IsEqualTo(value);
    }

    [Test]
    [VarintCases]
    public async ValueTask apache_reads_a_long_we_wrote(long value) {
        // Arrange
        var wire = Ours(e => e.WriteLong(value));

        // Act
        var actual = ReadApache(wire, d => d.ReadLong());

        // Assert
        await Assert.That(actual).IsEqualTo(value);
    }

    #endregion // Varints

    #region ->> Primitives <<-

    [Test]
    public async ValueTask writes_primitives_identically_to_apache() {
        // Arrange — one record's worth of every leaf type, in one stream, so a misplaced byte in any of
        // them shifts everything after it and the comparison fails loudly.
        var text     = Faker.Lorem.Sentence();
        var bytes    = Faker.Random.Bytes(37);
        var single   = Faker.Random.Float();
        var @double  = Faker.Random.Double();
        var fixed12  = Faker.Random.Bytes(12);

        // Act
        var ours = Ours(e => {
            e.WriteNull();
            e.WriteBoolean(true);
            e.WriteBoolean(false);
            e.WriteInt(int.MinValue);
            e.WriteFloat(single);
            e.WriteDouble(@double);
            e.WriteString(text);
            e.WriteBytes(bytes);
            e.WriteFixed(fixed12);
            e.WriteEnum(3);
            e.WriteUnionIndex(1);
        });

        var apache = Apache(e => {
            e.WriteNull();
            e.WriteBoolean(true);
            e.WriteBoolean(false);
            e.WriteInt(int.MinValue);
            e.WriteFloat(single);
            e.WriteDouble(@double);
            e.WriteString(text);
            e.WriteBytes(bytes);
            e.WriteFixed(fixed12);
            e.WriteEnum(3);
            e.WriteUnionIndex(1);
        });

        // Assert
        await Assert.That(ours).IsDeepEquivalentTo(apache);
    }

    [Test]
    public async ValueTask reads_primitives_apache_wrote() {
        // Arrange
        var text    = Faker.Lorem.Sentence();
        var bytes   = Faker.Random.Bytes(37);
        var single  = Faker.Random.Float();
        var @double = Faker.Random.Double();

        var wire = Apache(e => {
            e.WriteBoolean(true);
            e.WriteInt(-77);
            e.WriteFloat(single);
            e.WriteDouble(@double);
            e.WriteString(text);
            e.WriteBytes(bytes);
        });

        // Act & Assert
        var decoder = new AvroBinaryDecoder(wire);

        await Assert.That(decoder.ReadBoolean()).IsTrue();
        await Assert.That(decoder.ReadInt()).IsEqualTo(-77);
        await Assert.That(decoder.ReadFloat()).IsEqualTo(single);
        await Assert.That(decoder.ReadDouble()).IsEqualTo(@double);
        await Assert.That(decoder.ReadString()).IsEqualTo(text);
        await Assert.That(decoder.ReadBytes()).IsDeepEquivalentTo(bytes);
    }

    [Test]
    public async ValueTask writes_a_multi_byte_string_identically_to_apache() {
        // Arrange — the length prefix counts UTF-8 bytes, not characters. A prefix counting characters
        // would agree with Apache on ASCII and diverge on everything else.
        const string Text = "Ripley — 日本語 — 🚀";

        // Act
        var ours   = Ours(e => e.WriteString(Text));
        var apache = Apache(e => e.WriteString(Text));

        // Assert
        await Assert.That(ours).IsDeepEquivalentTo(apache);
        await Assert.That(ReadOurs(ours, d => d.ReadString())).IsEqualTo(Text);
    }

    [Test]
    public async ValueTask writes_a_string_longer_than_the_stack_buffer_identically_to_apache() {
        // Arrange — past the stack threshold the encoder rents instead, which is a different code path.
        var text = Faker.Random.String2(4_000);

        // Act
        var ours = Ours(e => e.WriteString(text));

        // Assert
        await Assert.That(ours).IsDeepEquivalentTo(Apache(e => e.WriteString(text)));
        await Assert.That(ReadOurs(ours, d => d.ReadString())).IsEqualTo(text);
    }

    #endregion // Primitives

    #region ->> Blocked collections <<-

    [Test]
    public async ValueTask writes_a_blocked_array_identically_to_apache() {
        // Arrange
        long[] items = [1, 2, 3];

        // Act
        var ours = Ours(e => {
            e.WriteArrayStart();
            e.SetItemCount(items.Length);

            foreach (var item in items) {
                e.StartItem();
                e.WriteLong(item);
            }

            e.WriteArrayEnd();
        });

        var apache = Apache(e => {
            e.WriteArrayStart();
            e.SetItemCount(items.Length);

            foreach (var item in items) {
                e.StartItem();
                e.WriteLong(item);
            }

            e.WriteArrayEnd();
        });

        // Assert
        await Assert.That(ours).IsDeepEquivalentTo(apache);
    }

    [Test]
    public async ValueTask writes_an_empty_array_as_a_single_terminator() {
        // Arrange — SetItemCount writes nothing for zero. Emitting a zero count as well would encode a
        // block that a reader takes as the end of the collection, and the terminator after it would be
        // read as the start of the next field.
        var ours = Ours(e => {
            e.WriteArrayStart();
            e.SetItemCount(0);
            e.WriteArrayEnd();
        });

        // Act & Assert
        await Assert.That(ours).IsDeepEquivalentTo(Apache(e => {
            e.WriteArrayStart();
            e.SetItemCount(0);
            e.WriteArrayEnd();
        }));

        await Assert.That(ours.Length).IsEqualTo(1);
        await Assert.That(ReadOurs(ours, d => d.ReadArrayStart())).IsEqualTo(0L);
    }

    [Test]
    public async ValueTask reads_a_block_whose_count_is_negative() {
        // Arrange — a writer may emit a negative count followed by the block's byte size, so a reader
        // that does not want the block can skip it. The count is the absolute value. Apache's own
        // encoder never writes this shape, so the bytes are built by hand.
        var wire = Ours(e => {
            e.WriteLong(-2);  // two items, byte size follows
            e.WriteLong(2);   // the block occupies two bytes
            e.WriteLong(1);
            e.WriteLong(2);
            e.WriteLong(0);   // terminator
        });

        // Act
        var decoder = new AvroBinaryDecoder(wire);
        var count   = decoder.ReadArrayStart();

        // Assert — the byte-size long must have been consumed, or these read as garbage.
        await Assert.That(count).IsEqualTo(2L);
        await Assert.That(decoder.ReadLong()).IsEqualTo(1L);
        await Assert.That(decoder.ReadLong()).IsEqualTo(2L);
        await Assert.That(decoder.ReadArrayNext()).IsEqualTo(0L);
    }

    #endregion // Blocked collections

    #region ->> Failure modes <<-

    [Test]
    public async ValueTask rejects_a_byte_that_is_not_a_boolean() {
        // Arrange — 0 and 1 are the only encodings; anything else means the stream is misaligned.
        byte[] wire = [7];

        // Act & Assert
        await Assert.That(() => ReadOurs(wire, d => d.ReadBoolean())).Throws<InvalidDataException>();
    }

    [Test]
    public async ValueTask rejects_a_stream_that_ends_mid_value() {
        // Arrange — a length prefix promising more bytes than the stream holds.
        var wire = Ours(e => e.WriteLong(64))[..1];

        // Act & Assert
        await Assert.That(() => ReadOurs(wire, d => d.ReadLong())).Throws<EndOfStreamException>();
    }

    #endregion // Failure modes
}
