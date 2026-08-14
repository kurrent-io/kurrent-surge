namespace Kurrent.Surge.Client;

/// <summary>
/// A unit of work that runs once, in order, when the client first establishes a connection.
/// What a failure means for the client is decided where the task is registered, not here —
/// see <see cref="KurrentClientInitializationOptions.AddTask(IClientInitializationTask, bool)"/>.
/// </summary>
[PublicAPI]
public interface IClientInitializationTask {
	/// <summary>
	/// The name used in logs and failure reports. Defaults to the implementing type's name, minus a trailing "Task".
	/// </summary>
	string Name {
		get {
			var name = GetType().Name;
			if (name.EndsWith("Task", StringComparison.Ordinal))
				name = name[..^4];
			return name;
		}
	}

	ValueTask Execute(IKurrentClient client, CancellationToken ct);
}

/// <summary>
/// The body of a delegate-based initialization task.
/// </summary>
public delegate ValueTask InitializeClient(IKurrentClient client, CancellationToken ct);

/// <summary>
/// The body of a delegate-based initialization task with caller-provided state, so the
/// delegate can be <see langword="static"/> and capture nothing.
/// </summary>
public delegate ValueTask InitializeClient<in TState>(IKurrentClient client, TState state, CancellationToken ct);
