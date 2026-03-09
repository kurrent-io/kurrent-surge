namespace Kurrent.Client.Streams;

/// <summary>
/// A filter that matches all records.
/// </summary>
[PublicAPI]
public sealed record EmptyFilter : IRecordFilter {
    public StreamName TargetStream => StreamName.Unset;

    public string Expression => string.Empty;

    public bool IsMatch(Record record) => true;
}
