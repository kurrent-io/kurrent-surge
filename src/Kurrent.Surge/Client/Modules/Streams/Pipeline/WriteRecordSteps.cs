using Kurrent.Toolkit;
using System.Buffers;
using Kurrent.Surge.Schema.Governance;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;
using Kurrent.Variant;
using Google.Protobuf;
using Kurrent.Surge.Codecs;
using Kurrent.Surge.Codecs.Avro;
using Kurrent.Surge.Codecs.Protobuf;
using Contracts = KurrentDB.Protocol.V2.Streams;

namespace Kurrent.Surge.Client.Streams;

/// <summary>
/// Typestate carrier after catalog resolution: the source record, its schema name, and the
/// wire format the routing step settled on.
/// </summary>
readonly record struct NamedWrite(AppendRecord Source, SchemaName Schema, SchemaDataFormat Format);

/// <summary>
/// The routing verdict at the chain's head: a catalog record carrying its resolved schema
/// name, or a raw-bytes record already encoded.
/// </summary>
readonly partial record struct WriteResolution : IVariant<NamedWrite, EncodedWrite>;

/// <summary>
/// Typestate carrier between resolve and encode: the source record and its resolved wire schema.
/// </summary>
readonly record struct ResolvedWrite(AppendRecord Source, RecordSchemaInfo Schema);

/// <summary>
/// Typestate carrier between encode and frame: the source record, the schema the serializer
/// resolved, and the serialized payload.
/// </summary>
readonly record struct EncodedWrite(AppendRecord Source, RecordSchemaInfo Schema, ReadOnlyMemory<byte> Payload);

/// <summary>
/// Routes the record. Raw Bytes unwraps straight to an encoded write — no name to resolve,
/// no governance to consult, no codec to run. Catalog records resolve their schema name over
/// the client's frozen snapshot and continue down the chain; an unmapped type throws here,
/// with the pipeline on the stack instead of the serializer.
/// </summary>
sealed class ClassifyWriteStep(MessageResolver messages, bool appendAsJson) : IPipelineStep<AppendRecord, WriteResolution> {
    public ValueTask<WriteResolution> Execute(AppendRecord input, CancellationToken ct) {
        if (input.DataFormat == SchemaDataFormat.Bytes)
            return new((WriteResolution)new EncodedWrite(
                input,
                new RecordSchemaInfo { Schema = SchemaName.From(input.SchemaName!), DataFormat = SchemaDataFormat.Bytes },
                UnwrapBytes(input.Value)));

        var messageType = input.Value!.GetType();

        // Settled here rather than at encode because the format is also a governance lookup key.
        // It rides the typestate rather than the record: an unstated format is the common case,
        // and putting it back on the record would cost a record copy per append.
        var format = input.DataFormat != SchemaDataFormat.Unspecified
            ? input.DataFormat
            : appendAsJson
                ? SchemaDataFormat.Json
                : SchemaDataFormat.FromMessageType(messageType);

        return new((WriteResolution)new NamedWrite(input, messages.ResolveSchemaName(messageType), format));
    }

    static ReadOnlyMemory<byte> UnwrapBytes(object? value) =>
        value switch {
            byte[] bytes               => bytes,
            ReadOnlyMemory<byte> bytes => bytes,
            Memory<byte> bytes         => bytes,
            ReadOnlySequence<byte> seq => seq.IsSingleSegment ? seq.First : seq.ToArray(),
            ArraySegment<byte> seg     => seg.AsMemory(),
            null                       => ReadOnlyMemory<byte>.Empty,
            _                          => throw new InvalidOperationException($"Bytes format requires a binary payload, got '{value.GetType().Name}'.")
        };
}

/// <summary>
/// Lifts the governed segment over the routing union so the whole write pipeline composes as
/// one <c>Then</c> chain: named records continue into the lifted chain, already-encoded
/// bytes pass straight through to the frame.
/// </summary>
static class WriteResolutionSteps {
    public static IPipelineStep<WriteResolution, EncodedWrite> OnNamed(this IPipelineStep<NamedWrite, EncodedWrite> governed) =>
        new OnNamedStep(governed);

    sealed class OnNamedStep(IPipelineStep<NamedWrite, EncodedWrite> governed) : IPipelineStep<WriteResolution, EncodedWrite> {
        public ValueTask<EncodedWrite> Execute(WriteResolution input, CancellationToken ct) =>
            input.Match(
                named => governed.Execute(named, ct),
                encoded => new ValueTask<EncodedWrite>(encoded)
            );
    }
}

/// <summary>
/// THE governance step — the only one. Awaits the interceptor's single-flight registration
/// (free once complete; a failed catalog registration rethrows here, at the first governed
/// record) and reads the version id from the registration results. No registry call exists
/// on this path.
/// </summary>
sealed class GovernWriteStep(ISchemaGovernance governance) : IPipelineStep<NamedWrite, ResolvedWrite> {
    public async ValueTask<ResolvedWrite> Execute(NamedWrite input, CancellationToken ct) {
        var source = input.Source;

        // WaitAsync abandons only this caller's wait — the single-flight registration keeps
        // running on the client lifetime token for every other caller.
        var registered = await governance.CatalogRegistration.WaitAsync(ct).ConfigureAwait(false);

        // The registration is keyed by type and carries the format its definition was registered
        // under, which is not this record's encoding — the settled format has to win, or a
        // Protobuf payload frames itself as the registration's format.
        var schemaInfo = registered.TryGet(source.Value!.GetType(), out var info)
            ? info with { DataFormat = input.Format }
            : new RecordSchemaInfo { Schema = input.Schema, DataFormat = input.Format };

        return new(source, schemaInfo);
    }
}

/// <summary>
/// Pure encode over an already-resolved schema through the format's codec.
/// </summary>
sealed class EncodeWriteStep(SchemaCodecProvider codecs) : IPipelineStep<ResolvedWrite, EncodedWrite> {
    public ValueTask<EncodedWrite> Execute(ResolvedWrite input, CancellationToken ct) {
        var source = input.Source;

        var codec = codecs.GetCodec(input.Schema.DataFormat);

        try {
            return new(new EncodedWrite(source, input.Schema, codec.Encode(source.Value)));
        }
        catch (Exception ex) {
            throw new SchemaCodecEncodingException(input.Schema.DataFormat, source.Value!.GetType(), ex);
        }
    }
}

/// <summary>
/// Frames the wire message. <c>SchemaInfo.Id</c> is set only when the registry supplied a
/// version id — an absent id stays absent on the wire, never minted client-side.
/// </summary>
sealed class FrameWriteStep : IPipelineStep<EncodedWrite, Contracts.AppendRecord> {
    public ValueTask<Contracts.AppendRecord> Execute(EncodedWrite input, CancellationToken ct) {
        var source = input.Source;

        // UnsafeWrap constructs a new ByteString from the given bytes. The bytes are not
        // copied, and must not be modified while the ByteString is in use.
        var contract = new Contracts.AppendRecord {
            // The wire expects the GUID string form — RecordId.ToString() is the ULID face.
            RecordId = source.RecordId.ToGuidString(),
            Data     = UnsafeByteOperations.UnsafeWrap(input.Payload),
            Schema   = new Contracts.SchemaInfo {
                Name   = input.Schema.Schema,
                Format = (Contracts.SchemaFormat)input.Schema.DataFormat
            },
            Stream   = source.Stream
        };

        if (input.Schema.HasSchemaId)
            contract.Schema.Id = input.Schema.VersionId.ToString();

        if (source.Metadata.Count > 0)
            source.Metadata.CopyToProperties(contract.Properties);

        return new(contract);
    }
}
