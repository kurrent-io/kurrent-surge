using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Client.Streams.Checkpoints;
using Kurrent.Surge.Client.Legacy;

namespace Kurrent.Surge.Tests.Modules.Streams;

[Category("Streams")]
public class MapToRecordAsyncTests {
    [Test]
    public async ValueTask maps_normal_event() {
        // Arrange
        var readEvent = FakeReadEvent.New.Generate();
        var evt       = readEvent.Event;

        // Act
        var ctx = new MappingContext(
            NullMetadataDecoder.Instance,
            NullableRecordDecoder.Instance,
            CancellationToken.None
        );
        var record = await readEvent.MapToRecordAsync(SequenceId.From(1), ctx, skipDecoding: true);

        // Assert
        await Assert.That(record.Id.ToGuid()).IsEqualTo(Uuid.FromDto(evt.Id).ToGuid());
        await Assert.That(record.Stream).IsEqualTo(StreamName.From((string)evt.StreamIdentifier!));
        await Assert.That(record.Revision).IsEqualTo(Revision.From((long)evt.StreamRevision));
        await Assert.That(record.Position.LogPosition).IsEqualTo(LogPosition.From((long)evt.CommitPosition));
        await Assert.That(record.IsLink).IsFalse();
        await Assert.That(record.Link.IsUnset).IsTrue();
        await Assert.That(record.Data.Length).IsGreaterThan(0);
    }

    [Test]
    public async ValueTask maps_resolved_link_event() {
        // Arrange
        var readEvent = FakeReadEvent.New.WithResolvedLink().Generate();
        var evt       = readEvent.Event;
        var link      = readEvent.Link;

        // Act
        var ctx = new MappingContext(
            NullMetadataDecoder.Instance,
            NullableRecordDecoder.Instance,
            CancellationToken.None
        );
        var record = await readEvent.MapToRecordAsync(SequenceId.From(1), ctx, skipDecoding: true);

        // Assert
        await Assert.That(record.Id.ToGuid()).IsEqualTo(Uuid.FromDto(evt.Id).ToGuid());
        await Assert.That(record.Stream).IsEqualTo(StreamName.From((string)evt.StreamIdentifier!));
        await Assert.That(record.Revision).IsEqualTo(Revision.From((long)evt.StreamRevision));
        await Assert.That(record.IsLink).IsFalse();
        await Assert.That(record.Link.IsUnset).IsFalse();
        await Assert.That(record.Link.Position.Stream).IsEqualTo(StreamName.From((string)link.StreamIdentifier!));
        await Assert.That(record.Link.Position.Revision).IsEqualTo(Revision.From((long)link.StreamRevision));
    }

    [Test]
    public async ValueTask maps_broken_link_when_resolved_target_is_unavailable() {
        // Arrange
        var readEvent = FakeReadEvent.New.WithBrokenLink().Generate();
        var link      = readEvent.Link;

        // Act
        var ctx = new MappingContext(
            NullMetadataDecoder.Instance,
            NullableRecordDecoder.Instance,
            CancellationToken.None
        );
        var record = await readEvent.MapToRecordAsync(SequenceId.From(1), ctx, skipDecoding: true);

        // Assert
        await Assert.That(record.Id.ToGuid()).IsEqualTo(Uuid.FromDto(link.Id).ToGuid());
        await Assert.That(record.Stream).IsEqualTo(StreamName.From((string)link.StreamIdentifier!));
        await Assert.That(record.Revision).IsEqualTo(Revision.From((long)link.StreamRevision));
        await Assert.That(record.Position.LogPosition).IsEqualTo(LogPosition.From((long)link.CommitPosition));
        await Assert.That(record.IsLink).IsTrue();
        await Assert.That(record.Link.IsUnset).IsTrue();
    }

    [Test]
    public void throws_when_neither_event_nor_link() {
        // Arrange
        var readEvent = FakeReadEvent.New.WithoutLinkOrEvent().Generate();
        
        // Act & Assert
        var ctx = new MappingContext(
            NullMetadataDecoder.Instance,
            NullableRecordDecoder.Instance,
            CancellationToken.None
        );
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await readEvent.MapToRecordAsync(SequenceId.From(1), ctx, skipDecoding: true)
        );
    }

    [Test]
    public async ValueTask resolved_link_checkpoint_position_is_the_link_position() {
        // Arrange
        var readEvent = FakeReadEvent.New.WithResolvedLink().Generate();
        var link      = readEvent.Link;

        var ctx = new MappingContext(
            NullMetadataDecoder.Instance,
            NullableRecordDecoder.Instance,
            CancellationToken.None
        );
        var record = await readEvent.MapToRecordAsync(SequenceId.From(1), ctx, skipDecoding: true);

        var expected = RecordPosition.From(
            LogPosition.From((long)link.CommitPosition),
            StreamName.From((string)link.StreamIdentifier!),
            Revision.From((long)link.StreamRevision),
            record.Position.Partition);

        // Act
        var checkpointPosition = record.CheckpointPosition;

        // Assert
        await Assert.That(checkpointPosition).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask event_checkpoint_position_is_its_own_position() {
        // Arrange
        var readEvent = FakeReadEvent.New.Generate();

        var ctx = new MappingContext(
            NullMetadataDecoder.Instance,
            NullableRecordDecoder.Instance,
            CancellationToken.None
        );
        var record   = await readEvent.MapToRecordAsync(SequenceId.From(1), ctx, skipDecoding: true);
        var expected = record.Position;

        // Act
        var checkpointPosition = record.CheckpointPosition;

        // Assert
        await Assert.That(checkpointPosition).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask broken_link_checkpoint_position_is_its_own_position() {
        // Arrange
        var readEvent = FakeReadEvent.New.WithBrokenLink().Generate();

        var ctx = new MappingContext(
            NullMetadataDecoder.Instance,
            NullableRecordDecoder.Instance,
            CancellationToken.None
        );
        var record   = await readEvent.MapToRecordAsync(SequenceId.From(1), ctx, skipDecoding: true);
        var expected = record.Position;

        // Act
        var checkpointPosition = record.CheckpointPosition;

        // Assert
        await Assert.That(checkpointPosition).IsEqualTo(expected);
    }

    sealed class NullMetadataDecoder : IMetadataDecoder {
        public static readonly NullMetadataDecoder Instance = new();

        public ValueTask<Metadata> Decode(ReadOnlyMemory<byte> bytes, MetadataDecoderContext context) =>
            new(new Metadata());
    }
}
