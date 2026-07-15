namespace Kurrent.Surge.Client.Connectors;

public readonly record struct ConnectorId {
    public static readonly ConnectorId Unset = new("");

    ConnectorId(string value) => Value = value;

    public string Value { get; }

    public bool IsUnset => string.IsNullOrEmpty(Value);

    public override string ToString() => Value;

    public static ConnectorId From(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"ConnectorId '{value}' is not valid.", nameof(value))
            : new(value);

    public static implicit operator ConnectorId(string _) => From(_);

    public static implicit operator string(ConnectorId id) => id.Value;
}
