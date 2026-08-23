using Kurrent.Surge.Codecs.Protobuf.Experimental;

namespace Kurrent.Surge.Core.Tests.Unit.Codecs.Protobuf.Experimental;

public class ProtobufJsonWriterDepthTests {
    const string NodeTypeName = "transcoder.probe.Node";

    [Test]
    public async ValueTask rejects_a_payload_nested_past_the_depth_limit() {
        // Arrange
        var wire = Nest(ProtobufJsonWriterOptions.DefaultMaxDepth + 5);

        // Act & Assert
        await Assert.That(() => ProbeSchema.Write(wire, NodeTypeName)).Throws<InvalidDataException>();
    }

    [Test]
    public async ValueTask accepts_a_payload_nested_up_to_the_depth_limit() {
        // Arrange
        var wire = Nest(ProtobufJsonWriterOptions.DefaultMaxDepth - 1);

        // Act
        var actual = ProbeSchema.Write(wire, NodeTypeName);

        // Assert
        await Assert.That(actual).StartsWith("{\"child\":");
    }

    [Test]
    public async ValueTask honours_a_lowered_depth_limit() {
        // Arrange
        var wire    = Nest(10);
        var options = new ProtobufJsonWriterOptions { MaxDepth = 3 };

        // Act & Assert
        await Assert.That(() => ProbeSchema.Write(wire, NodeTypeName, options)).Throws<InvalidDataException>();
    }

    /// <remarks>
    /// Built by hand rather than through the generated type: Google's own encoder refuses to write
    /// past its recursion limit, which is the very thing under test here.
    /// </remarks>
    static byte[] Nest(int levels) {
        // The innermost Node carries a label so the payload is not a run of empty messages.
        var payload = new List<byte> { 2 << 3 | 2, 3, (byte)'e', (byte)'n', (byte)'d' };

        for (var level = 0; level < levels; level++) {
            var wrapped = new List<byte> { 1 << 3 | 2 };

            WriteVarint(wrapped, (ulong)payload.Count);

            wrapped.AddRange(payload);

            payload = wrapped;
        }

        return payload.ToArray();
    }

    static void WriteVarint(List<byte> destination, ulong value) {
        while (value >= 0x80) {
            destination.Add((byte)(value | 0x80));
            value >>= 7;
        }

        destination.Add((byte)value);
    }
}
