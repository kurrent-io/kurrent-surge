using Kurrent;
using Kurrent.Client.Exceptions;

namespace KurrentDB.Client;

/// <summary>
/// Exception thrown when a required metadata property is missing.
/// </summary>
public class RequiredMetadataPropertyMissingException : KurrentException {
	/// <summary>
	/// Constructs a new <see cref="RequiredMetadataPropertyMissingException"/>.
	/// </summary>
	/// <param name="missingMetadataProperty"></param>
	/// <param name="innerException"></param>
	public RequiredMetadataPropertyMissingException(string missingMetadataProperty, Exception? innerException = null) :
		base(ErrorCode.FromType<RequiredMetadataPropertyMissingException>(), $"Required metadata property {missingMetadataProperty} is missing", innerException) { }
}
