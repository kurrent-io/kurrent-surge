namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Exception thrown when cluster discovery fails after exhausting all retry attempts.
/// </summary>
/// <remarks>
/// <para>
/// This exception is thrown by <see cref="ClusterResolver"/> when it cannot
/// establish a connection to any cluster node after the maximum number of
/// discovery attempts (configured via <see cref="ResilienceOptions.MaxDiscoveryAttempts"/>).
/// </para>
/// <para>
/// The exception preserves the number of attempts made and the last underlying
/// exception for diagnostic purposes.
/// </para>
/// </remarks>
public sealed class DiscoveryException : Exception {
    /// <summary>
    /// Gets the number of discovery attempts that were made.
    /// </summary>
    public int AttemptCount { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscoveryException"/> class.
    /// </summary>
    /// <param name="attemptCount">The number of discovery attempts made.</param>
    /// <param name="innerException">The last exception that caused discovery to fail.</param>
    public DiscoveryException(int attemptCount, Exception? innerException = null)
        : base($"Failed to discover cluster after {attemptCount} attempts.", innerException) {
        AttemptCount = attemptCount;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscoveryException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="attemptCount">The number of discovery attempts made.</param>
    /// <param name="innerException">The last exception that caused discovery to fail.</param>
    public DiscoveryException(string message, int attemptCount, Exception? innerException = null)
        : base(message, innerException) {
        AttemptCount = attemptCount;
    }
}
