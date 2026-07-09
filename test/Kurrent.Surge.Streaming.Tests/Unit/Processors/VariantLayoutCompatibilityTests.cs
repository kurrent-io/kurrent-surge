using System.Runtime.CompilerServices;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming.Gateway;

namespace Kurrent.Surge.Streaming.Tests;

/// <summary>
/// Guards the zero-allocation bridge between the client's <see cref="SubscribeMessage"/> and the data
/// source's <see cref="DataSourceMessage"/>. Both are emitted by the VariantGenerator for the same
/// <c>&lt;Connected, Record, Checkpoint&gt;</c> shape, so they share an identical layout
/// (<c>object _value; byte _index</c>) — the ONLY reason <see cref="Unsafe.BitCast{TFrom,TTo}"/> can
/// reinterpret one as the other with no copy and no re-boxing (proven below: the underlying object is
/// reused for both the reference and the boxed-struct cases). If the generator ever changes the emitted
/// shape — field order, tag width, extra fields — BitCast would silently produce garbage; these tests
/// fail loud instead. Remove them only if the BitCast bridge is removed.
/// </summary>
public class VariantLayoutCompatibilityTests {
    [Test]
    public async ValueTask the_two_variants_have_the_same_size(CancellationToken ct) {
        // Arrange
        var expected = Unsafe.SizeOf<SubscribeMessage>();

        // Act
        var actual = Unsafe.SizeOf<DataSourceMessage>();

        // Assert — equal size is the BitCast precondition; a mismatch means the reinterpret is invalid.
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask bitcast_reinterprets_record_case_reusing_the_reference(CancellationToken ct) {
        // Arrange — Record is a reference type; _value holds the reference directly.
        var              record = Record.None;
        SubscribeMessage sub    = record;

        // Act
        var ds = Unsafe.BitCast<SubscribeMessage, DataSourceMessage>(sub);

        // Assert — same tag, same underlying object (no re-allocation).
        await Assert.That(ds.Index).IsEqualTo(sub.Index);
        await Assert.That(ds.IsRecord).IsTrue();
        await Assert.That(ReferenceEquals(ds.Value, sub.Value)).IsTrue();
        await Assert.That(ReferenceEquals(ds.AsRecord, record)).IsTrue();
    }

    [Test]
    public async ValueTask bitcast_reinterprets_connected_case_reusing_the_same_box(CancellationToken ct) {
        // Arrange — Connected is a struct, boxed into _value once at construction.
        SubscribeMessage sub = default(Connected);

        // Act
        var ds = Unsafe.BitCast<SubscribeMessage, DataSourceMessage>(sub);

        // Assert — the ORIGINAL box is reused verbatim, not re-boxed (the payoff over Match-based mapping).
        await Assert.That(ds.Index).IsEqualTo(sub.Index);
        await Assert.That(ds.IsConnected).IsTrue();
        await Assert.That(ReferenceEquals(ds.Value, sub.Value)).IsTrue();
        await Assert.That(ds.AsConnected).IsEqualTo(sub.AsConnected);
    }
}
