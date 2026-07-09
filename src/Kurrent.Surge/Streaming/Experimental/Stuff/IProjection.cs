// namespace Kurrent.Surge.Streaming.Core.Stuff;
//
// public record ProjectionContext<TClient>(Func<TClient> GetClient) : ProcessRecordContext {
//     public ProjectionContext(ProcessRecordContext source, Func<TClient> getClient) : this(getClient) {
//         Record            = source.Record;
//         Progress          = source.Progress;
//         RequestTrack      = source.RequestTrack;
//         ProcessorId       = source.ProcessorId;
//         DroneId           = source.DroneId;
//         SessionId         = source.SessionId;
//         Logger            = source.Logger;
//         CancellationToken = source.CancellationToken;
//     }
//
//     public object Message => Record.Value!;
// }
//
// // client visible
// public interface IProjection<TClient> {
//     /// <summary>
//     /// Returns <c>true</c> if a handler is registered for the record's decoded message type.
//     /// </summary>
//     public bool CanProject(Record record) =>
//         record.IsDecoded && Handlers.ContainsKey(record.ValueType);
//
//     
//     ValueTask Project(ProjectionContext<TClient> context);
// }
//
// class Projection<TClient>(TClient Client, IProjection<TClient> Projection) : IRecordBatchProcessor {
//     
//     
//     public ValueTask ProcessBatch(BatchContext context) =>
//         throw new NotImplementedException();
// }
