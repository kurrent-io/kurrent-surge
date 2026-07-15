namespace Kurrent.Surge.Schema;

public readonly record struct SchemaName {
    public static readonly SchemaName Unset = new("");

    SchemaName(string value) => Value = value;

    public string Value { get; }

    public bool IsUnset => string.IsNullOrEmpty(Value);

    public override string ToString() => Value;

    public static SchemaName From(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"SchemaName '{value}' is not valid.", nameof(value))
            : new(value);

    public static implicit operator SchemaName(string _) => From(_);

    public static implicit operator string(SchemaName name) => name.Value;
}
