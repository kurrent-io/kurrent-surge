using Grpc.Core;
using Grpc.Net.Client.Configuration;

namespace KurrentDB.Client;

/// <summary>
/// Defines retry configuration for gRPC operations.
/// </summary>
record KurrentDBClientRetrySettings {
	/// <summary>
	/// Gets or sets whether retry is enabled.
	/// </summary>
	public bool IsEnabled { get; init; } = true;

	/// <summary>
	/// Gets or sets the maximum number of retry attempts.
	/// </summary>
	public int MaxAttempts { get; init; } = 3;

	/// <summary>
	/// Gets or sets the initial backoff delay.
	/// </summary>
	public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromSeconds(250);

	/// <summary>
	/// Gets or sets the maximum backoff delay.
	/// </summary>
	public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(10);

	/// <summary>
	/// Gets or sets the backoff multiplier. Each successive backoff increases by this multiplier.
	/// </summary>
	public double BackoffMultiplier { get; init; } = 2.0;

	/// <summary>
	/// Default retry settings with the default values.
	/// </summary>
	public static KurrentDBClientRetrySettings Default => new();

	/// <summary>
	/// Retry settings with retry disabled.
	/// </summary>
	public static KurrentDBClientRetrySettings NoRetry => new() { IsEnabled = false };

	// The retryable codes are fixed, not a knob: the declarative channel retry policy is data
	// evaluated inside Grpc.Net and cannot consult the contract's ShouldRetry predicate — these
	// are the classic transient codes the policy always retried.
	static readonly StatusCode[] RetryableStatusCodes = [
		StatusCode.Unavailable,       // Server temporarily unavailable
		StatusCode.Unknown,           // Unknown error (often network issues)
		StatusCode.DeadlineExceeded,  // Server took too long to respond
		StatusCode.ResourceExhausted, // Server overloaded
	];

    internal MethodConfig GetRetryMethodConfig() {
        var retryPolicy = new RetryPolicy {
            MaxAttempts       = MaxAttempts,
            InitialBackoff    = InitialBackoff,
            MaxBackoff        = MaxBackoff,
            BackoffMultiplier = BackoffMultiplier
        };

        foreach (var statusCode in RetryableStatusCodes)
            retryPolicy.RetryableStatusCodes.Add(statusCode);

        return new() {
            Names       = { MethodName.Default },
            RetryPolicy = retryPolicy
        };
    }
}
