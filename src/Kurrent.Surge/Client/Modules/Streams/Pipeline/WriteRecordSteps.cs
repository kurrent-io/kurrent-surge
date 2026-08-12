using System.Buffers;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Client.Schema.Governance;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;
using Kurrent.Variant;
using Google.Protobuf;
using Contracts = KurrentDB.Protocol.V2.Streams;

namespace Kurrent.Surge.Client.Streams;

/// <summary>
/// Typestate carrier after catalog resolution: the source record and its schema name.
/// </summary>
readonly record struct NamedWrite(AppendRecord Source, SchemaName Schema);

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
sealed class ClassifyWriteStep(MessageResolver messages) : IStep<AppendRecord, WriteResolution> {
    public ValueTask<WriteResolution> Execute(AppendRecord input, CancellationToken ct) =>
        new(input.DataFormat == SchemaDataFormat.Bytes
            ? (WriteResolution)new EncodedWrite(input, new RecordSchemaInfo { DataFormat = SchemaDataFormat.Bytes }, UnwrapBytes(input.Value))
            : (WriteResolution)new NamedWrite(input, messages.ResolveSchemaName(input.Value.GetType())));

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
    public static IStep<WriteResolution, EncodedWrite> OnNamed(this IStep<NamedWrite, EncodedWrite> governed) =>
        new OnNamedStep(governed);

    sealed class OnNamedStep(IStep<NamedWrite, EncodedWrite> governed) : IStep<WriteResolution, EncodedWrite> {
        public ValueTask<EncodedWrite> Execute(WriteResolution input, CancellationToken ct) =>
            input.Match(
                named => governed.Execute(named, ct),
                encoded => new ValueTask<EncodedWrite>(encoded)
            );
    }
}

/// <summary>
/// THE governance step — the only one. Awaits the interceptor's single-flight validation
/// (free once complete; a failed catalog validation rethrows here, at the first governed
/// record), resolves the policy (folded in: it stays per-record because it will consult the
/// server's settings when <c>SchemaRegistryPolicy.FromServerFeatures</c> goes live), and reads
/// the version id from the validation results. No registry call exists on this path.
/// </summary>
sealed class GovernWriteStep(
    CatalogValidationInterceptor validation,
    SchemaRegistryPolicy policy,
    IReadOnlyDictionary<SchemaDataFormat, SchemaRegistrationOptions> registrations
) : IStep<NamedWrite, ResolvedWrite> {
    public async ValueTask<ResolvedWrite> Execute(NamedWrite input, CancellationToken ct) {
        var source = input.Source;

        // WaitAsync abandons only this caller's wait — the single-flight validation keeps
        // running on the client lifetime token for every other caller.
        var validated = await validation.Validation.WaitAsync(ct).ConfigureAwait(false);

        policy
            .Resolve(registrations[source.DataFormat], source.Stream, source.DataFormat)
            .EnsureDataFormatCompliance(source.DataFormat);

        var schema = validated.TryGetValue((source.Value.GetType(), source.DataFormat), out var info)
            ? info
            : new RecordSchemaInfo { Schema = input.Schema, DataFormat = source.DataFormat };

        return new(source, schema);
    }
}

/// <summary>
/// Pure encode over an already-resolved schema through the format's codec.
/// </summary>
sealed class EncodeWriteStep(SchemaCodecProvider codecs) : IStep<ResolvedWrite, EncodedWrite> {
    public ValueTask<EncodedWrite> Execute(ResolvedWrite input, CancellationToken ct) {
        var source = input.Source;

        try {
            var payload = source.Value is null
                ? ReadOnlyMemory<byte>.Empty
                : codecs.GetCodec(source.DataFormat).Encode(source.Value);

            return new(new EncodedWrite(source, input.Schema, payload));
        }
        // Mirrors the serializer base's wrap so the governed chain keeps the same error contract.
        catch (Exception ex) when (ex is not SchemaSerializationException) {
            throw new SchemaSerializationFailedException(source.DataFormat, source.Value!.GetType(), ex);
        }
    }
}

/// <summary>
/// Frames the wire message. <c>SchemaInfo.Id</c> is set only when the registry supplied a
/// version id — an absent id stays absent on the wire, never minted client-side.
/// </summary>
sealed class FrameWriteStep : IStep<EncodedWrite, Contracts.AppendRecord> {
    public ValueTask<Contracts.AppendRecord> Execute(EncodedWrite input, CancellationToken ct) {
        var source = input.Source;

        // UnsafeWrap constructs a new ByteString from the given bytes. The bytes are not
        // copied, and must not be modified while the ByteString is in use.
        var contract = new Contracts.AppendRecord {
            // The wire expects the GUID string form — RecordId.ToString() is the ULID face.
            RecordId = source.RecordId.ToGuidString(),
            Data     = UnsafeByteOperations.UnsafeWrap(input.Payload),
            Schema   = new Contracts.SchemaInfo {
                Name   = source.SchemaName ?? input.Schema.Schema,
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
