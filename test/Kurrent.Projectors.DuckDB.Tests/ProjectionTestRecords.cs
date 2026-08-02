using Kurrent.Surge.Client.Processors.Gateway.Messages;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming;
using Kurrent.Surge.Streaming.Models;

namespace Kurrent.Projectors.DuckDB.Tests;

/// <summary>
/// Builds the records and batches a projection's <c>Apply</c> takes, so a shape can be driven without a
/// stream or a processor behind it.
/// </summary>
static class ProjectionTestRecords {
    /// <summary>
    /// A record a projection will dispatch. Dispatch keys on the decoded CLR type, so both
    /// <c>Value</c> and <c>ValueType</c> have to be set — the wire format never comes into it here.
    /// </summary>
    public static Record Decoded<T>(T message, long logPosition) where T : class =>
        new() {
            Id        = Guid.NewGuid(),
            Position  = At(logPosition),
            Value     = message,
            ValueType = typeof(T)
        };

    /// <summary>A record that carries no decoded value, and so matches no handler.</summary>
    public static Record Undecoded(long logPosition) =>
        new() {
            Id       = Guid.NewGuid(),
            Position = At(logPosition)
        };

    public static BatchContext BatchOf(params Record[] records) =>
        new() { Records = RecordBatch.From(records) };

    static RecordPosition At(long logPosition) =>
        RecordPosition.From(LogPosition.From(logPosition), Partition.From(0));
}
