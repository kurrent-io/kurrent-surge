namespace Kurrent.Client;

/// <summary>
/// Marks a class as representing a Kurrent operation error exception.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class KurrentOperationErrorAttribute(object errorReason) : Attribute {
    public ErrorCode ErrorCode { get; } = ErrorCode.From(errorReason.ToString() ?? "");
}
