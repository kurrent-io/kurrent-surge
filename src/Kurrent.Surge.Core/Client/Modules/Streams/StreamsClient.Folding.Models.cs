namespace Kurrent.Client.Streams;

public delegate TState Evolve<TState>(
    TState state,
    Record record);

public delegate ValueTask<TState> EvolveAsync<TState>(
    TState state,
    Record record,
    CancellationToken ct);

public record FoldResponse<TState>(
    TState State,
    RecordPosition Position);
