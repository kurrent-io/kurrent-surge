using Contracts = KurrentDB.Protocol.V2.Streams;

namespace Kurrent.Surge.Client.Streams;

static class AppendRecordsMapping {
    extension(ConsistencyCheck source) {
        public Contracts.ConsistencyCheck ToContract() =>
            new() {
                StreamState = source.Value switch {
                    StreamStateCheck check => new() { Stream = check.Stream, ExpectedState = check.ExpectedState.ToWireValue() },
                    _                      => throw new ArgumentException($"Unsupported consistency check type: {source.Value.GetType().Name}")
                }
            };
    }

    extension(Contracts.AppendRecordsResponse source) {
        public AppendRecordsResponse ToResponse() {
            var revisions = source.Revisions
                .Select(x => new StreamPosition(x.Stream, x.Revision))
                .ToArray();

            return new(revisions, source.Position);
        }
    }
}
