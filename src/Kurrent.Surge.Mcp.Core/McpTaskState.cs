namespace Kurrent.Surge.Mcp;

/// <summary>
/// Version-neutral task lifecycle state persisted by <see cref="DuckDBTaskStore"/>.
/// </summary>
/// <remarks>
/// Stored in the database by <b>name</b>, never by ordinal. The ModelContextProtocol task-status
/// enum assigns different ordinals across SDK versions (1.4.1: <c>Failed=3, Cancelled=4</c>;
/// 2.0: <c>Cancelled=3, Failed=4</c>), so both adapters map their package enum to this one by name.
/// Persisting a raw ordinal would let one adapter silently misread rows written by the other.
/// </remarks>
[PublicAPI]
public enum McpTaskState {
    /// <summary>The request is currently being processed.</summary>
    Working,

    /// <summary>The receiver needs input from the requestor before the task can proceed.</summary>
    InputRequired,

    /// <summary>The request completed successfully and results are available.</summary>
    Completed,

    /// <summary>The request did not complete successfully.</summary>
    Failed,

    /// <summary>The request was cancelled before completion.</summary>
    Cancelled
}

[PublicAPI]
public static class McpTaskStateExtensions {
    /// <summary>True for the terminal states that must not be transitioned out of by result stores/cancels.</summary>
    public static bool IsTerminal(this McpTaskState state) =>
        state is McpTaskState.Completed or McpTaskState.Failed or McpTaskState.Cancelled;
}
