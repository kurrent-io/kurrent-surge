#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.

using System.Diagnostics;
using Kurrent.Client.Schema.Serialization;
using Contracts = KurrentDB.Protocol.V2.Streams;

namespace Kurrent.Client.Streams;

static partial class StreamsClientV2Mapper {
	public static async Task<Record> Map(this Contracts.Record source, ISchemaSerializerProvider serializerProvider, bool skipDecoding,  CancellationToken ct) {
		var metadata = source.Properties.MapToMetadata();

		// create a decoder
		IRecordDecoder decoder = new RecordDecoder(serializerProvider);

		// and we are done
		var record = new Record(decoder) {
			Id             = Guid.Parse(source.RecordId),
			Stream         = source.Stream,
			StreamRevision = source.StreamRevision,
			Position       = source.Position,
			Timestamp      = source.Timestamp.ToDateTime(),
			Metadata       = metadata,
			Data           = source.Data.Memory // .ToByteArray() ? safer?
		};

		// now decode the record if required
		if (!skipDecoding)
			await record
				.TryDecode(ct)
				.ConfigureAwait(false);

		return record;
	}

	public static Contracts.ReadDirection Map(this ReadDirection source) =>
		source switch {
			ReadDirection.Forwards  => Contracts.ReadDirection.Forwards,
			ReadDirection.Backwards => Contracts.ReadDirection.Backwards,
			_                       => throw new UnreachableException($"Unexpected read direction: {source}")
		};

	public static Contracts.ReadFilter Map(this ReadFilter source) =>
		new() {
			Scope      = source.Scope.Map(),
			Expression = source.Expression
		};

	public static Contracts.ReadFilterScope Map(this ReadFilterScope source) =>
		source switch {
			ReadFilterScope.Unspecified => Contracts.ReadFilterScope.Unspecified,
			ReadFilterScope.Stream      => Contracts.ReadFilterScope.Stream,
			ReadFilterScope.Schema      => Contracts.ReadFilterScope.Schema,
			ReadFilterScope.Properties  => Contracts.ReadFilterScope.Properties,
			ReadFilterScope.Record      => Contracts.ReadFilterScope.Record,
			_                           => throw new UnreachableException($"Unexpected read filter scope: {source}")
		};

	public static Heartbeat Map(this Contracts.Heartbeat source) {
		return new Heartbeat(
			source.Type switch {
				Contracts.HeartbeatType.Checkpoint => HeartbeatType.Checkpoint,
				Contracts.HeartbeatType.CaughtUp   => HeartbeatType.CaughtUp,
				Contracts.HeartbeatType.FellBehind => HeartbeatType.FellBehind,
				_                                  => throw new UnreachableException($"Unexpected heartbeat type: {source.Type}")
			},
			source.Position,
			StreamRevision.Unset,
			source.Timestamp.ToDateTimeOffset()
		);
	}

	public static Contracts.HeartbeatOptions Map(this HeartbeatOptions source) =>
		new() {
			Enable           = source.Enable,
			RecordsThreshold = source.RecordsThreshold
		};
}
