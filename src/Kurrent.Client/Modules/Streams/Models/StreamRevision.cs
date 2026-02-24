namespace Kurrent.Client.Streams;

/// <summary>
/// Represents a stream name paired with its revision number.
/// </summary>
[PublicAPI]
public readonly record struct StreamRevision(StreamName Stream, Revision Revision);
