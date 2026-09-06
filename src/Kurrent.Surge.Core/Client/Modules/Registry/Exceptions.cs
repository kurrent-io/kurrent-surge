// ReSharper disable CheckNamespace

using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Errors;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Client.Exceptions;

[PublicAPI]
public class SchemaNotFoundException : KurrentException {
    public static Errors.ErrorCode Code { get; } = Errors.ErrorCode.From("SCHEMA_NOT_FOUND");

    public SchemaNotFoundException(SchemaName schema)
        : this($"Schema '{schema}' was not found.") => Schema = schema;

    public SchemaNotFoundException(string message) : base(Code, message) { }

    public SchemaNotFoundException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }

    public SchemaName? Schema { get; init; }
}

[PublicAPI]
public class SchemaAlreadyExistsException : KurrentException {
    public static Errors.ErrorCode Code { get; } = Errors.ErrorCode.From("SCHEMA_ALREADY_EXISTS");

    public SchemaAlreadyExistsException(SchemaName schema)
        : this($"Schema '{schema}' already exists.") => Schema = schema;

    public SchemaAlreadyExistsException(string message) : base(Code, message) { }

    public SchemaAlreadyExistsException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }

    public SchemaName? Schema { get; init; }
}

[PublicAPI]
public class SchemaDeletedException : KurrentException {
    public static Errors.ErrorCode Code { get; } = Errors.ErrorCode.From("SCHEMA_DELETED");

    public SchemaDeletedException(SchemaName schema)
        : this($"Schema '{schema}' has been deleted.") => Schema = schema;

    public SchemaDeletedException(string message) : base(Code, message) { }

    public SchemaDeletedException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }

    public SchemaName? Schema { get; init; }
}

/// <summary>
/// A schema failed its compatibility check. Not part of the server error-code vocabulary — the server
/// reports the failing rules rather than a single code — so it carries a locally derived code.
/// </summary>
[PublicAPI]
public class SchemaIncompatibleException(SchemaCompatibilityErrors errors)
    : KurrentException(Surge.Errors.ErrorCode.From("SchemaIncompatible"), "Schema compatibility check failed.") {
    public SchemaCompatibilityErrors Errors { get; } = errors;
}
