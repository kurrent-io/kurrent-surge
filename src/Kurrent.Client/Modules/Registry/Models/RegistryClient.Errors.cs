using Kurrent.Client.Exceptions;
using Kurrent.Variant;

namespace Kurrent.Client.Registry;

[PublicAPI]
public partial class CreateSchemaError : IVariantException<
    SchemaAlreadyExistsException,
    Exception> {
    public static CreateSchemaError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<CreateSchemaError>(
            SchemaAlreadyExistsException.From);
}

[PublicAPI]
public partial class GetSchemaError : IVariantException<
    SchemaNotFoundException,
    SchemaDeletedException,
    Exception> {
    public static GetSchemaError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<GetSchemaError>(
            SchemaNotFoundException.From,
            SchemaDeletedException.From);
}

[PublicAPI]
public partial class GetSchemaVersionError : IVariantException<
    SchemaNotFoundException,
    SchemaDeletedException,
    Exception> {
    public static GetSchemaVersionError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<GetSchemaVersionError>(
            SchemaNotFoundException.From,
            SchemaDeletedException.From);
}

[PublicAPI]
public partial class DeleteSchemaError : IVariantException<
    SchemaNotFoundException,
    SchemaDeletedException,
    Exception> {
    public static DeleteSchemaError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<DeleteSchemaError>(
            SchemaNotFoundException.From,
            SchemaDeletedException.From);
}

[PublicAPI]
public partial class CheckSchemaCompatibilityError : IVariantException<
    SchemaNotFoundException,
    SchemaDeletedException,
    SchemaIncompatibleException,
    Exception> {
    public static CheckSchemaCompatibilityError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<CheckSchemaCompatibilityError>(
            SchemaNotFoundException.From,
            SchemaDeletedException.From);
}

[PublicAPI]
public partial class ListSchemasError : IVariantException<
    Exception> {
    public static ListSchemasError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<ListSchemasError>();
}

[PublicAPI]
public partial class RegisterSchemaVersionError : IVariantException<
    SchemaNotFoundException,
    SchemaDeletedException,
    Exception> {
    public static RegisterSchemaVersionError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<RegisterSchemaVersionError>(
            SchemaNotFoundException.From,
            SchemaDeletedException.From);
}
