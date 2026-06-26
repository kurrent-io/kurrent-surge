// ReSharper disable CheckNamespace

using Kurrent.Client.Registry;
using Kurrent.Schema;
using KurrentDB.Protocol.V2.Registry.Errors;
using static System.String;

using Status = Google.Rpc.Status;

namespace Kurrent.Client.Exceptions;

[KurrentOperationError(RegistryError.SchemaNotFound)]
public partial class SchemaNotFoundException {
    public SchemaName? Schema { get; private set; }

    public SchemaNotFoundException(SchemaName schema)
        : this(Format("Schema '{0}' was not found.", schema)) {
        Schema = schema;
    }

    public void AfterParseRpcStatus(Status status, SchemaNotFoundException self) {
        var details = status.GetDetail<SchemaNotFoundErrorDetails>();
        if (details is null) return;
        self.Schema = SchemaName.From(details.Schema);
    }
}

[KurrentOperationError(RegistryError.SchemaAlreadyExists)]
public partial class SchemaAlreadyExistsException {
    public SchemaName? Schema { get; private set; }

    public SchemaAlreadyExistsException(SchemaName schema)
        : this(Format("Schema '{0}' already exists.", schema)) {
        Schema = schema;
    }

    public void AfterParseRpcStatus(Status status, SchemaAlreadyExistsException self) {
        var details = status.GetDetail<SchemaAlreadyExistsErrorDetails>();
        if (details is null) return;
        self.Schema = SchemaName.From(details.Schema);
    }
}

[KurrentOperationError(RegistryError.SchemaDeleted)]
public partial class SchemaDeletedException {
    public SchemaName? Schema { get; private set; }

    public SchemaDeletedException(SchemaName schema)
        : this(Format("Schema '{0}' has been deleted.", schema)) {
        Schema = schema;
    }

    public void AfterParseRpcStatus(Status status, SchemaDeletedException self) {
        var details = status.GetDetail<SchemaDeletedErrorDetails>();
        if (details is null) return;
        self.Schema = SchemaName.From(details.Schema);
    }
}

public class SchemaIncompatibleException : KurrentException {
    public SchemaCompatibilityErrors Errors { get; }

    public SchemaIncompatibleException(SchemaCompatibilityErrors errors)
        : base(ErrorCode.From("SchemaIncompatible"),
            "Schema compatibility check failed.") {
        Errors = errors;
    }
}
