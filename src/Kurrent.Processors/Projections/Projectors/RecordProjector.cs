// ORIGINAL SURGE IMPLEMENTATION — preserved for reference during refactor to IRecordProcessor.
// Disabled from compilation; re-enable by removing the #if false / #endif wrapping below.
#if false

using Kurrent.Processors;

namespace Kurrent.Surge.Projectors;

public interface IRecordProjector<TClient> : IMessageHandler {
    public ValueTask Project(ProjectionContext<TClient> context);

    ValueTask IMessageHandler.Handle(ProcessRecordContext context) =>
        Project((ProjectionContext<TClient>)context);
}

[PublicAPI]
public abstract class RecordProjector<TClient, TMessage> : IRecordProjector<TClient> {
    public abstract ValueTask Project(TMessage message, ProjectionContext<TClient> context);

    ValueTask IRecordProjector<TClient>.Project(ProjectionContext<TClient> context) =>
        Project((TMessage)context.Record.Value!, context);

    internal class Proxy(Func<TMessage, ProjectionContext<TClient>, ValueTask> handler) : RecordProjector<TClient, TMessage> {
        public override ValueTask Project(TMessage message, ProjectionContext<TClient> context) => handler(message, context);
    }
}

#endif
