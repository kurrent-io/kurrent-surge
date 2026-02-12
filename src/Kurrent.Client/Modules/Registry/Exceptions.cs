// ReSharper disable CheckNamespace

using Kurrent.Client.Registry;
using KurrentDB.Protocol.V2.Registry.Errors;
using Resources;
using static System.String;

namespace Kurrent.Client.Exceptions;

[KurrentOperationError(RegistryError.SchemaNotFound)]
public partial class SchemaNotFoundException {
    public SchemaNotFoundException(SchemaName schema)
        : this(Format(ErrorMessages.SchemaNotFoundException, schema)) { }
}

[KurrentOperationError(RegistryError.SchemaAlreadyExists)]
public partial class SchemaAlreadyExistsException {
    public SchemaAlreadyExistsException(SchemaName schema)
        : this(Format(ErrorMessages.SchemaAlreadyExistsException, schema)) { }
}

[KurrentOperationError(RegistryError.SchemaDeleted)]
public partial class SchemaDeletedException {
    public SchemaDeletedException(SchemaName schema)
        : this(Format(ErrorMessages.SchemaDeletedException, schema)) { }
}

public class SchemaIncompatibleException : KurrentException {
    public SchemaCompatibilityErrors Errors { get; }

    public SchemaIncompatibleException(SchemaCompatibilityErrors errors)
        : base(ErrorCode.From("SchemaIncompatible"),
            ErrorMessages.SchemaIncompatibleException) {
        Errors = errors;
    }
}
