namespace Kurrent.Client.Indexes;

[PublicAPI]
public readonly record struct IndexName {
    public static readonly IndexName Unset = new("");

    IndexName(string value) => Value = value;

    public string Value { get; }

    public bool IsUnset => string.IsNullOrEmpty(Value);

    public override string ToString() => Value ?? "";

    public static IndexName From(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"IndexName '{value}' is not valid.", nameof(value))
            : new(value);

    public static implicit operator IndexName(string value) => From(value);

    public static implicit operator string(IndexName name) => name.Value;
}
