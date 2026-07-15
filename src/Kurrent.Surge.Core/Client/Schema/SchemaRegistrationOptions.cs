namespace Kurrent.Surge.Client.Schema;

/// <summary>
/// Configuration for schema registration behavior
/// </summary>
public record SchemaRegistrationOptions {
	/// <summary>
	/// Controls schema registration behavior
	/// </summary>
	public SchemaRegistrationMode RegistrationMode { get; init; } = SchemaRegistrationMode.Manual;

	/// <summary>
	/// Controls schema validation behavior.
	/// </summary>
	public SchemaValidationMode ValidationMode { get; init; } = SchemaValidationMode.Disabled;

	public static SchemaRegistrationOptions ManualMap => new() {
		RegistrationMode = SchemaRegistrationMode.Manual,
		ValidationMode   = SchemaValidationMode.Disabled
	};

	public static SchemaRegistrationOptions AutoRegister => new() {
		RegistrationMode = SchemaRegistrationMode.Auto,
		ValidationMode   = SchemaValidationMode.Enabled
	};

	public static SchemaRegistrationOptions ManualRegister => new() {
		RegistrationMode = SchemaRegistrationMode.Manual,
		ValidationMode   = SchemaValidationMode.Enabled
	};

	public static SchemaRegistrationOptions ServerPolicy => new() {
		RegistrationMode = SchemaRegistrationMode.ServerPolicy,
		ValidationMode   = SchemaValidationMode.Enabled
	};
}

/// <summary>
/// Defines how schema registration should be handled
/// </summary>
public enum SchemaRegistrationMode {
	/// <summary>
	/// Automatically register schemas when they are first encountered
	/// </summary>
	Auto,

	/// <summary>
	/// Do not register schemas automatically - schemas must be pre-registered
	/// </summary>
	Manual,

	/// <summary>
	/// Let the server policy determine the registration behavior
	/// </summary>
	ServerPolicy
}

/// <summary>
/// Defines how schema validation should be handled
/// </summary>
public enum SchemaValidationMode {
	/// <summary>
	/// Validate schemas against the registry
	/// </summary>
	Enabled,

	/// <summary>
	/// Skip validation against the registry
	/// </summary>
	Disabled,

	/// <summary>
	/// Let the server policy determine the validation behavior
	/// </summary>
	ServerPolicy
}
