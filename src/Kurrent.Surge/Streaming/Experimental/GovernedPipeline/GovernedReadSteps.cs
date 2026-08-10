using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;
using Kurrent.Variant;

namespace Kurrent.Surge.Experimental;

/// <summary>
/// Typestate carrier after policy resolution. Guards (already decoded, empty payload, Bytes)
/// never enter this chain — the pipeline decides them before step one.
/// </summary>
readonly record struct PolicyResolvedRead(Record Source, ResolvedSchemaRegistryPolicy Policy);

/// <summary>A record whose type resolved: everything decode needs, nothing nullable.</summary>
readonly record struct DecodableRead(Record Source, ResolvedSchemaRegistryPolicy Policy, Type MessageType);

/// <summary>A decision already made — the chain ends here with this outcome.</summary>
readonly record struct TerminalRead(DecodeOutcome Outcome);

/// <summary>
/// The resolution verdict: decodable, or terminal. The union replaces nullable-field
/// pass-through carriers — illegal states are unrepresentable.
/// </summary>
readonly partial record struct TypeResolution : IVariant<DecodableRead, TerminalRead>;

/// <summary>
/// Policy resolution stays a per-record step: it is designed to consult the server's registry
/// settings (<c>SchemaRegistryPolicy.FromServerFeatures</c>, not implemented yet). It runs
/// before type resolution because the unmapped-type decision is policy-gated.
/// </summary>
sealed class ResolvePolicyReadStep(
    SchemaRegistryPolicy policy,
    IReadOnlyDictionary<SchemaDataFormat, SchemaRegistrationOptions> registrations
) : IStep<Record, PolicyResolvedRead> {
    public ValueTask<PolicyResolvedRead> Execute(Record input, CancellationToken ct) {
        var resolved = policy
            .Resolve(registrations[input.Schema.DataFormat], input.Stream, input.Schema.DataFormat)
            .EnsureDataFormatCompliance(input.Schema.DataFormat);

        return new(new PolicyResolvedRead(input, resolved));
    }
}

/// <summary>
/// Pure catalog lookup: schema name → type. Unmapped resolves here, before any codec
/// involvement — a forward-unknown type is data for fallback handlers unless governance
/// demands resolution, in which case it throws with the pipeline on the stack.
/// </summary>
sealed class ResolveTypeReadStep(MessageResolver messages) : IStep<PolicyResolvedRead, TypeResolution> {
    public ValueTask<TypeResolution> Execute(PolicyResolvedRead input, CancellationToken ct) {
        var (record, policy) = input;

        var messageType = messages.ResolveType(record.Schema.Schema);

        if (messageType is null) {
            return policy.ValidateSchemas
                ? throw new MessageTypeResolutionException(record.Schema.Schema)
                : new((TypeResolution)new TerminalRead(DecodeOutcome.UnknownType));
        }

        return new((TypeResolution)new DecodableRead(record, policy, messageType));
    }
}

/// <summary>
/// The one effectful read stage: compatibility governance per policy. The version id is
/// discarded like the production path discards its context write-back — the record's schema
/// info is never mutated by decoding.
/// </summary>
sealed class GovernReadStep(ISchemaGovernance governance) : IStep<DecodableRead, DecodableRead> {
    public async ValueTask<DecodableRead> Execute(DecodableRead input, CancellationToken ct) {
        await governance
            .EnsureCompatible(input.Source.Schema, input.MessageType, input.Policy, ct)
            .ConfigureAwait(false);

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
/// value onto the record like <c>RecordDecoder</c> does.
/// </summary>
sealed class GovernedDecodeStep(SchemaCodecProvider codecs) : IStep<DecodableRead, DecodeOutcome> {
    public ValueTask<DecodeOutcome> Execute(DecodableRead input, CancellationToken ct) {
        var record = input.Source;

        try {
            var value = codecs.GetCodec(record.Schema.DataFormat).Decode(record.Data, input.MessageType);

            record.Value     = value;
            record.ValueType = value.GetType();

            return new(DecodeOutcome.Decoded);
        }
        // Mirrors the serializer base's wrap so the governed chain keeps the same error contract.
        catch (Exception ex) when (ex is not SchemaSerializationException) {
            throw new SchemaDeserializationFailedException(record.Schema.DataFormat, record.Schema.Schema, ex);
        }
    }
}
