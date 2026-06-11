using Grpc.Core;
using Kurrent.Client.Exceptions;

namespace Kurrent.Client.Tests;

/// <summary>
/// Shared transient-error defaults for <c>EventuallyPasses</c> assertions in this test suite.
/// </summary>
public static class EventuallyDefaults {
    public static readonly Type[] TransientErrors = [
        typeof(SchemaNotFoundException),
        typeof(ConnectorNotFoundException)
    ];

    public static EventuallyOptions RetryOnTransients(CancellationToken ct = default) {
        var options = new EventuallyOptions { CancellationToken = ct }.RetryOn(TransientErrors);

        // Generic gRPC status codes are not wrapped in typed exceptions, so an
        // eventually-consistent read surfaces as a raw RpcException(NotFound) until
        // the resource becomes visible. The type list cannot express a status-code
        // match, hence the predicate extension.
        var retryOnListed = options.IsTransient;
        options.IsTransient = ex => retryOnListed(ex) || ex is RpcException { StatusCode: StatusCode.NotFound };

        return options;
    }
}
