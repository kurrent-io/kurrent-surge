namespace Kurrent.Surge.Client.Streams;

/// <summary>
/// Excludes every record the server or a projection wrote for itself: any record whose stream name
/// or schema name starts with <c>$</c>. The default filter for application reads of the log.
/// </summary>
/// <remarks>
/// Routes through <c>$all</c> with a server-side <c>stream_identifier</c> regex that drops whole
/// system streams before the wire. The schema-name half runs on the client, because a hard delete
/// writes a <c>$streamDeleted</c> record into the user's own stream.
/// </remarks>
sealed record SystemRecordFilter : RecordFilter {
    public const string StreamExpression = @"^[^\$]";

    public override string Expression { get; init; } = StreamExpression;

    public override bool IsMatch(Record record) => !IsSystemRecord(record);

    public static bool IsSystemRecord(Record record) =>
        record.Stream.Value.StartsWith('$') || record.Schema.Schema.Value.StartsWith('$');
}
