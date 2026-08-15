namespace Kurrent.Surge.Client;

/// <summary>
/// Tasks that run once, in order, when the client first establishes a connection.
/// </summary>
[PublicAPI]
public sealed class KurrentClientInitializationOptions {
	public List<InitializationTaskRegistration> Tasks { get; } = [];

	/// <summary>
	/// Adds an initialization task. Tasks run in the order they were added.
	/// When <paramref name="throwOnFailure"/> is <see langword="true"/>, a failure faults
	/// the client: later tasks never run and every subsequent operation throws the failure.
	/// Otherwise the failure is logged as a warning and the client stays fully usable.
	/// </summary>
	public KurrentClientInitializationOptions AddTask(IClientInitializationTask task, bool throwOnFailure = false) {
		Tasks.Add(new(task, throwOnFailure));
		return this;
	}

	/// <summary>
	/// Adds an initialization task from a delegate. Tasks run in the order they were added.
	/// When <paramref name="throwOnFailure"/> is <see langword="true"/>, a failure faults
	/// the client: later tasks never run and every subsequent operation throws the failure.
	/// Otherwise the failure is logged as a warning and the client stays fully usable.
	/// </summary>
	public KurrentClientInitializationOptions AddTask(
		string name, InitializeClient execute, bool throwOnFailure = false
	) =>
        AddTask(new DelegateInitializationTask(name, execute), throwOnFailure);

	/// <summary>
	/// Adds an initialization task from a delegate with caller-provided state, so the
	/// delegate can be <see langword="static"/> and capture nothing.
	/// Tasks run in the order they were added.
	/// When <paramref name="throwOnFailure"/> is <see langword="true"/>, a failure faults
	/// the client: later tasks never run and every subsequent operation throws the failure.
	/// Otherwise the failure is logged as a warning and the client stays fully usable.
	/// </summary>
	public KurrentClientInitializationOptions AddTask<TState>(
		string name, TState state, InitializeClient<TState> execute, bool throwOnFailure = false
	) =>
        AddTask(new DelegateInitializationTask<TState>(name, state, execute), throwOnFailure);
}

/// <summary>
/// An initialization task and the failure behavior chosen at registration.
/// Equality is by task name: two registrations with the same task name are the same
/// registration, whatever their failure behavior.
/// </summary>
[PublicAPI]
public sealed record InitializationTaskRegistration(IClientInitializationTask Task, bool ThrowOnFailure) {
	public bool Equals(InitializationTaskRegistration? other) =>
		other is { } && string.Equals(Task.Name, other.Task.Name, StringComparison.Ordinal);

	public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Task.Name);
}
