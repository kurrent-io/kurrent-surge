namespace Kurrent.Surge.Client.Streams;

/// <summary>
/// A filter that matches all records.
/// </summary>
[PublicAPI]
public sealed record EmptyFilter : RecordFilter {
    public override string Expression { get; init; } = "";

    public override bool IsMatch(Record record) => true;
}
