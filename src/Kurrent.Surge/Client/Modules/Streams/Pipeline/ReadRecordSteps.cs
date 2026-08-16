using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Schema.Governance;
using Kurrent.Surge.Codecs;
using Kurrent.Surge.Schema.Catalogs;
using Kurrent.Variant;

namespace Kurrent.Surge.Client.Streams;

/// <summary>A record whose type resolved: everything decode needs, nothing nullable.</summary>
readonly record struct DecodableRead(Record Source, Type MessageType);

/// <summary>A decision already made — the chain ends here with this outcome.</summary>
readonly record struct TerminalRead(DecodeOutcome Outcome);

/// <summary>
/// The resolution verdict: decodable, or terminal. The union replaces nullable-field
/// pass-through carriers — illegal states are unrepresentable.
/// </summary>
readonly partial record struct TypeResolution : IVariant<DecodableRead, TerminalRead>;

/// <summary>
/// Pure catalog lookup: schema name → type. Unmapped resolves here, before any codec
/// involvement — a forward-unknown type is data for fallback handlers unless the client's
/// options demand resolution, in which case it throws with the pipeline on the stack.
/// Guards (already decoded, empty payload, Bytes) never enter this chain — the pipeline
/// decides them before step one.
/// </summary>
sealed class ResolveTypeReadStep(
    MessageResolver messages,
    KurrentClientSchemaOptions schema
) : IStep<Record, TypeResolution> {
    public ValueTask<TypeResolution> Execute(Record input, CancellationToken ct) {
        var messageType = messages.ResolveType(input.Schema.Schema);

        if (messageType is null) {
            return schema.ValidationMode is SchemaValidationMode.Enabled
                ? throw new MessageTypeResolutionException(input.Schema.Schema)
                : new(new TerminalRead(DecodeOutcome.UnknownType));
        }

        return new(new DecodableRead(input, messageType));
    }
}

/// <summary>
/// The one effectful read stage: compatibility governance per the client's schema options.
/// Backfills the record's version id when the wire carried none and the registry resolves one.
/// </summary>
sealed class GovernReadStep(ISchemaGovernance governance) : IStep<DecodableRead, DecodableRead> {
    public async ValueTask<DecodableRead> Execute(DecodableRead input, CancellationToken ct) {
        var record = input.Source;

        // V1 reads carry no version id on the wire. Catalog messages got theirs during the
        // startup walk, so the record is backfilled from the walk's map first —
        // policy-free and without a registry round trip.
        if (!record.Schema.HasSchemaId) {
            var registered = await governance.CatalogRegistration.WaitAsync(ct).ConfigureAwait(false);

            if (registered.TryGet(input.MessageType, record.Schema.DataFormat, out var info) && info.HasSchemaId)
                record.Schema = record.Schema with { VersionId = info.VersionId };
        }

        // Compatibility-by-name covers the rest under a validating policy; the resolved id
        // is backfilled the same way so readers learn which registered version matched.
        var backfill = await governance
            .EnsureCompatible(record.Schema, input.MessageType, ct)
            .ConfigureAwait(false);

        if (backfill != SchemaVersionId.Unset)
            record.Schema = record.Schema with { VersionId = backfill };

        return input;
    }
}

/// <summary>
/// Lifts a decodable-record chain over the resolution union so the whole read pipeline
/// composes as one <c>Then</c> chain: decodable resolutions continue into the lifted chain,
/// terminal resolutions exit with their outcome.
/// </summary>
static class TypeResolutionSteps {
    public static IStep<TypeResolution, DecodeOutcome> OnDecodable(this IStep<DecodableRead, DecodeOutcome> decoding) =>
        new OnDecodableStep(decoding);

    sealed class OnDecodableStep(IStep<DecodableRead, DecodeOutcome> decoding) : IStep<TypeResolution, DecodeOutcome> {
        public ValueTask<DecodeOutcome> Execute(TypeResolution input, CancellationToken ct) =>
            input.Match(
                decodable => decoding.Execute(decodable, ct),
                terminal => new ValueTask<DecodeOutcome>(terminal.Outcome)
            );
    }
}

/// <summary>
/// Pure decode over an already-resolved type through the format's codec. Materializes the
/// decoded value onto the record.
/// </summary>
sealed class DecodeReadStep(SchemaCodecProvider codecs) : IStep<DecodableRead, DecodeOutcome> {
    public ValueTask<DecodeOutcome> Execute(DecodableRead input, CancellationToken ct) {
        var record = input.Source;

        var codec = codecs.GetCodec(record.Schema.DataFormat);

        try {
            var value = codec.Decode(record.Data, input.MessageType);

            record.Value     = value;
            record.ValueType = value.GetType();

            return new(DecodeOutcome.Decoded);
        }
        catch (Exception ex) {
            throw new SchemaCodecDecodingException(record.Schema.DataFormat, record.Schema.Schema, ex);
        }
    }
}
