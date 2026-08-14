// using Kurrent.Surge.Client.Features;

using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Client.Schema;

/// <summary>
/// The server's voice: the registration, validation, and format requirements the server
/// enforces. Never user-configured — populated from server capabilities when
/// <c>FromServerFeatures</c> goes live.
/// </summary>
internal record SchemaRegistryPolicy {
	public static readonly SchemaRegistryPolicy Disabled = new() { Enabled = false };

	public static readonly SchemaRegistryPolicy NoRequirements = new() {
		Enabled                 = true,
		RegistrationRequirement = PolicyRequirement.Optional,
		ValidationRequirement   = PolicyRequirement.Optional,
		EnforcedDataFormat      = SchemaDataFormat.Unspecified
	};

	/// <summary>
	/// Whether the schema registry is enabled on the server
	/// </summary>
	public bool Enabled { get; init; } = true;

	/// <summary>
	/// The registration requirement enforced by the server
	/// </summary>
	public PolicyRequirement RegistrationRequirement { get; init; } = PolicyRequirement.Optional;

	/// <summary>
	/// The validation requirement enforced by the server
	/// </summary>
	public PolicyRequirement ValidationRequirement { get; init; } = PolicyRequirement.Optional;

	/// <summary>
	/// The format required by the server, or Unspecified if not enforced
	/// </summary>
	public SchemaDataFormat EnforcedDataFormat { get; init; } = SchemaDataFormat.Unspecified;

    /// <summary>
    /// Resolves the effective policy: the server's requirements override the client's
    /// options, and the client decides only what the server leaves optional.
    /// </summary>
    /// <param name="options">The client's schema options.</param>
    /// <param name="stream">The stream identifier associated with the schema.</param>
    /// <param name="dataFormat">The data format used by the serializer, which may be considered in resolution if the server does not enforce a specific format.</param>
    /// <returns>A <see cref="ResolvedSchemaRegistryPolicy"/> object that encapsulates the resolved settings.</returns>
    public ResolvedSchemaRegistryPolicy Resolve(KurrentClientSchemaOptions options, string? stream, SchemaDataFormat dataFormat = SchemaDataFormat.Unspecified) {
		// If schema registry is disabled on server, create a policy with local operations only
		if (!Enabled)
			return new(false, false, SchemaDataFormat.Unspecified, stream);

		var autoRegister = options.RegistrationMode switch {
			SchemaRegistrationMode.Auto => RegistrationRequirement is not PolicyRequirement.Prohibited,
			_                           => RegistrationRequirement is PolicyRequirement.Required
		};

		var validate = options.ValidationMode switch {
			SchemaValidationMode.Enabled => ValidationRequirement is not PolicyRequirement.Prohibited,
			_                            => ValidationRequirement is PolicyRequirement.Required
		};

		var actualDataFormat = EnforcedDataFormat != SchemaDataFormat.Unspecified
			? EnforcedDataFormat
			: dataFormat;

		return new(autoRegister, validate, actualDataFormat, stream);
	}

	// /// <summary>
	// /// Creates a schema registry policy from server capabilities.
	// /// </summary>
	// /// <param name="features">The server capabilities object containing schema registry information.</param>
	// /// <returns>
	// /// A configured <see cref="SchemaRegistryPolicy"/> based on server capabilities,
	// /// or <see cref="SchemaRegistryPolicy.Disabled"/> if schema registry is not supported.
	// /// </returns>
	// public static SchemaRegistryPolicy FromServerFeatures(ServerFeatures features) {
	// 	// Check if schema registry capability exists
	// 	var capability = features.GetFeature("SchemaRegistry");
	//
	// 	// If capability is null or not enabled, return disabled policy
	// 	if (capability is null && capability is not { Enabled: true })
	// 		return Disabled;
	//
	// 	var registrationSupport = capability.GetRequirementValue("RegistrationSupport", false);
	// 	var validationSupport   = capability.GetRequirementValue("ValidationSupport", false);
	//
	// 	// If neither registration nor validation is supported, consider it disabled
	// 	if (!registrationSupport && !validationSupport)
	// 		return Disabled;
	//
	// 	var registration   = capability.GetRequirementValue("Registration", PolicyRequirement.Optional);
	// 	var validation     = capability.GetRequirementValue("Validation", PolicyRequirement.Optional);
	// 	var enforcedFormat = capability.GetRequirementValue("DataFormat", SchemaDataFormat.Unspecified);
	//
	// 	return new SchemaRegistryPolicy {
	// 		Enabled                 = true,
	// 		RegistrationRequirement = registration,
	// 		ValidationRequirement   = validation,
	// 		EnforcedDataFormat      = enforcedFormat
	// 	};
	// }
}

/// <summary>
/// Defines the policy requirement level for a server feature
/// </summary>
public enum PolicyRequirement {
	/// <summary>
	/// The feature is optional - client controls the behavior
	/// </summary>
	Optional,

	/// <summary>
	/// The feature is required - must be enabled
	/// </summary>
	Required,

	/// <summary>
	/// The feature is prohibited - must be disabled
	/// </summary>
	Prohibited
}
