using Kurrent.Client.Exceptions;
using Kurrent.Variant;

namespace Kurrent.Client.Registry;

[PublicAPI]
public partial class CreateSchemaError : IVariantException<
    Exception> {
    public static CreateSchemaError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<CreateSchemaError>();
}

[PublicAPI]
public partial class GetSchemaError : IVariantException<
    Exception> {
    public static GetSchemaError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<GetSchemaError>();
}

[PublicAPI]
public partial class GetSchemaVersionError : IVariantException<
    Exception> {
    public static GetSchemaVersionError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<GetSchemaVersionError>();
}

[PublicAPI]
public partial class DeleteSchemaError : IVariantException<
    Exception> {
    public static DeleteSchemaError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<DeleteSchemaError>();
}

[PublicAPI]
public partial class CheckSchemaCompatibilityError : IVariantException<
    Exception> {
    public static CheckSchemaCompatibilityError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<CheckSchemaCompatibilityError>();
}
