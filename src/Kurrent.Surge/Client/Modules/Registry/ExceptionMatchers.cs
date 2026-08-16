// ReSharper disable CheckNamespace

using Google.Rpc;
using Grpc.Core;
using Kurrent.Surge.Schema;

using RegistryErrors = KurrentDB.Protocol.V2.Registry.Errors;
using Status = Google.Rpc.Status;

using static Kurrent.Surge.Client.Exceptions.MatcherHelpers;
using static Kurrent.Surge.Client.Exceptions.SchemaNameHelper;

namespace Kurrent.Surge.Client.Exceptions;

static class RegistryExceptionMatchers {
    // A not-found reaches the registry's error mapping wrapped in an AggregateException when the
    // schema's own stream does not exist — routine on a cold cluster, where those streams are
    // still being created. The server's type test does not see through the wrapper, so the
    // mapping falls through to its catch-all and reports Internal. The wrapped exception's type
    // name is the only signal left on the wire.
    //
    //   Status(StatusCode="Internal", Detail="One or more errors occurred.
    //          (Exception of type 'KurrentDB.Core.Services.Transport.Enumerators.ReadResponseException
    //           +StreamNotFound' was thrown.)")
    //
    // Drop the second clause once the server unwraps before classifying and sends NotFound.
    static bool IsSchemaAbsent(Status status) =>
        (StatusCode)status.Code == StatusCode.NotFound
     || ((StatusCode)status.Code == StatusCode.Internal
      && status.Message.Contains("ReadResponseException+StreamNotFound", StringComparison.Ordinal));

    extension(SchemaNotFoundException) {
        public static SchemaNotFoundException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, SchemaNotFoundException.Code, (d, m, i) => new SchemaNotFoundException(d, m, i) {
                Schema = ToSchemaName(status.GetDetail<RegistryErrors.SchemaNotFoundErrorDetails>()?.Schema)
            });

        public static OperationExceptionParser Match(SchemaName schema) =>
            (status, _) => IsSchemaAbsent(status) ? new SchemaNotFoundException(schema) : null;

        public static OperationExceptionParser Match(string message) =>
            (status, _) => IsSchemaAbsent(status) ? new SchemaNotFoundException(message) : null;
    }
}

static class SchemaAlreadyExistsMatcher {
    extension(SchemaAlreadyExistsException) {
        public static SchemaAlreadyExistsException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, SchemaAlreadyExistsException.Code, (d, m, i) => new SchemaAlreadyExistsException(d, m, i) {
                Schema = ToSchemaName(status.GetDetail<RegistryErrors.SchemaAlreadyExistsErrorDetails>()?.Schema)
            });

        public static OperationExceptionParser Match(SchemaName schema) =>
            OnStatusCode(StatusCode.AlreadyExists, () => new SchemaAlreadyExistsException(schema));
    }
}

static class SchemaDeletedMatcher {
    extension(SchemaDeletedException) {
        public static SchemaDeletedException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, SchemaDeletedException.Code, (d, m, i) => new SchemaDeletedException(d, m, i) {
                Schema = ToSchemaName(status.GetDetail<RegistryErrors.SchemaDeletedErrorDetails>()?.Schema)
            });

        public static OperationExceptionParser Match(SchemaName schema) =>
            OnStatusCode(StatusCode.FailedPrecondition, () => new SchemaDeletedException(schema));

        public static OperationExceptionParser Match(string message) =>
            OnStatusCode(StatusCode.FailedPrecondition, () => new SchemaDeletedException(message));
    }
}

static class SchemaNameHelper {
    public static SchemaName? ToSchemaName(string? schema) =>
        schema is { Length: > 0 } ? SchemaName.From(schema) : null;
}
