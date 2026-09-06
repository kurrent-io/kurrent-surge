using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Errors;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Codecs;

/// <summary>
/// Base class for codec exceptions.
/// Provides a common structure for exceptions related to encoding and decoding,
/// including metadata about the schema data format used.
/// Inherits from <see cref="KurrentException"/> to maintain consistency in error handling across the Kurrent client.
/// </summary>
public abstract class SchemaCodecException(string reason, SchemaDataFormat dataFormat, Exception? innerException = null)
    : KurrentException(Errors.ErrorCode.FromType<SchemaCodecException>(), reason, innerException) {
	public SchemaDataFormat DataFormat { get; } = dataFormat;
}

/// <summary>
/// Exception thrown when encoding a value fails.
/// </summary>
public class SchemaCodecEncodingException(SchemaDataFormat dataFormat, Type messageType, Exception? innerException = null)
	: SchemaCodecException($"Failed to encode '{messageType}' using {dataFormat} format.", dataFormat, innerException) {
	public Type MessageType { get; } = messageType;
}

/// <summary>
/// Exception thrown when decoding data fails.
/// </summary>
public class SchemaCodecDecodingException(SchemaDataFormat dataFormat, SchemaName schemaName, Exception? innerException = null)
	: SchemaCodecException($"Failed to decode data with schema '{schemaName}' using {dataFormat} format.", dataFormat, innerException) {
	public SchemaName SchemaName { get; } = schemaName;
}

/// <summary>
/// Exception thrown when a schema format is not supported by the codec.
/// </summary>
public class UnsupportedSchemaDataFormatException(SchemaDataFormat expectedFormat, SchemaDataFormat actualFormat)
	: SchemaCodecException($"Unsupported schema format. Expected {expectedFormat}, but got {actualFormat}.", actualFormat) {
	public SchemaDataFormat ExpectedFormat { get; } = expectedFormat;
	public SchemaDataFormat ActualFormat   { get; } = actualFormat;
}

/// <summary>
/// Exception thrown when a schema validation fails.
/// </summary>
public class SchemaValidationException(SchemaDataFormat dataFormat, SchemaIdentifier schemaIdentifier, Type messageType, IReadOnlyList<SchemaCompatibilityError> validationErrors)
	: KurrentException(Errors.ErrorCode.FromType<SchemaValidationException>(), ErrorMessage(messageType, schemaIdentifier, validationErrors)) {
	public SchemaDataFormat                        DataFormat       { get; } = dataFormat;
	public Type                                    MessageType      { get; } = messageType;
	public SchemaIdentifier                        SchemaIdentifier { get; } = schemaIdentifier;
	public IReadOnlyList<SchemaCompatibilityError> ValidationErrors { get; } = validationErrors;

	static string ErrorMessage(Type messageType, SchemaIdentifier schemaIdentifier, IReadOnlyList<SchemaCompatibilityError> validationErrors) {
        var schemaIdentifierText = schemaIdentifier.Match(
            schemaName => $"schema '{schemaName}'",
            schemaVersionId => $"schema version '{schemaVersionId}'");

		return $"Schema validation failed for message type '{messageType.FullName}' with {schemaIdentifierText}."
		     + $"Found {validationErrors.Count} errors:{Environment.NewLine}{string.Join(Environment.NewLine, validationErrors.Select(error => $" - {error}"))}";
	}
}
