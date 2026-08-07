using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Client.Streams.Checkpoints;
using KurrentDB.Client;

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
            NullSchemaSerializerProvider.Instance,
            NullMetadataDecoder.Instance,
            SchemaRegistryPolicy.NoRequirements,
            new RecordDecoder(NullSchemaSerializerProvider.Instance),
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
            NullSchemaSerializerProvider.Instance,
            NullMetadataDecoder.Instance,
            SchemaRegistryPolicy.NoRequirements,
            new RecordDecoder(NullSchemaSerializerProvider.Instance),
            CancellationToken.None
        );
        var record = await readEvent.MapToRecordAsync(SequenceId.From(1), ctx, skipDecoding: true);

        // Assert
        await Assert.That(record.Id.ToGuid()).IsEqualTo(Uuid.FromDto(evt.Id).ToGuid());
        await Assert.That(record.Stream).IsEqualTo(StreamName.From((string)evt.StreamIdentifier!));
        await Assert.That(record.Revision).IsEqualTo(Revision.From((long)evt.StreamRevision));
        await Assert.That(record.IsLink).IsFalse();
        await Assert.That(record.Link.IsUnset).IsFalse();
        await Assert.That(record.Link.Stream).IsEqualTo(StreamName.From((string)link.StreamIdentifier!));
        await Assert.That(record.Link.Revision).IsEqualTo(Revision.From((long)link.StreamRevision));
    }

    [Test]
    public async ValueTask maps_broken_link_when_resolved_target_is_unavailable() {
        // Arrange
        var readEvent = FakeReadEvent.New.WithBrokenLink().Generate();
        var link      = readEvent.Link;

        // Act
        var ctx = new MappingContext(
            NullSchemaSerializerProvider.Instance,
            NullMetadataDecoder.Instance,
            SchemaRegistryPolicy.NoRequirements,
            new RecordDecoder(NullSchemaSerializerProvider.Instance),
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
            NullSchemaSerializerProvider.Instance,
            NullMetadataDecoder.Instance,
            SchemaRegistryPolicy.NoRequirements,
            new RecordDecoder(NullSchemaSerializerProvider.Instance),
            CancellationToken.None
        );
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await readEvent.MapToRecordAsync(SequenceId.From(1), ctx, skipDecoding: true)
        );
    }

    sealed class NullMetadataDecoder : IMetadataDecoder {
        public static readonly NullMetadataDecoder Instance = new();

        public ValueTask<Metadata> Decode(ReadOnlyMemory<byte> bytes, MetadataDecoderContext context) =>
            new(new Metadata());
    }

    sealed class NullSchemaSerializerProvider : ISchemaSerializerProvider {
        public static readonly NullSchemaSerializerProvider Instance = new();

        public bool SupportsDataFormat(SchemaDataFormat dataFormat) => false;

        public ISchemaSerializer GetSerializer(SchemaDataFormat dataFormat) =>
            throw new NotSupportedException();
    }
}
