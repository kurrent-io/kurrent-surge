using Google.Rpc;

namespace Kurrent.Toolkit.GoogleRpc;

/// <summary>
/// Extension methods for converting gRPC error details back into exceptions.
/// </summary>
static class DebugInfoExtensions {
    /// <summary>
    /// Converts a <see cref="DebugInfo"/> error detail into an <see cref="Exception"/>.
    /// The exception message is taken from the <see cref="DebugInfo.Detail"/> property,
    /// and stack trace information from <see cref="DebugInfo.StackEntries"/> is included
    /// in the exception's Data dictionary.
    /// If <paramref name="debugInfo"/> is null or has no detail message, returns null.
    /// </summary>
    /// <param name="debugInfo">The debug information to convert.</param>
    /// <returns>
    /// An <see cref="Exception"/> containing the debug information. If <paramref name="debugInfo"/>
    /// has stack entries, they are stored in the exception's Data dictionary under the "StackEntries" key.
    /// </returns>
    public static Exception? ToException(this DebugInfo? debugInfo) {
        if (debugInfo is null || string.IsNullOrWhiteSpace(debugInfo.Detail))
            return null;

        var exception = new Exception(debugInfo.Detail);

        if (debugInfo.StackEntries.Count > 0) {
            exception.Data["StackEntries"]      = string.Join("\n", debugInfo.StackEntries);
            exception.Data["StackEntriesCount"] = debugInfo.StackEntries.Count;
        }

        // ExceptionDispatchInfo.Capture(exception).Throw();

        return exception;
    }
}
