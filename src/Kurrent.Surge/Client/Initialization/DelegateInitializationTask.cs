namespace Kurrent.Surge.Client;

sealed class DelegateInitializationTask(
	string name,
	InitializeClient execute
) : IClientInitializationTask {
	public string Name => name;

	public ValueTask Execute(IKurrentClient client, CancellationToken ct) => execute(client, ct);
}

sealed class DelegateInitializationTask<TState>(
	string name,
	TState state,
	InitializeClient<TState> execute
) : IClientInitializationTask {
	public string Name => name;

	public ValueTask Execute(IKurrentClient client, CancellationToken ct) => execute(client, state, ct);
}
