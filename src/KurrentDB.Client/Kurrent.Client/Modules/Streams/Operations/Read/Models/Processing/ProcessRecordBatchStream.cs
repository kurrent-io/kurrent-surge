using System.Threading.Channels;

namespace Kurrent.Client.Streams;

/// <summary>
/// A record batch stream that includes the server-assigned subscription identifier.
/// </summary>
/// <remarks>
/// <para>
/// Extends <see cref="RecordBatchStream"/> with the subscription ID returned from
/// the server during subscription confirmation.
/// </para>
/// </remarks>
record ProcessRecordBatchStream : RecordBatchStream {
    internal ProcessRecordBatchStream(Func<Channel<ReadRecordsResult>> channelFactory, int batchSize, TimeSpan timeout, Guid subscriptionId)
        : base(channelFactory, batchSize, timeout) => SubscriptionId = subscriptionId;

    /// <summary>
    /// Gets the server-assigned subscription identifier.
    /// </summary>
    public Guid SubscriptionId { get; }
}
