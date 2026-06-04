using Kurrent.Client.Streams;

namespace Kurrent.Streaming.Experimental;

interface IConsumerMetadata {
    /// <summary>
	/// Unique identifier of the consumer.
	/// </summary>
	public string ConsumerId { get; }
    
	/// <summary>
	/// The name of the subscription.
	/// </summary>
	public string Group { get; }

	/// <summary>
	/// The filter used to filter out the records sent by the server for processing.
	/// </summary>
	public RecordFilter Filter { get; }
}

/// <summary>
///  Represents a consumer that can consume records from a stream.
/// </summary>
interface IConsumer : IConsumerMetadata, IAsyncDisposable {
	/// <summary>
	///	Consumes the records from the stream.
	/// </summary>
	public IAsyncEnumerator<Record> Consume(CancellationToken stoppingToken = default);

	/// <summary>
	///	Tracks the consumed record position in memory and might trigger the commit of the next ready positions.
	/// </summary>
	public ValueTask<RecordPosition[]> Track(Record record, CancellationToken cancellationToken = default);
    
    /// <summary>
    ///	Commits all tracked positions effectively acknowledging all the records as processed.
    /// </summary>
    public ValueTask<RecordPosition[]> CommitAll(CancellationToken cancellationToken = default);

	/// <summary>
	/// Returns the latest positions for all partitions.
	/// </summary>
	public ValueTask<RecordPosition[]> GetLatestPositions(CancellationToken cancellationToken = default);
}