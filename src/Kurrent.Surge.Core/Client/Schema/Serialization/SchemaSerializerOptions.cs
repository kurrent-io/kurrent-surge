namespace Kurrent.Surge.Client.Schema.Serialization;

public record SchemaSerializerOptions {
	/// <summary>
	/// Configuration options for schema registration, validation, and type mapping behaviors.
	/// </summary>
	public SchemaRegistrationOptions SchemaRegistration { get; init; } = new();
}
