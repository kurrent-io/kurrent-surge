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
    extension(SchemaNotFoundException) {
        public static SchemaNotFoundException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, SchemaNotFoundException.Code, (d, m, i) => new SchemaNotFoundException(d, m, i) {
                Schema = ToSchemaName(status.GetDetail<RegistryErrors.SchemaNotFoundErrorDetails>()?.Schema)
            });

        public static OperationExceptionParser Match(SchemaName schema) =>
            OnStatusCode(StatusCode.NotFound, () => new SchemaNotFoundException(schema));

        public static OperationExceptionParser Match(string message) =>
            OnStatusCode(StatusCode.NotFound, () => new SchemaNotFoundException(message));
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
