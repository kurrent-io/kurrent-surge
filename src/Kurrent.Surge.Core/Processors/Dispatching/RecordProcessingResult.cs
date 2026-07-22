namespace Kurrent.Surge.Streaming;

/// <summary>
/// Outcome of dispatching a record batch to one processor shape.
/// </summary>
readonly record struct RecordProcessingResult(bool ShouldStop, string? Reason) {
    public static readonly RecordProcessingResult Continue = new(false, null);

    public static RecordProcessingResult Stop(string? reason) =>
        new(true, reason);
}
