namespace Kurrent.Client.Connectors;

public readonly record struct ConnectorId(string Value) {
    public string Value { get; } = Value;

    public override string ToString() => Value;

    public static ConnectorId From(string value) {
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"ConnectorId '{value}' is not valid.", nameof(value))
            : new(value);
    }

    public static implicit operator ConnectorId(string _)  => From(_);
    public static implicit operator string(ConnectorId id) => id.ToString();
}
