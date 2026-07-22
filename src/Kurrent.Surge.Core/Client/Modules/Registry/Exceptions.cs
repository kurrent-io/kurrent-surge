// ReSharper disable CheckNamespace

using Kurrent.Surge.Client.Errors;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Schema;
using static System.String;

namespace Kurrent.Surge.Client.Exceptions;

[PublicAPI]
public class SchemaNotFoundException : KurrentException {
    public static ErrorCode Code => KurrentErrorCodes.Registry.SchemaNotFound;

    public SchemaNotFoundException(SchemaName schema)
        : this(Format("Schema '{0}' was not found.", schema)) => Schema = schema;

    public SchemaNotFoundException(string message) : base(Code, message) { }

    public SchemaNotFoundException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }

    public SchemaName? Schema { get; init; }
}

[PublicAPI]
public class SchemaAlreadyExistsException : KurrentException {
    public static ErrorCode Code => KurrentErrorCodes.Registry.SchemaAlreadyExists;

    public SchemaAlreadyExistsException(SchemaName schema)
        : this(Format("Schema '{0}' already exists.", schema)) => Schema = schema;

    public SchemaAlreadyExistsException(string message) : base(Code, message) { }

    public SchemaAlreadyExistsException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }

    public SchemaName? Schema { get; init; }
}

[PublicAPI]
public class SchemaDeletedException : KurrentException {
    public static ErrorCode Code => KurrentErrorCodes.Registry.SchemaDeleted;

    public SchemaDeletedException(SchemaName schema)
        : this(Format("Schema '{0}' has been deleted.", schema)) => Schema = schema;

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
    : KurrentException(ErrorCode.From("SchemaIncompatible"), "Schema compatibility check failed.") {
    public SchemaCompatibilityErrors Errors { get; } = errors;
}
