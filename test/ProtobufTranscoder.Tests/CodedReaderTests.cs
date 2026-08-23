using ProtobufTranscoder.Wire;

namespace ProtobufTranscoder.Tests;

public class CodedReaderTests {
    [Test]
    public async Task ReadTag_ReturnsCorrectFieldNumberAndWireType() {
        byte[] data        = [0x08, 0x96, 0x01];
        var    reader      = new CodedReader(data);
        var    tag         = reader.ReadTag();
        var    fieldNumber = CodedReader.GetFieldNumber(tag);
        var    wireType    = CodedReader.GetWireType(tag);

        await Assert.That(fieldNumber).IsEqualTo(1);
        await Assert.That(wireType).IsEqualTo(WireType.Varint);
    }

    [Test]
    public async Task ReadInt32_DecodesVarint() {
        byte[] data   = [0x96, 0x01];
        var    reader = new CodedReader(data);
        var    value  = reader.ReadInt32();
        await Assert.That(value).IsEqualTo(150);
    }

    [Test]
    public async Task ReadInt64_DecodesLargeVarint() {
        byte[] data   = [0xAC, 0x02];
        var    reader = new CodedReader(data);
        var    value  = reader.ReadInt64();
        await Assert.That(value).IsEqualTo(300L);
    }

    [Test]
    public async Task ReadSInt32_DecodesZigZagNegative() {
        byte[] data   = [0x01];
        var    reader = new CodedReader(data);
        var    value  = reader.ReadSInt32();
        await Assert.That(value).IsEqualTo(-1);
    }

    [Test]
    public async Task ReadSInt32_DecodesZigZagPositive() {
        byte[] data   = [0x02];
        var    reader = new CodedReader(data);
        var    value  = reader.ReadSInt32();
        await Assert.That(value).IsEqualTo(1);
    }

    [Test]
    public async Task ReadBool_True() {
        byte[] data   = [0x01];
        var    reader = new CodedReader(data);
        var    value  = reader.ReadBool();
        await Assert.That(value).IsTrue();
    }

    [Test]
    public async Task ReadBool_False() {
        byte[] data   = [0x00];
        var    reader = new CodedReader(data);
        var    value  = reader.ReadBool();
        await Assert.That(value).IsFalse();
    }

    [Test]
    public async Task ReadString_DecodesUtf8() {
        byte[] data   = [0x07, 0x74, 0x65, 0x73, 0x74, 0x69, 0x6E, 0x67];
        var    reader = new CodedReader(data);
        var    value  = reader.ReadString();
        await Assert.That(value).IsEqualTo("testing");
    }

    [Test]
    public async Task ReadFixed32_DecodesLittleEndian() {
        byte[] data   = [0x01, 0x00, 0x00, 0x00];
        var    reader = new CodedReader(data);
        var    value  = reader.ReadFixed32();
        await Assert.That(value).IsEqualTo(1u);
    }

    [Test]
    public async Task ReadFixed64_DecodesLittleEndian() {
        byte[] data   = [0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        var    reader = new CodedReader(data);
        var    value  = reader.ReadFixed64();
        await Assert.That(value).IsEqualTo(1ul);
    }

    [Test]
    public async Task ReadFloat_DecodesValue() {
        var data   = BitConverter.GetBytes(1.0f);
        var reader = new CodedReader(data);
        var value  = reader.ReadFloat();
        await Assert.That(value).IsEqualTo(1.0f);
    }

    [Test]
    public async Task ReadDouble_DecodesValue() {
        var data   = BitConverter.GetBytes(3.14);
        var reader = new CodedReader(data);
        var value  = reader.ReadDouble();
        await Assert.That(value).IsEqualTo(3.14);
    }

    [Test]
    public async Task ReadMessage_ReturnsSubReader() {
        byte[] data      = [0x02, 0x08, 0x01];
        var    reader    = new CodedReader(data);
        var    sub       = reader.ReadMessage();
        var    mainAtEnd = reader.IsAtEnd;

        var tag         = sub.ReadTag();
        var fieldNumber = CodedReader.GetFieldNumber(tag);
        var value       = sub.ReadInt32();
        var subAtEnd    = sub.IsAtEnd;

        await Assert.That(mainAtEnd).IsTrue();
        await Assert.That(fieldNumber).IsEqualTo(1);
        await Assert.That(value).IsEqualTo(1);
        await Assert.That(subAtEnd).IsTrue();
    }

    [Test]
    public async Task SkipField_SkipsVarint() {
        byte[] data   = [0x08, 0x96, 0x01, 0x10, 0x01];
        var    reader = new CodedReader(data);
        var    tag1   = reader.ReadTag();
        reader.SkipField(tag1);

        var tag2        = reader.ReadTag();
        var fieldNumber = CodedReader.GetFieldNumber(tag2);
        var value       = reader.ReadInt32();

        await Assert.That(fieldNumber).IsEqualTo(2);
        await Assert.That(value).IsEqualTo(1);
    }

    [Test]
    public async Task TruncatedVarint_ThrowsDecodeException() {
        byte[]           data   = [0x80];
        DecodeException? caught = null;

        try {
            var reader = new CodedReader(data);
            reader.ReadInt32();
        }
        catch (DecodeException ex) {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
    }

    [Test]
    public async Task IsAtEnd_TrueForEmptyBuffer() {
        var reader = new CodedReader(ReadOnlySpan<byte>.Empty);
        var atEnd  = reader.IsAtEnd;
        await Assert.That(atEnd).IsTrue();
    }

    [Test]
    public async Task ReadMultipleFields_Sequentially() {
        byte[] data   = [0x08, 0x96, 0x01, 0x12, 0x05, 0x68, 0x65, 0x6C, 0x6C, 0x6F];
        var    reader = new CodedReader(data);

        var tag1 = reader.ReadTag();
        var fn1  = CodedReader.GetFieldNumber(tag1);
        var val1 = reader.ReadInt32();

        var tag2  = reader.ReadTag();
        var fn2   = CodedReader.GetFieldNumber(tag2);
        var val2  = reader.ReadString();
        var atEnd = reader.IsAtEnd;

        await Assert.That(fn1).IsEqualTo(1);
        await Assert.That(val1).IsEqualTo(150);
        await Assert.That(fn2).IsEqualTo(2);
        await Assert.That(val2).IsEqualTo("hello");
        await Assert.That(atEnd).IsTrue();
    }
}