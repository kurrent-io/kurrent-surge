namespace Kurrent.Surge.Client.Streams;

public static partial class StreamsClientExtensions {
    extension(IAsyncEnumerable<ReadMessage> source) {
        public async ValueTask<FoldResponse<TState>> FoldAsync<TState>(
            EvolveAsync<TState> evolve, TState initialState, CancellationToken cancellationToken
        ) where TState : new() {
            var state   = initialState;
            var lastPos = RecordPosition.Unset;

            await source
                .AggregateAsync(initialState, async (seed, result, ct) => {
                    switch (result.Value) {
                        case Record record:
                            seed = await evolve(seed, record, ct);
                            lastPos = record.Position;
                        break;

                        case Checkpoint checkpoint: lastPos = checkpoint.Positions[^1]; break;
                    }

                    return seed;

                }, cancellationToken)
                .ConfigureAwait(false);
            
            return new(state, lastPos);
            
            // await foreach (var result in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            //     switch (result.Value) {
            //         case Record record:
            //             state   = await evolve(state, record, context, cancellationToken);
            //             lastPos = record.Position;
            //         break;
            //
            //         case Checkpoint checkpoint:
            //             lastPos = checkpoint.Positions[^1];
            //         break;
            //     }
            // }
            //
            // return new(state, lastPos, context);
        }
       
        public  ValueTask<FoldResponse<TState>> FoldAsync<TState>(
            EvolveAsync<TState> evolve, CancellationToken cancellationToken
        ) where TState : new() => source.FoldAsync(evolve, new(), cancellationToken);
        
        public ValueTask<FoldResponse<TState>> FoldAsync<TState>(
            Evolve<TState> evolve
        ) where TState : new() =>
            source.FoldAsync<TState>((s, r, _) => ValueTask.FromResult(evolve(s, r)), new(), CancellationToken.None);
        
        public ValueTask<FoldResponse<TState>> FoldAsync<TState>(
            Evolve<TState> evolve, TState initialState
        ) where TState : new() =>
            source.FoldAsync((s, r, _) => ValueTask.FromResult(evolve(s, r)), initialState, CancellationToken.None);
    }
}
