using Kurrent;
using Kurrent.Client.Exceptions;

namespace KurrentDB.Client;

/// <summary>
/// The exception that is thrown when <see cref="System.Net.EndPoint"/> discovery fails.
/// </summary>
class DiscoveryException : KurrentException {
	/// <summary>
	/// Constructs a new <see cref="DiscoveryException"/>.
	/// </summary>
	/// <param name="reason"></param>
	/// <param name="innerException"></param>
	[Obsolete]
	public DiscoveryException(string reason, Exception? innerException = null) : base(ErrorCode.FromType<DiscoveryException>(), reason, innerException) =>
		MaxDiscoverAttempts = 0;

	/// <summary>
	/// Constructs a new <see cref="DiscoveryException"/>.
	/// </summary>
	/// <param name="maxDiscoverAttempts">The configured number of discovery attempts.</param>
	public DiscoveryException(int maxDiscoverAttempts) : base(ErrorCode.FromType<DiscoveryException>(), $"Failed to discover candidate in {maxDiscoverAttempts} attempts.") =>
		MaxDiscoverAttempts = maxDiscoverAttempts;

	/// <summary>
	/// The configured number of discovery attempts.
	/// </summary>
	public int MaxDiscoverAttempts { get; }
}
