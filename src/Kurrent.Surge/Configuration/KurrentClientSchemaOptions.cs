using Kurrent.Surge.Schema.Naming;
using Kurrent.Validation;

namespace Kurrent.Surge.Client;

/// <summary>
/// Defines schema settings for a KurrentDB client: registration, validation, and naming.
/// </summary>
/// <remarks>
/// The server policy overrides these options when it requires or prohibits an operation.
/// The client decides only what the server leaves optional. Server policy resolution is
/// not implemented yet — no requirement is enforced.
/// </remarks>
[PublicAPI]
public record KurrentClientSchemaOptions : SelfValidating<KurrentClientSchemaOptions, KurrentClientSchemaOptions.Validator> {
	/// <summary>
	/// Controls schema registration behavior.
	/// Defaults to <see cref="SchemaRegistrationMode.Manual"/>.
	/// </summary>
	public SchemaRegistrationMode RegistrationMode { get; set; } = SchemaRegistrationMode.Manual;

	/// <summary>
	/// Controls schema validation against the registry.
	/// Defaults to <see cref="SchemaValidationMode.Disabled"/>.
	/// </summary>
	public SchemaValidationMode ValidationMode { get; set; } = SchemaValidationMode.Disabled;

	/// <summary>
	/// The strategy used for generating schema names from message types.
	/// Defaults to <see cref="SchemaNameStrategies.TypeName"/>.
	/// </summary>
	public ISchemaNameStrategy NameStrategy { get; set; } = SchemaNameStrategies.TypeName();

	/// <summary>
	/// The registry is not involved. Schema names map locally, produced records carry names
	/// only, and records with unknown schemas pass through reads undecoded. The default.
	/// </summary>
	public static KurrentClientSchemaOptions Disabled => new() {
		RegistrationMode = SchemaRegistrationMode.Manual,
		ValidationMode   = SchemaValidationMode.Disabled
	};

	/// <summary>
	/// Registers the schemas of produced messages automatically and never validates.
	/// Consuming stays tolerant: records with unknown schemas pass through reads undecoded.
	/// </summary>
	public static KurrentClientSchemaOptions AutoRegister => new() {
		RegistrationMode = SchemaRegistrationMode.Auto,
		ValidationMode   = SchemaValidationMode.Disabled
	};

	/// <summary>
	/// Never registers. Verifies every catalog schema is pre-registered and compatible before
	/// the first operation, and fails reads that carry unknown or incompatible schemas.
	/// </summary>
	public static KurrentClientSchemaOptions Strict => new() {
		RegistrationMode = SchemaRegistrationMode.Manual,
		ValidationMode   = SchemaValidationMode.Enabled
	};

    public class Validator : Validator<Validator, KurrentClientSchemaOptions> { }
}

/// <summary>
/// Defines how schema registration is handled.
/// </summary>
public enum SchemaRegistrationMode {
	/// <summary>
	/// Automatically register schemas when they are first encountered.
	/// </summary>
	Auto,

	/// <summary>
	/// Do not register schemas automatically - schemas must be pre-registered.
	/// </summary>
	Manual
}

/// <summary>
/// Defines how schema validation is handled.
/// </summary>
public enum SchemaValidationMode {
	/// <summary>
	/// Validate schemas against the registry.
	/// </summary>
	Enabled,

	/// <summary>
	/// Skip validation against the registry.
	/// </summary>
	Disabled
}
